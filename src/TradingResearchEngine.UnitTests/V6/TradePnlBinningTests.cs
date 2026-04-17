using TradingResearchEngine.Application.Helpers;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>Unit tests for ChartComputationHelpers.BinTradePnl.</summary>
public sealed class TradePnlBinningTests
{
    [Fact]
    public void BinTradePnl_20Bins_CoverFullPnlRange()
    {
        var trades = CreateTrades(-500m, -200m, 0m, 100m, 300m, 500m, 800m, 1000m);

        var bins = ChartComputationHelpers.BinTradePnl(trades, 20);

        Assert.Equal(20, bins.Count);
        // First bin should start at or below min PnL
        Assert.True(bins[0].LowerBound <= -500m);
        // Last bin should end at or above max PnL
        Assert.True(bins[^1].UpperBound >= 1000m);
        // Total count across all bins should equal trade count
        Assert.Equal(trades.Count, bins.Sum(b => b.Count));
    }

    [Fact]
    public void BinTradePnl_EmptyTrades_ReturnsEmptyHistogram()
    {
        var bins = ChartComputationHelpers.BinTradePnl(new List<ClosedTrade>(), 20);
        Assert.Empty(bins);
    }

    [Fact]
    public void BinTradePnl_NullTrades_ReturnsEmptyHistogram()
    {
        var bins = ChartComputationHelpers.BinTradePnl(null!, 20);
        Assert.Empty(bins);
    }

    [Fact]
    public void BinTradePnl_AllSameValue_PlacesAllInOneBin()
    {
        var trades = CreateTrades(100m, 100m, 100m);

        var bins = ChartComputationHelpers.BinTradePnl(trades, 20);

        Assert.Equal(20, bins.Count);
        Assert.Equal(3, bins.Sum(b => b.Count));
    }

    [Fact]
    public void BinHoldingPeriods_EmptyTrades_ReturnsEmpty()
    {
        var bins = ChartComputationHelpers.BinHoldingPeriods(new List<ClosedTrade>());
        Assert.Empty(bins);
    }

    private static List<ClosedTrade> CreateTrades(params decimal[] pnlValues)
    {
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return pnlValues.Select((pnl, i) => new ClosedTrade(
            Symbol: "TEST",
            EntryTime: baseTime.AddDays(i),
            ExitTime: baseTime.AddDays(i + 1),
            EntryPrice: 100m,
            ExitPrice: 100m + pnl,
            Quantity: 1m,
            Direction: Direction.Long,
            GrossPnl: pnl,
            Commission: 0m,
            NetPnl: pnl
        )).ToList();
    }
}
