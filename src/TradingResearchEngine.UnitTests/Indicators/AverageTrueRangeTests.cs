using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class AverageTrueRangeTests
{
    [Fact]
    public void Value_BeforeWarmup_ReturnsNull()
    {
        var atr = new AverageTrueRange(3);
        atr.Update(100m, 105m, 95m);
        atr.Update(102m, 107m, 97m);

        Assert.Null(atr.Value);
        Assert.False(atr.IsReady);
    }

    [Fact]
    public void Value_AtWarmup_ReturnsSimpleAverageOfTrueRanges()
    {
        var atr = new AverageTrueRange(3);
        // Bar 1: TR = H-L = 105-95 = 10 (no previous close)
        atr.Update(100m, 105m, 95m);
        // Bar 2: TR = max(107-97, |107-100|, |97-100|) = max(10, 7, 3) = 10
        atr.Update(102m, 107m, 97m);
        // Bar 3: TR = max(110-98, |110-102|, |98-102|) = max(12, 8, 4) = 12
        atr.Update(105m, 110m, 98m);

        Assert.True(atr.IsReady);
        // ATR = (10 + 10 + 12) / 3 = 32/3 ≈ 10.6667
        var expected = 32m / 3m;
        Assert.Equal(expected, atr.Value);
    }

    [Fact]
    public void Value_AfterWarmup_AppliesWilderSmoothing()
    {
        var atr = new AverageTrueRange(3);
        atr.Update(100m, 105m, 95m);  // TR = 10
        atr.Update(102m, 107m, 97m);  // TR = 10
        atr.Update(105m, 110m, 98m);  // TR = 12, ATR = 32/3

        // Bar 4: TR = max(115-100, |115-105|, |100-105|) = max(15, 10, 5) = 15
        atr.Update(110m, 115m, 100m);
        // Wilder: ATR = ((32/3 * 2) + 15) / 3 = (64/3 + 15) / 3 = (64/3 + 45/3) / 3 = 109/9
        var expected = 109m / 9m;
        Assert.Equal(expected, atr.Value);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var atr = new AverageTrueRange(3);
        atr.Update(100m, 105m, 95m);
        atr.Update(102m, 107m, 97m);
        atr.Update(105m, 110m, 98m);
        Assert.True(atr.IsReady);

        atr.Reset();

        Assert.False(atr.IsReady);
        Assert.Null(atr.Value);
    }

    [Fact]
    public void Reset_ThenReplay_ProducesSameResult()
    {
        var atr = new AverageTrueRange(3);
        var bars = new[] { (100m, 105m, 95m), (102m, 107m, 97m), (105m, 110m, 98m), (110m, 115m, 100m) };

        foreach (var (c, h, l) in bars) atr.Update(c, h, l);
        var firstResult = atr.Value;

        atr.Reset();
        foreach (var (c, h, l) in bars) atr.Update(c, h, l);

        Assert.Equal(firstResult, atr.Value);
    }

    [Fact]
    public void Constructor_PeriodLessThanOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AverageTrueRange(0));
    }

    [Fact]
    public void Update_WithoutHighLow_UsesCloseForAll()
    {
        var atr = new AverageTrueRange(2);
        // Without high/low, TR = close - close = 0 for first bar
        atr.Update(100m);
        // Second bar: TR = max(0, |close-prevClose|, |close-prevClose|) = |105-100| = 5
        atr.Update(105m);

        Assert.True(atr.IsReady);
        // ATR = (0 + 5) / 2 = 2.5
        Assert.Equal(2.5m, atr.Value);
    }
}
