using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class SimpleMovingAverageTests
{
    [Fact]
    public void Value_BeforeWarmup_ReturnsNull()
    {
        var sma = new SimpleMovingAverage(3);
        sma.Update(10m);
        sma.Update(20m);

        Assert.Null(sma.Value);
        Assert.False(sma.IsReady);
    }

    [Fact]
    public void Value_AfterWarmup_ReturnsCorrectAverage()
    {
        var sma = new SimpleMovingAverage(3);
        sma.Update(10m);
        sma.Update(20m);
        sma.Update(30m);

        Assert.True(sma.IsReady);
        Assert.Equal(20m, sma.Value);
    }

    [Fact]
    public void Value_RollingWindow_DropsOldestValue()
    {
        var sma = new SimpleMovingAverage(3);
        sma.Update(10m);
        sma.Update(20m);
        sma.Update(30m);
        sma.Update(40m);

        // Window: [20, 30, 40] → average = 30
        Assert.Equal(30m, sma.Value);
    }

    [Fact]
    public void Reset_ClearsState_RequiresNewWarmup()
    {
        var sma = new SimpleMovingAverage(3);
        sma.Update(10m);
        sma.Update(20m);
        sma.Update(30m);
        Assert.True(sma.IsReady);

        sma.Reset();

        Assert.False(sma.IsReady);
        Assert.Null(sma.Value);
    }

    [Fact]
    public void Reset_ThenReplay_ProducesSameResult()
    {
        var sma = new SimpleMovingAverage(3);
        var data = new[] { 10m, 20m, 30m, 40m, 50m };

        foreach (var d in data) sma.Update(d);
        var firstResult = sma.Value;

        sma.Reset();
        foreach (var d in data) sma.Update(d);

        Assert.Equal(firstResult, sma.Value);
    }

    [Fact]
    public void Constructor_PeriodLessThanOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimpleMovingAverage(0));
    }

    [Fact]
    public void Period_ReturnsConfiguredValue()
    {
        var sma = new SimpleMovingAverage(5);
        Assert.Equal(5, sma.Period);
    }

    [Fact]
    public void Value_PeriodOne_ReturnsLatestClose()
    {
        var sma = new SimpleMovingAverage(1);
        sma.Update(42m);

        Assert.True(sma.IsReady);
        Assert.Equal(42m, sma.Value);

        sma.Update(99m);
        Assert.Equal(99m, sma.Value);
    }
}
