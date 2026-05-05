using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class BollingerBandsTests
{
    [Fact]
    public void Value_BeforeWarmup_ReturnsNull()
    {
        var bb = new BollingerBands(3);
        bb.Update(10m);
        bb.Update(20m);

        Assert.Null(bb.Value);
        Assert.False(bb.IsReady);
    }

    [Fact]
    public void Value_AtWarmup_ReturnsCorrectBands()
    {
        var bb = new BollingerBands(3, 2m);
        bb.Update(10m);
        bb.Update(20m);
        bb.Update(30m);

        Assert.True(bb.IsReady);
        var result = bb.Value!.Value;

        // Mean = 20, Variance = ((10-20)^2 + (20-20)^2 + (30-20)^2) / 3 = 200/3
        // StdDev = sqrt(200/3) ≈ 8.1650
        var mean = 20m;
        var variance = 200m / 3m;
        var stdDev = (decimal)Math.Sqrt((double)variance);

        Assert.Equal(mean, result.Middle);
        Assert.Equal(mean + 2m * stdDev, result.Upper, 10);
        Assert.Equal(mean - 2m * stdDev, result.Lower, 10);
        Assert.Equal((result.Upper - result.Lower) / mean, result.BandWidth, 10);
    }

    [Fact]
    public void Value_ConstantPrices_ZeroBandWidth()
    {
        var bb = new BollingerBands(3, 2m);
        bb.Update(50m);
        bb.Update(50m);
        bb.Update(50m);

        Assert.True(bb.IsReady);
        var result = bb.Value!.Value;

        Assert.Equal(50m, result.Middle);
        Assert.Equal(50m, result.Upper);
        Assert.Equal(50m, result.Lower);
        Assert.Equal(0m, result.BandWidth);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var bb = new BollingerBands(3);
        bb.Update(10m);
        bb.Update(20m);
        bb.Update(30m);
        Assert.True(bb.IsReady);

        bb.Reset();

        Assert.False(bb.IsReady);
        Assert.Null(bb.Value);
    }

    [Fact]
    public void Reset_ThenReplay_ProducesSameResult()
    {
        var bb = new BollingerBands(5);
        var data = new[] { 10m, 20m, 30m, 40m, 50m, 60m };

        foreach (var d in data) bb.Update(d);
        var firstResult = bb.Value;

        bb.Reset();
        foreach (var d in data) bb.Update(d);

        Assert.Equal(firstResult, bb.Value);
    }

    [Fact]
    public void Constructor_PeriodLessThanTwo_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BollingerBands(1));
    }
}
