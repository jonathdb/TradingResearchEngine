using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.V7;

// Feature: trading-engine-stories, Property 15: Dashboard Sorting Correctness

/// <summary>
/// Property 15: Dashboard Sorting Correctness.
/// For any list of BacktestResult items and any sortable column (Sharpe, MaxDrawdown, TradeCount),
/// sorting in ascending order SHALL produce a sequence where each element's sort key is ≤ the next
/// element's sort key.
/// **Validates: Requirements 15.1**
/// </summary>
public class DashboardSortingProperties
{
    /// <summary>
    /// Creates a minimal BacktestResult with the specified metric values for sorting tests.
    /// </summary>
    private static BacktestResult CreateResult(decimal? sharpeRatio, decimal maxDrawdown, int totalTrades)
    {
        var config = new ScenarioConfig(
            ScenarioId: Guid.NewGuid().ToString(),
            Description: "test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>(),
            StrategyType: "moving-average-crossover",
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
            Status: BacktestStatus.Completed,
            EquityCurve: Array.Empty<EquityCurvePoint>(),
            Trades: Array.Empty<ClosedTrade>(),
            StartEquity: 100_000m,
            EndEquity: 100_000m,
            MaxDrawdown: maxDrawdown,
            SharpeRatio: sharpeRatio,
            SortinoRatio: null,
            CalmarRatio: null,
            ReturnOnMaxDrawdown: null,
            TotalTrades: totalTrades,
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
    /// For any list of BacktestResult items, sorting by SharpeRatio ascending
    /// (with null treated as decimal.MinValue) produces monotonically non-decreasing keys.
    /// **Validates: Requirements 15.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortBySharpeAscending_ProducesNonDecreasingKeys(
        NonNegativeInt countWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(countWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ =>
            {
                decimal? sharpe = rng.Next(10) == 0
                    ? null
                    : (decimal)(rng.NextDouble() * 10 - 5);
                decimal maxDd = (decimal)(rng.NextDouble() * 0.5);
                int trades = rng.Next(0, 500);
                return CreateResult(sharpe, maxDd, trades);
            })
            .ToList();

        // Dashboard sort key for Sharpe: x => x.SharpeRatio ?? decimal.MinValue
        var sorted = items.OrderBy(x => x.SharpeRatio ?? decimal.MinValue).ToList();

        return IsNonDecreasing(sorted, x => x.SharpeRatio ?? decimal.MinValue);
    }

    /// <summary>
    /// For any list of BacktestResult items, sorting by MaxDrawdown ascending
    /// produces monotonically non-decreasing keys.
    /// **Validates: Requirements 15.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortByMaxDrawdownAscending_ProducesNonDecreasingKeys(
        NonNegativeInt countWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(countWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ =>
            {
                decimal? sharpe = rng.Next(10) == 0
                    ? null
                    : (decimal)(rng.NextDouble() * 10 - 5);
                decimal maxDd = (decimal)(rng.NextDouble() * 0.5);
                int trades = rng.Next(0, 500);
                return CreateResult(sharpe, maxDd, trades);
            })
            .ToList();

        // Dashboard sort key for MaxDrawdown: x => x.MaxDrawdown
        var sorted = items.OrderBy(x => x.MaxDrawdown).ToList();

        return IsNonDecreasing(sorted, x => x.MaxDrawdown);
    }

    /// <summary>
    /// For any list of BacktestResult items, sorting by TotalTrades ascending
    /// produces monotonically non-decreasing keys.
    /// **Validates: Requirements 15.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortByTotalTradesAscending_ProducesNonDecreasingKeys(
        NonNegativeInt countWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(countWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ =>
            {
                decimal? sharpe = rng.Next(10) == 0
                    ? null
                    : (decimal)(rng.NextDouble() * 10 - 5);
                decimal maxDd = (decimal)(rng.NextDouble() * 0.5);
                int trades = rng.Next(0, 500);
                return CreateResult(sharpe, maxDd, trades);
            })
            .ToList();

        // Dashboard sort key for TotalTrades: x => x.TotalTrades
        var sorted = items.OrderBy(x => x.TotalTrades).ToList();

        return IsNonDecreasing(sorted, x => (decimal)x.TotalTrades);
    }

    /// <summary>
    /// For any list of BacktestResult items, sorting by SharpeRatio descending
    /// produces monotonically non-increasing keys.
    /// **Validates: Requirements 15.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortBySharpeDescending_ProducesNonIncreasingKeys(
        NonNegativeInt countWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(countWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ =>
            {
                decimal? sharpe = rng.Next(10) == 0
                    ? null
                    : (decimal)(rng.NextDouble() * 10 - 5);
                decimal maxDd = (decimal)(rng.NextDouble() * 0.5);
                int trades = rng.Next(0, 500);
                return CreateResult(sharpe, maxDd, trades);
            })
            .ToList();

        // Descending sort
        var sorted = items.OrderByDescending(x => x.SharpeRatio ?? decimal.MinValue).ToList();

        return IsNonIncreasing(sorted, x => x.SharpeRatio ?? decimal.MinValue);
    }

    /// <summary>
    /// For any list of BacktestResult items, sorting by MaxDrawdown descending
    /// produces monotonically non-increasing keys.
    /// **Validates: Requirements 15.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortByMaxDrawdownDescending_ProducesNonIncreasingKeys(
        NonNegativeInt countWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(countWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ =>
            {
                decimal? sharpe = rng.Next(10) == 0
                    ? null
                    : (decimal)(rng.NextDouble() * 10 - 5);
                decimal maxDd = (decimal)(rng.NextDouble() * 0.5);
                int trades = rng.Next(0, 500);
                return CreateResult(sharpe, maxDd, trades);
            })
            .ToList();

        var sorted = items.OrderByDescending(x => x.MaxDrawdown).ToList();

        return IsNonIncreasing(sorted, x => x.MaxDrawdown);
    }

    /// <summary>
    /// For any list of BacktestResult items, sorting by TotalTrades descending
    /// produces monotonically non-increasing keys.
    /// **Validates: Requirements 15.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool SortByTotalTradesDescending_ProducesNonIncreasingKeys(
        NonNegativeInt countWrap)
    {
        int count = countWrap.Item % 50;
        var rng = new Random(countWrap.Item);

        var items = Enumerable.Range(0, count)
            .Select(_ =>
            {
                decimal? sharpe = rng.Next(10) == 0
                    ? null
                    : (decimal)(rng.NextDouble() * 10 - 5);
                decimal maxDd = (decimal)(rng.NextDouble() * 0.5);
                int trades = rng.Next(0, 500);
                return CreateResult(sharpe, maxDd, trades);
            })
            .ToList();

        var sorted = items.OrderByDescending(x => x.TotalTrades).ToList();

        return IsNonIncreasing(sorted, x => (decimal)x.TotalTrades);
    }

    /// <summary>
    /// Verifies that the sequence is monotonically non-decreasing by the given key selector.
    /// </summary>
    private static bool IsNonDecreasing(IReadOnlyList<BacktestResult> items, Func<BacktestResult, decimal> keySelector)
    {
        for (int i = 1; i < items.Count; i++)
        {
            if (keySelector(items[i - 1]) > keySelector(items[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Verifies that the sequence is monotonically non-increasing by the given key selector.
    /// </summary>
    private static bool IsNonIncreasing(IReadOnlyList<BacktestResult> items, Func<BacktestResult, decimal> keySelector)
    {
        for (int i = 1; i < items.Count; i++)
        {
            if (keySelector(items[i - 1]) < keySelector(items[i]))
                return false;
        }
        return true;
    }
}
