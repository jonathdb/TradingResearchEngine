using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.UnitTests.Indicators;

public class DonchianChannelTests
{
    [Fact]
    public void Value_BeforeWarmup_ReturnsNull()
    {
        var dc = new DonchianChannel(3);
        dc.Update(100m, 105m, 95m);
        dc.Update(102m, 107m, 97m);

        Assert.Null(dc.Value);
        Assert.False(dc.IsReady);
    }

    [Fact]
    public void Value_AtWarmup_ReturnsCorrectChannel()
    {
        var dc = new DonchianChannel(3);
        dc.Update(100m, 105m, 95m);
        dc.Update(102m, 107m, 97m);
        dc.Update(101m, 103m, 99m);

        Assert.True(dc.IsReady);
        var result = dc.Value!.Value;

        // Upper = max(105, 107, 103) = 107
        // Lower = min(95, 97, 99) = 95
        // Middle = (107 + 95) / 2 = 101
        Assert.Equal(107m, result.Upper);
        Assert.Equal(95m, result.Lower);
        Assert.Equal(101m, result.Middle);
    }

    [Fact]
    public void Value_RollingWindow_DropsOldestBar()
    {
        var dc = new DonchianChannel(3);
        dc.Update(100m, 105m, 95m);
        dc.Update(102m, 107m, 97m);
        dc.Update(101m, 103m, 99m);
        dc.Update(104m, 110m, 100m);

        var result = dc.Value!.Value;

        // Window: bars 2,3,4 → highs [107, 103, 110], lows [97, 99, 100]
        // Upper = max(107, 103, 110) = 110
        // Lower = min(97, 99, 100) = 97
        // Middle = (110 + 97) / 2 = 103.5
        Assert.Equal(110m, result.Upper);
        Assert.Equal(97m, result.Lower);
        Assert.Equal(103.5m, result.Middle);
    }

    [Fact]
    public void Update_WithoutHighLow_UsesCloseForBoth()
    {
        var dc = new DonchianChannel(3);
        dc.Update(100m);
        dc.Update(110m);
        dc.Update(90m);

        var result = dc.Value!.Value;

        Assert.Equal(110m, result.Upper);
        Assert.Equal(90m, result.Lower);
        Assert.Equal(100m, result.Middle);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var dc = new DonchianChannel(3);
        dc.Update(100m, 105m, 95m);
        dc.Update(102m, 107m, 97m);
        dc.Update(101m, 103m, 99m);
        Assert.True(dc.IsReady);

        dc.Reset();

        Assert.False(dc.IsReady);
        Assert.Null(dc.Value);
    }

    [Fact]
    public void Reset_ThenReplay_ProducesSameResult()
    {
        var dc = new DonchianChannel(3);
        var bars = new[] { (100m, 105m, 95m), (102m, 107m, 97m), (101m, 103m, 99m), (104m, 110m, 100m) };

        foreach (var (c, h, l) in bars) dc.Update(c, h, l);
        var firstResult = dc.Value;

        dc.Reset();
        foreach (var (c, h, l) in bars) dc.Update(c, h, l);

        Assert.Equal(firstResult, dc.Value);
    }

    [Fact]
    public void Constructor_PeriodLessThanOne_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DonchianChannel(0));
    }
}
