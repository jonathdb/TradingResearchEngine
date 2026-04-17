using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Helpers;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 19: PnLBinningCoverage
/// Non-empty trade PnL list → exactly 20 bins covering min to max PnL.
/// **Validates: Requirements 13.3, 18.2**
/// </summary>
public class PnlBinningCoverageProperties
{
    [Property(MaxTest = 100)]
    public bool PnlBinning_Exactly20Bins_CoveringMinToMax(NonEmptyArray<int> pnlInts)
    {
        // Convert ints to decimal PnL values to get varied inputs
        var pnlValues = pnlInts.Get.Select(i => (decimal)i).ToArray();

        // Create trades from PnL values
        var baseTime = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var trades = pnlValues.Select((pnl, idx) => new ClosedTrade(
            Symbol: "TEST",
            EntryTime: baseTime.AddDays(idx),
            ExitTime: baseTime.AddDays(idx + 1),
            EntryPrice: 100m,
            ExitPrice: 100m + pnl,
            Quantity: 1m,
            Direction: Direction.Long,
            GrossPnl: pnl,
            Commission: 0m,
            NetPnl: pnl
        )).ToList();

        var bins = ChartComputationHelpers.BinTradePnl(trades, 20);

        // Must produce exactly 20 bins
        if (bins.Count != 20) return false;

        var minPnl = pnlValues.Min();
        var maxPnl = pnlValues.Max();

        // First bin lower bound should be at or below min PnL
        if (bins[0].LowerBound > minPnl) return false;

        // Last bin upper bound should be at or above max PnL
        if (bins[^1].UpperBound < maxPnl) return false;

        // Total count across all bins should equal trade count
        if (bins.Sum(b => b.Count) != trades.Count) return false;

        return true;
    }
}
