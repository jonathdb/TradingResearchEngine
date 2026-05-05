using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class ExponentialMovingAverageTests
{
    [Fact]
    public void Value_BeforeWarmup_ReturnsNull()
    {
        var ema = new ExponentialMovingAverage(3);
        ema.Update(10m);
        ema.Update(20m);

        Assert.Null(ema.Value);
        Assert.False(ema.IsReady);
    }

    [Fact]
    public void Value_AtWarmup_ReturnsSeedSma()
    {
        var ema = new ExponentialMovingAverage(3);
        ema.Update(10m);
        ema.Update(20m);
        ema.Update(30m);

        Assert.True(ema.IsReady);
        // Seed = SMA of first 3 values = (10+20+30)/3 = 20
        Assert.Equal(20m, ema.Value);
    }

    [Fact]
    public void Value_AfterWarmup_AppliesExponentialSmoothing()
    {
        var ema = new ExponentialMovingAverage(3);
        // Multiplier = 2/(3+1) = 0.5
        ema.Update(10m);
        ema.Update(20m);
        ema.Update(30m);
        // Seed EMA = 20

        ema.Update(40m);
        // EMA = (40 - 20) * 0.5 + 20 = 30
        Assert.Equal(30m, ema.Value);

        ema.Update(50m);
        // EMA = (50 - 30) * 0.5 + 30 = 40
        Assert.Equal(40m, ema.Value);
    }

    [Fact]
    public void Reset_ClearsState_RequiresNewWarmup()
    {
        var ema = new ExponentialMovingAverage(3);
        ema.Update(10m);
        ema.Update(20m);
        ema.Update(30m);
        Assert.True(ema.IsReady);

        ema.Reset();

        Assert.False(ema.IsReady);
        Assert.Null(ema.Value);
    }

    [Fact]
    public void Reset_ThenReplay_ProducesSameResult()
    {
        var ema = new ExponentialMovingAverage(5);
        var data = new[] { 10m, 20m, 30m, 40m, 50m, 60m, 70m };

        foreach (var d in data) ema.Update(d);
        var firstResult = ema.Value;

        ema.Reset();
        foreach (var d in data) ema.Update(d);

        Assert.Equal(firstResult, ema.Value);
    }

    [Fact]
    public void Constructor_PeriodLessThanOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExponentialMovingAverage(0));
    }

    [Fact]
    public void Multiplier_CorrectFormula()
    {
        // Period 9 → multiplier = 2/(9+1) = 0.2
        var ema = new ExponentialMovingAverage(9);
        // Feed 9 values to seed
        for (int i = 1; i <= 9; i++) ema.Update(i * 10m);
        // Seed = (10+20+30+40+50+60+70+80+90)/9 = 50

        ema.Update(100m);
        // EMA = (100 - 50) * 0.2 + 50 = 60
        Assert.Equal(60m, ema.Value);
    }
}
