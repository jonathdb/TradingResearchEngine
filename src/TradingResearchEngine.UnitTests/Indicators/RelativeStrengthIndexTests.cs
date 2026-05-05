using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class RelativeStrengthIndexTests
{
    [Fact]
    public void Value_BeforeWarmup_ReturnsNull()
    {
        var rsi = new RelativeStrengthIndex(14);
        for (int i = 0; i < 14; i++) rsi.Update(100m + i);

        Assert.Null(rsi.Value);
        Assert.False(rsi.IsReady);
    }

    [Fact]
    public void Value_AtWarmup_ReturnsFirstRsi()
    {
        var rsi = new RelativeStrengthIndex(3);
        // Need period + 1 = 4 data points to produce first RSI
        rsi.Update(44m);  // count=1, no change
        rsi.Update(44.5m); // count=2, gain=0.5, loss=0
        rsi.Update(43.5m); // count=3, gain=0, loss=1
        rsi.Update(44.5m); // count=4, gain=1, loss=0

        Assert.True(rsi.IsReady);
        // AvgGain = (0.5 + 0 + 1) / 3 = 0.5
        // AvgLoss = (0 + 1 + 0) / 3 = 1/3
        // RS = 0.5 / (1/3) = 1.5
        // RSI = 100 - 100/(1+1.5) = 100 - 40 = 60
        Assert.Equal(60m, rsi.Value!.Value, 10);
    }

    [Fact]
    public void Value_AllGains_Returns100()
    {
        var rsi = new RelativeStrengthIndex(3);
        rsi.Update(10m);
        rsi.Update(20m);
        rsi.Update(30m);
        rsi.Update(40m);

        Assert.True(rsi.IsReady);
        // All gains, no losses → RSI = 100
        Assert.Equal(100m, rsi.Value);
    }

    [Fact]
    public void Value_AllLosses_Returns0()
    {
        var rsi = new RelativeStrengthIndex(3);
        rsi.Update(40m);
        rsi.Update(30m);
        rsi.Update(20m);
        rsi.Update(10m);

        Assert.True(rsi.IsReady);
        // All losses, no gains → AvgGain = 0, RS = 0, RSI = 100 - 100/(1+0) = 0
        Assert.Equal(0m, rsi.Value);
    }

    [Fact]
    public void Value_AfterWarmup_AppliesWilderSmoothing()
    {
        var rsi = new RelativeStrengthIndex(3);
        rsi.Update(44m);
        rsi.Update(44.5m); // gain=0.5
        rsi.Update(43.5m); // loss=1
        rsi.Update(44.5m); // gain=1
        // AvgGain = 1.5/3 = 0.5, AvgLoss = 1/3

        rsi.Update(45.5m); // gain=1
        // Wilder: AvgGain = (0.5*2 + 1)/3 = 2/3
        // Wilder: AvgLoss = (1/3*2 + 0)/3 = 2/9
        // RS = (2/3) / (2/9) = 3
        // RSI = 100 - 100/(1+3) = 75
        Assert.Equal(75m, rsi.Value!.Value, 10);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var rsi = new RelativeStrengthIndex(3);
        rsi.Update(10m);
        rsi.Update(20m);
        rsi.Update(30m);
        rsi.Update(40m);
        Assert.True(rsi.IsReady);

        rsi.Reset();

        Assert.False(rsi.IsReady);
        Assert.Null(rsi.Value);
    }

    [Fact]
    public void Reset_ThenReplay_ProducesSameResult()
    {
        var rsi = new RelativeStrengthIndex(5);
        var data = new[] { 44m, 44.5m, 43.5m, 44.5m, 45.5m, 44m, 46m };

        foreach (var d in data) rsi.Update(d);
        var firstResult = rsi.Value;

        rsi.Reset();
        foreach (var d in data) rsi.Update(d);

        Assert.Equal(firstResult, rsi.Value);
    }

    [Fact]
    public void Constructor_PeriodLessThanOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RelativeStrengthIndex(0));
    }
}
