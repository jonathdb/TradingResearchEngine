using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class RollingZScoreTests
{
    [Fact]
    public void Value_BeforeWarmup_ReturnsNull()
    {
        var zs = new RollingZScore(3);
        zs.Update(10m);
        zs.Update(20m);

        Assert.Null(zs.Value);
        Assert.False(zs.IsReady);
    }

    [Fact]
    public void Value_AtWarmup_ReturnsCorrectZScore()
    {
        var zs = new RollingZScore(3);
        zs.Update(10m);
        zs.Update(20m);
        zs.Update(30m);

        Assert.True(zs.IsReady);
        // Mean = 20, Variance = ((10-20)^2 + (20-20)^2 + (30-20)^2) / 3 = 200/3
        // StdDev = sqrt(200/3) ≈ 8.1650
        // Z-Score of latest (30) = (30 - 20) / sqrt(200/3) = 10 / 8.1650 ≈ 1.2247
        var mean = 20m;
        var variance = 200m / 3m;
        var stdDev = (decimal)Math.Sqrt((double)variance);
        var expected = (30m - mean) / stdDev;

        Assert.Equal(expected, zs.Value!.Value, 10);
    }

    [Fact]
    public void Value_ConstantPrices_ReturnsZero()
    {
        var zs = new RollingZScore(3);
        zs.Update(50m);
        zs.Update(50m);
        zs.Update(50m);

        Assert.True(zs.IsReady);
        Assert.Equal(0m, zs.Value);
    }

    [Fact]
    public void Value_LatestBelowMean_ReturnsNegative()
    {
        var zs = new RollingZScore(3);
        zs.Update(30m);
        zs.Update(20m);
        zs.Update(10m);

        Assert.True(zs.IsReady);
        // Mean = 20, latest = 10, Z-Score should be negative
        Assert.True(zs.Value < 0);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var zs = new RollingZScore(3);
        zs.Update(10m);
        zs.Update(20m);
        zs.Update(30m);
        Assert.True(zs.IsReady);

        zs.Reset();

        Assert.False(zs.IsReady);
        Assert.Null(zs.Value);
    }

    [Fact]
    public void Reset_ThenReplay_ProducesSameResult()
    {
        var zs = new RollingZScore(5);
        var data = new[] { 10m, 20m, 30m, 40m, 50m, 60m };

        foreach (var d in data) zs.Update(d);
        var firstResult = zs.Value;

        zs.Reset();
        foreach (var d in data) zs.Update(d);

        Assert.Equal(firstResult, zs.Value);
    }

    [Fact]
    public void Constructor_PeriodLessThanTwo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RollingZScore(1));
    }
}
