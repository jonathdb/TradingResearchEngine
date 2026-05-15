using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.Portfolio;

public class TradeExcursionTrackerTests
{
    private static readonly DateTimeOffset EntryTime = new(2024, 1, 1, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExitTime = new(2024, 1, 5, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LongTrade_PriceGoesUp_MFEIsPositive()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Long);
        tracker.UpdatePrice(110m); // +10%
        tracker.UpdatePrice(105m);

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.NotNull(anatomy.MaxFavorableExcursion);
        Assert.Equal(0.10m, anatomy.MaxFavorableExcursion!.Value);
    }

    [Fact]
    public void LongTrade_PriceGoesDown_MAEIsNegative()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Long);
        tracker.UpdatePrice(90m); // -10%
        tracker.UpdatePrice(95m);

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.NotNull(anatomy.MaxAdverseExcursion);
        Assert.Equal(-0.10m, anatomy.MaxAdverseExcursion!.Value);
    }

    [Fact]
    public void LongTrade_PriceFluctuates_TracksExtremes()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Long);
        tracker.UpdatePrice(95m);  // -5%
        tracker.UpdatePrice(115m); // +15%
        tracker.UpdatePrice(85m);  // -15% (new low)
        tracker.UpdatePrice(110m); // +10%

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.Equal(-0.15m, anatomy.MaxAdverseExcursion!.Value);
        Assert.Equal(0.15m, anatomy.MaxFavorableExcursion!.Value);
    }

    [Fact]
    public void ShortTrade_PriceGoesDown_MFEIsPositive()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Short);
        tracker.UpdatePrice(90m); // Price down = favorable for short

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.NotNull(anatomy.MaxFavorableExcursion);
        Assert.Equal(0.10m, anatomy.MaxFavorableExcursion!.Value);
    }

    [Fact]
    public void ShortTrade_PriceGoesUp_MAEIsNegative()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Short);
        tracker.UpdatePrice(110m); // Price up = adverse for short

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.NotNull(anatomy.MaxAdverseExcursion);
        Assert.Equal(-0.10m, anatomy.MaxAdverseExcursion!.Value);
    }

    [Fact]
    public void ShortTrade_PriceFluctuates_TracksExtremes()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Short);
        tracker.UpdatePrice(105m); // adverse +5%
        tracker.UpdatePrice(85m);  // favorable -15%
        tracker.UpdatePrice(120m); // adverse +20% (new high)
        tracker.UpdatePrice(90m);  // favorable -10%

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        // MAE: worst adverse = price went to 120, so (100 - 120)/100 = -0.20
        Assert.Equal(-0.20m, anatomy.MaxAdverseExcursion!.Value);
        // MFE: best favorable = price went to 85, so (100 - 85)/100 = 0.15
        Assert.Equal(0.15m, anatomy.MaxFavorableExcursion!.Value);
    }

    [Fact]
    public void NoUpdates_ReturnsNullExcursions()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Long);

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.Null(anatomy.MaxAdverseExcursion);
        Assert.Null(anatomy.MaxFavorableExcursion);
    }

    [Fact]
    public void ZeroEntryPrice_ReturnsNullExcursions()
    {
        var tracker = new TradeExcursionTracker(0m, 10m, Direction.Long);
        tracker.UpdatePrice(50m);

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.Null(anatomy.MaxAdverseExcursion);
        Assert.Null(anatomy.MaxFavorableExcursion);
    }

    [Fact]
    public void Duration_ComputedCorrectly()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Long);
        tracker.UpdatePrice(105m);

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.Equal(ExitTime - EntryTime, anatomy.Duration);
    }

    [Fact]
    public void LongTrade_PriceStaysAtEntry_ExcursionsAreZero()
    {
        var tracker = new TradeExcursionTracker(100m, 10m, Direction.Long);
        tracker.UpdatePrice(100m);

        var anatomy = tracker.BuildAnatomy(EntryTime, ExitTime);

        Assert.Equal(0m, anatomy.MaxAdverseExcursion!.Value);
        Assert.Equal(0m, anatomy.MaxFavorableExcursion!.Value);
    }
}
