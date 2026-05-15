using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.Research;

/// <summary>
/// Unit tests for <see cref="SweepResultPager"/> verifying paging, filtering, and sorting.
/// </summary>
public sealed class SweepResultPagerTests
{
    private static BacktestResult MakeResult(
        string scenarioId,
        decimal? sharpe,
        decimal maxDrawdown,
        int totalTrades,
        decimal? winRate = null,
        decimal? profitFactor = null,
        decimal? calmar = null,
        Dictionary<string, object>? strategyParams = null)
    {
        var config = new ScenarioConfig(
            scenarioId, "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "sma",
            strategyParams ?? new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);

        return new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: config,
            Status: BacktestStatus.Completed,
            EquityCurve: Array.Empty<EquityCurvePoint>(),
            Trades: Array.Empty<ClosedTrade>(),
            StartEquity: 100_000m,
            EndEquity: 110_000m,
            MaxDrawdown: maxDrawdown,
            SharpeRatio: sharpe,
            SortinoRatio: null,
            CalmarRatio: calmar,
            ReturnOnMaxDrawdown: null,
            TotalTrades: totalTrades,
            WinRate: winRate,
            ProfitFactor: profitFactor,
            AverageWin: 500m,
            AverageLoss: -300m,
            Expectancy: 100m,
            AverageHoldingPeriod: TimeSpan.FromHours(4),
            EquityCurveSmoothness: 0.9m,
            MaxConsecutiveLosses: 3,
            MaxConsecutiveWins: 5,
            RunDurationMs: 1000,
            StrategyVersionId: null);
    }

    private static List<BacktestResult> MakeSampleResults()
    {
        return new List<BacktestResult>
        {
            MakeResult("sweep-1", 2.5m, 0.05m, 50, 0.65m, 2.0m, 3.0m,
                new Dictionary<string, object> { ["period"] = 10 }),
            MakeResult("sweep-2", 1.8m, 0.08m, 30, 0.55m, 1.5m, 2.0m,
                new Dictionary<string, object> { ["period"] = 20 }),
            MakeResult("sweep-3", 3.1m, 0.03m, 80, 0.70m, 2.5m, 4.0m,
                new Dictionary<string, object> { ["period"] = 14 }),
            MakeResult("sweep-4", 0.5m, 0.15m, 20, 0.45m, 0.8m, 0.5m,
                new Dictionary<string, object> { ["period"] = 30 }),
            MakeResult("sweep-5", 1.2m, 0.10m, 40, 0.50m, 1.2m, 1.5m,
                new Dictionary<string, object> { ["period"] = 25 }),
        };
    }

    [Fact]
    public void GetPage_ReturnsCorrectPageSize()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var result = pager.GetPage(new SweepPageRequest { Page = 1, PageSize = 2 });

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public void GetPage_DefaultSortBySharpeDescending()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var result = pager.GetPage(new SweepPageRequest { Page = 1, PageSize = 5 });

        // Highest Sharpe first (3.1, 2.5, 1.8, 1.2, 0.5)
        Assert.Equal(3.1m, result.Items[0].SharpeRatio);
        Assert.Equal(2.5m, result.Items[1].SharpeRatio);
        Assert.Equal(0.5m, result.Items[4].SharpeRatio);
    }

    [Fact]
    public void GetPage_SortByMaxDrawdownAscending()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var result = pager.GetPage(new SweepPageRequest
        {
            Page = 1,
            PageSize = 5,
            SortBy = SweepSortMetric.MaxDrawdown,
            SortDescending = false
        });

        // Lowest drawdown first (0.03, 0.05, 0.08, 0.10, 0.15)
        Assert.Equal(0.03m, result.Items[0].MaxDrawdown);
        Assert.Equal(0.15m, result.Items[4].MaxDrawdown);
    }

    [Fact]
    public void GetPage_FilterByScenarioId()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var result = pager.GetPage(new SweepPageRequest
        {
            Page = 1,
            PageSize = 10,
            Filter = "sweep-3"
        });

        Assert.Single(result.Items);
        Assert.Equal("sweep-3", result.Items[0].ScenarioConfig.ScenarioId);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public void GetPage_FilterByParameterValue()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var result = pager.GetPage(new SweepPageRequest
        {
            Page = 1,
            PageSize = 10,
            Filter = "period"
        });

        // All results have "period" parameter
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public void GetRange_ReturnsCorrectSlice()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var (items, totalCount) = pager.GetRange(
            filter: null,
            sortBy: SweepSortMetric.SharpeRatio,
            sortDescending: true,
            startIndex: 1,
            count: 2);

        Assert.Equal(2, items.Count);
        Assert.Equal(5, totalCount);
        // Second and third highest Sharpe
        Assert.Equal(2.5m, items[0].SharpeRatio);
        Assert.Equal(1.8m, items[1].SharpeRatio);
    }

    [Fact]
    public void GetRange_WithFilter_ReturnsFilteredCount()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var (items, totalCount) = pager.GetRange(
            filter: "sweep-1",
            sortBy: SweepSortMetric.SharpeRatio,
            sortDescending: true,
            startIndex: 0,
            count: 10);

        Assert.Single(items);
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public void GetPage_EmptyFilter_ReturnsAllResults()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var result = pager.GetPage(new SweepPageRequest
        {
            Page = 1,
            PageSize = 10,
            Filter = ""
        });

        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public void GetPage_PageBeyondResults_ReturnsEmpty()
    {
        var pager = new SweepResultPager(MakeSampleResults());

        var result = pager.GetPage(new SweepPageRequest
        {
            Page = 100,
            PageSize = 10
        });

        Assert.Empty(result.Items);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public void TotalCount_ReflectsAllResults()
    {
        var results = MakeSampleResults();
        var pager = new SweepResultPager(results);

        Assert.Equal(5, pager.TotalCount);
    }
}
