using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.V7;

// Feature: trading-engine-stories, Property 16: Dashboard Filtering Correctness

/// <summary>
/// Property 16: Dashboard Filtering Correctness.
/// For any set of BacktestResult items with mixed strategy types and statuses,
/// applying a strategy type filter SHALL return only items matching the selected type.
/// Toggling "Show failed runs" off SHALL exclude all items with BacktestStatus.Failed.
/// **Validates: Requirements 15.2, 15.3**
/// </summary>
public class DashboardFilteringProperties
{
    private static readonly string[] StrategyTypes =
    {
        "moving-average-crossover",
        "donchian-breakout",
        "zscore-mean-reversion",
        "volatility-scaled-trend",
        "baseline-buy-and-hold"
    };

    private static readonly BacktestStatus[] AllStatuses =
    {
        BacktestStatus.Completed,
        BacktestStatus.Failed,
        BacktestStatus.Cancelled
    };

    /// <summary>
    /// Creates a minimal BacktestResult with the specified strategy type and status.
    /// </summary>
    private static BacktestResult CreateResult(string strategyType, BacktestStatus status)
    {
        var config = new ScenarioConfig(
            ScenarioId: Guid.NewGuid().ToString(),
            Description: "test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>(),
            StrategyType: strategyType,
            StrategyParameters: new Dictionary<string, object>(),
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: "zero",
            CommissionModelType: "zero",
            InitialCash: 100_000m,
            AnnualRiskFreeRate: 0m,
            RandomSeed: null,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null);

        return new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: config,
            Status: status,
            EquityCurve: Array.Empty<EquityCurvePoint>(),
            Trades: Array.Empty<ClosedTrade>(),
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
    }

    /// <summary>
    /// Replicates the Dashboard filtering logic:
    /// FilteredRecentRuns = _recentRuns
    ///     .Where(r => _showFailedRuns || r.Status != BacktestStatus.Failed)
    ///     .Where(r => _selectedTypeFilter is null || r.ScenarioConfig.StrategyType == _selectedTypeFilter)
    ///     .ToList();
    /// </summary>
    private static List<BacktestResult> ApplyDashboardFilter(
        IReadOnlyList<BacktestResult> runs,
        bool showFailedRuns,
        string? selectedTypeFilter)
    {
        return runs
            .Where(r => showFailedRuns || r.Status != BacktestStatus.Failed)
            .Where(r => selectedTypeFilter is null || r.ScenarioConfig.StrategyType == selectedTypeFilter)
            .ToList();
    }

    /// <summary>
    /// For any set of results with mixed strategy types, applying a strategy type filter
    /// returns only items whose StrategyType matches the filter.
    /// **Validates: Requirements 15.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool StrategyTypeFilter_ReturnsOnlyMatchingItems(NonNegativeInt countWrap, NonNegativeInt seedWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(seedWrap.Item);

        // Generate results with mixed strategy types
        var items = Enumerable.Range(0, count)
            .Select(_ => CreateResult(
                StrategyTypes[rng.Next(StrategyTypes.Length)],
                AllStatuses[rng.Next(AllStatuses.Length)]))
            .ToList();

        // Pick a random strategy type to filter by
        string filterType = StrategyTypes[rng.Next(StrategyTypes.Length)];

        var filtered = ApplyDashboardFilter(items, showFailedRuns: true, selectedTypeFilter: filterType);

        // All returned items must match the filter type
        return filtered.All(r => r.ScenarioConfig.StrategyType == filterType);
    }

    /// <summary>
    /// For any set of results with mixed strategy types, applying a strategy type filter
    /// returns all items that match the filter (no matching items are lost).
    /// **Validates: Requirements 15.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool StrategyTypeFilter_ReturnsAllMatchingItems(NonNegativeInt countWrap, NonNegativeInt seedWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(seedWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ => CreateResult(
                StrategyTypes[rng.Next(StrategyTypes.Length)],
                AllStatuses[rng.Next(AllStatuses.Length)]))
            .ToList();

        string filterType = StrategyTypes[rng.Next(StrategyTypes.Length)];

        var filtered = ApplyDashboardFilter(items, showFailedRuns: true, selectedTypeFilter: filterType);

        // Count of filtered items must equal count of matching items in original list
        int expectedCount = items.Count(r => r.ScenarioConfig.StrategyType == filterType);
        return filtered.Count == expectedCount;
    }

    /// <summary>
    /// For any set of results with mixed statuses, toggling "Show failed" off
    /// excludes all items with BacktestStatus.Failed.
    /// **Validates: Requirements 15.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ShowFailedOff_ExcludesAllFailedItems(NonNegativeInt countWrap, NonNegativeInt seedWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(seedWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ => CreateResult(
                StrategyTypes[rng.Next(StrategyTypes.Length)],
                AllStatuses[rng.Next(AllStatuses.Length)]))
            .ToList();

        var filtered = ApplyDashboardFilter(items, showFailedRuns: false, selectedTypeFilter: null);

        // No Failed items should be present
        return filtered.All(r => r.Status != BacktestStatus.Failed);
    }

    /// <summary>
    /// For any set of results with mixed statuses, toggling "Show failed" off
    /// retains all non-failed items (Completed and Cancelled are preserved).
    /// **Validates: Requirements 15.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ShowFailedOff_RetainsAllNonFailedItems(NonNegativeInt countWrap, NonNegativeInt seedWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(seedWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ => CreateResult(
                StrategyTypes[rng.Next(StrategyTypes.Length)],
                AllStatuses[rng.Next(AllStatuses.Length)]))
            .ToList();

        var filtered = ApplyDashboardFilter(items, showFailedRuns: false, selectedTypeFilter: null);

        int expectedCount = items.Count(r => r.Status != BacktestStatus.Failed);
        return filtered.Count == expectedCount;
    }

    /// <summary>
    /// For any set of results, combining both filters (type filter + show failed off)
    /// returns only items matching the type AND not failed.
    /// **Validates: Requirements 15.2, 15.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool CombinedFilters_ReturnsOnlyMatchingNonFailedItems(NonNegativeInt countWrap, NonNegativeInt seedWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(seedWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ => CreateResult(
                StrategyTypes[rng.Next(StrategyTypes.Length)],
                AllStatuses[rng.Next(AllStatuses.Length)]))
            .ToList();

        string filterType = StrategyTypes[rng.Next(StrategyTypes.Length)];

        var filtered = ApplyDashboardFilter(items, showFailedRuns: false, selectedTypeFilter: filterType);

        // All items must match type AND not be failed
        bool allMatch = filtered.All(r =>
            r.ScenarioConfig.StrategyType == filterType &&
            r.Status != BacktestStatus.Failed);

        // Count must equal items matching both conditions
        int expectedCount = items.Count(r =>
            r.ScenarioConfig.StrategyType == filterType &&
            r.Status != BacktestStatus.Failed);

        return allMatch && filtered.Count == expectedCount;
    }
}
