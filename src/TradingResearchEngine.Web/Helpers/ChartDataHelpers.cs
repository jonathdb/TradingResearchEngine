using TradingResearchEngine.Application.Helpers;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Web.Helpers;

/// <summary>
/// Thin wrapper over <see cref="ChartComputationHelpers"/> for Web-layer chart components.
/// Pure computation logic lives in Application layer for testability.
/// </summary>
public static class ChartDataHelpers
{
    /// <summary>Computes monthly returns as percentages from an equity curve.</summary>
    public static IReadOnlyList<MonthlyReturn> ComputeMonthlyReturns(IReadOnlyList<EquityCurvePoint> curve)
        => ChartComputationHelpers.ComputeMonthlyReturns(curve);

    /// <summary>Bins trade PnL values into the specified number of buckets.</summary>
    public static IReadOnlyList<HistogramBin> BinTradePnl(IReadOnlyList<ClosedTrade> trades, int bins = 20)
        => ChartComputationHelpers.BinTradePnl(trades, bins);

    /// <summary>Bins trade holding periods into a histogram.</summary>
    public static IReadOnlyList<HistogramBin> BinHoldingPeriods(IReadOnlyList<ClosedTrade> trades, int bins = 20)
        => ChartComputationHelpers.BinHoldingPeriods(trades, bins);
}
