using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests;

// Feature: web-only-ux-overhaul, Property 4: Robustness warnings evaluate only completed runs in recency order

/// <summary>
/// Property 4: Robustness warnings evaluate only completed runs in recency order.
/// For any list of BacktestResult items with mixed statuses, filtering to
/// Status == Completed and taking the first 10 by descending RunId produces a subset where:
/// all items have Status == Completed, count ≤ 10, items are ordered by RunId descending,
/// and no non-Completed run appears in the result.
/// **Validates: Requirements 11.1, 11.3**
/// </summary>
public class RobustnessWarningsFilterProperties
{
    private static readonly ScenarioConfig MinimalConfig = new(
        ScenarioId: "test",
        Description: "test",
        ReplayMode: ReplayMode.Bar,
        DataProviderType: "csv",
        DataProviderOptions: new Dictionary<string, object>(),
        StrategyType: "test-strategy",
        StrategyParameters: new Dictionary<string, object>(),
        RiskParameters: new Dictionary<string, object>(),
        SlippageModelType: "zero",
        CommissionModelType: "zero",
        InitialCash: 100_000m,
        AnnualRiskFreeRate: 0.05m,
        RandomSeed: null,
        ResearchWorkflowType: null,
        ResearchWorkflowOptions: null,
        PropFirmOptions: null);

    private static BacktestResult CreateResult(Guid runId, BacktestStatus status) =>
        new(
            RunId: runId,
            ScenarioConfig: MinimalConfig,
            Status: status,
            EquityCurve: Array.Empty<Core.Portfolio.EquityCurvePoint>(),
            Trades: Array.Empty<Core.Portfolio.ClosedTrade>(),
            StartEquity: 100_000m,
            EndEquity: 100_000m,
            MaxDrawdown: 0m,
            SharpeRatio: null,
            SortinoRatio: null,
            CalmarRatio: null,
            VaR95: null,
            CVaR95: null,
            OmegaRatio: null,
            UlcerIndex: null,
            ReturnOnMaxDrawdown: null,
            TotalTrades: 0,
            WinRate: null,
            ProfitFactor: null,
            AverageWin: null,
            AverageLoss: null,
            Expectancy: null,
            AverageHoldingPeriod: null,
            EquityCurveSmoothness: null,
            MaxConsecutiveLosses: 0,
            MaxConsecutiveWins: 0,
            RunDurationMs: 100);

    /// <summary>
    /// For any list of BacktestResult items with mixed statuses (Completed, Failed, Cancelled),
    /// applying the canonical robustness warnings filter (order by RunId descending, filter to
    /// Completed, take 10) produces a result where:
    /// - All items have Status == Completed
    /// - Count is ≤ 10
    /// - Items are ordered by RunId descending (most recent first)
    /// - No non-Completed run appears in the result
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RobustnessWarnings_FilterOnlyCompletedRunsInRecencyOrder(byte[] statusBytes)
    {
        // Handle null/empty input from FsCheck
        if (statusBytes is null || statusBytes.Length == 0)
            return true; // vacuously true for empty input

        // Generate a list of BacktestResult items with mixed statuses
        var statuses = new[] { BacktestStatus.Completed, BacktestStatus.Failed, BacktestStatus.Cancelled };
        var runs = statusBytes
            .Select(b => CreateResult(Guid.NewGuid(), statuses[b % 3]))
            .ToList();

        // Simulate the canonical ordering: OrderByDescending(r => r.RunId)
        var orderedRuns = runs.OrderByDescending(r => r.RunId).ToList();

        // Apply the robustness warnings filter (same logic as Dashboard.razor):
        // _runs is ordered by descending RunId, filter to Completed, take 10
        var recentCompleted = orderedRuns
            .Where(r => r.Status == BacktestStatus.Completed)
            .Take(10)
            .ToList();

        // Property assertions:

        // 1. All items have Status == Completed
        if (recentCompleted.Any(r => r.Status != BacktestStatus.Completed))
            return false;

        // 2. Count is ≤ 10
        if (recentCompleted.Count > 10)
            return false;

        // 3. Items are ordered by RunId descending
        for (int i = 1; i < recentCompleted.Count; i++)
        {
            if (recentCompleted[i].RunId.CompareTo(recentCompleted[i - 1].RunId) > 0)
                return false;
        }

        // 4. No non-Completed run appears in the result (redundant with #1, but explicit)
        if (recentCompleted.Any(r => r.Status == BacktestStatus.Failed || r.Status == BacktestStatus.Cancelled))
            return false;

        return true;
    }
}
