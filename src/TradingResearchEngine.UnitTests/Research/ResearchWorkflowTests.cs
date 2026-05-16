using Moq;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.Research;

public class ResearchWorkflowTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MonteCarloWorkflow_SimulationCountLessThanOne_ThrowsArgumentException()
    {
        var workflow = new MonteCarloWorkflow(null!);
        var options = new MonteCarloOptions { SimulationCount = 0 };
        var result = MakeBacktestResult();

        Assert.Throws<ArgumentException>(() => workflow.RunAsync(result, options));
    }

    [Fact]
    public void ScenarioComparison_FewerThanTwoResults_ThrowsArgumentException()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var single = new List<BacktestResult> { MakeBacktestResult() };

        Assert.Throws<ArgumentException>(() => useCase.Compare(single));
    }

    [Fact]
    public void ScenarioComparison_TwoResults_ReturnsBestBySharpeAndDrawdown()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("scenario-a", sharpe: 1.5m, maxDd: 0.10m);
        var r2 = MakeBacktestResult("scenario-b", sharpe: 2.0m, maxDd: 0.20m);

        var report = useCase.Compare(new List<BacktestResult> { r1, r2 });

        Assert.Equal("scenario-b", report.BestBySharpe);
        Assert.Equal("scenario-a", report.BestByDrawdown);
        Assert.Equal(2, report.Rows.Count);
    }

    [Fact]
    public void ScenarioComparison_NoFilterNoSortKey_RankedScenarioIdsIsNull()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("scenario-a", sharpe: 1.5m, maxDd: 0.10m);
        var r2 = MakeBacktestResult("scenario-b", sharpe: 2.0m, maxDd: 0.20m);

        var report = useCase.Compare(new List<BacktestResult> { r1, r2 });

        Assert.Null(report.RankedScenarioIds);
    }

    [Fact]
    public void ScenarioComparison_WithFilter_ExcludesResultsBelowMinWinRate()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("low-wr", sharpe: 2.0m, maxDd: 0.05m, winRate: 0.3m);
        var r2 = MakeBacktestResult("high-wr", sharpe: 1.5m, maxDd: 0.10m, winRate: 0.7m);
        var r3 = MakeBacktestResult("mid-wr", sharpe: 1.8m, maxDd: 0.08m, winRate: 0.55m);

        var filter = new ComparisonFilter(MinWinRate: 0.5m);
        var report = useCase.Compare(new List<BacktestResult> { r1, r2, r3 }, filter, ComparisonSortKey.Sharpe);

        Assert.NotNull(report.RankedScenarioIds);
        Assert.Equal(2, report.RankedScenarioIds.Count);
        Assert.DoesNotContain("low-wr", report.RankedScenarioIds);
        Assert.Contains("high-wr", report.RankedScenarioIds);
        Assert.Contains("mid-wr", report.RankedScenarioIds);
    }

    [Fact]
    public void ScenarioComparison_WithFilter_ExcludesResultsBelowMinTrades()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("few-trades", sharpe: 2.0m, maxDd: 0.05m, totalTrades: 5);
        var r2 = MakeBacktestResult("many-trades", sharpe: 1.5m, maxDd: 0.10m, totalTrades: 50);

        var filter = new ComparisonFilter(MinTrades: 30);
        var report = useCase.Compare(new List<BacktestResult> { r1, r2 }, filter, ComparisonSortKey.Sharpe);

        Assert.NotNull(report.RankedScenarioIds);
        Assert.Single(report.RankedScenarioIds);
        Assert.Equal("many-trades", report.RankedScenarioIds[0]);
    }

    [Fact]
    public void ScenarioComparison_WithFilter_ExcludesResultsAboveMaxDrawdown()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("high-dd", sharpe: 2.0m, maxDd: 0.30m);
        var r2 = MakeBacktestResult("low-dd", sharpe: 1.5m, maxDd: 0.10m);

        var filter = new ComparisonFilter(MaxDrawdown: 0.20m);
        var report = useCase.Compare(new List<BacktestResult> { r1, r2 }, filter, ComparisonSortKey.Sharpe);

        Assert.NotNull(report.RankedScenarioIds);
        Assert.Single(report.RankedScenarioIds);
        Assert.Equal("low-dd", report.RankedScenarioIds[0]);
    }

    [Fact]
    public void ScenarioComparison_WithSortKeyCalmar_RanksByCalmarDescending()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("low-calmar", sharpe: 2.0m, maxDd: 0.10m, calmar: 1.0m);
        var r2 = MakeBacktestResult("high-calmar", sharpe: 1.5m, maxDd: 0.15m, calmar: 3.0m);
        var r3 = MakeBacktestResult("mid-calmar", sharpe: 1.8m, maxDd: 0.12m, calmar: 2.0m);

        var report = useCase.Compare(
            new List<BacktestResult> { r1, r2, r3 },
            filter: null,
            sortKey: ComparisonSortKey.Calmar);

        Assert.NotNull(report.RankedScenarioIds);
        Assert.Equal("high-calmar", report.RankedScenarioIds[0]);
        Assert.Equal("mid-calmar", report.RankedScenarioIds[1]);
        Assert.Equal("low-calmar", report.RankedScenarioIds[2]);
    }

    [Fact]
    public void ScenarioComparison_WithSortKeyMaxDrawdown_RanksByDrawdownAscending()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("high-dd", sharpe: 2.0m, maxDd: 0.30m);
        var r2 = MakeBacktestResult("low-dd", sharpe: 1.5m, maxDd: 0.05m);
        var r3 = MakeBacktestResult("mid-dd", sharpe: 1.8m, maxDd: 0.15m);

        var report = useCase.Compare(
            new List<BacktestResult> { r1, r2, r3 },
            filter: null,
            sortKey: ComparisonSortKey.MaxDrawdown);

        Assert.NotNull(report.RankedScenarioIds);
        Assert.Equal("low-dd", report.RankedScenarioIds[0]);
        Assert.Equal("mid-dd", report.RankedScenarioIds[1]);
        Assert.Equal("high-dd", report.RankedScenarioIds[2]);
    }

    [Fact]
    public void ScenarioComparison_FilterAndSort_PreservesDefaultBestOfLogic()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("scenario-a", sharpe: 1.5m, maxDd: 0.10m, winRate: 0.6m);
        var r2 = MakeBacktestResult("scenario-b", sharpe: 2.0m, maxDd: 0.20m, winRate: 0.7m);

        var filter = new ComparisonFilter(MinWinRate: 0.5m);
        var report = useCase.Compare(new List<BacktestResult> { r1, r2 }, filter, ComparisonSortKey.Sharpe);

        // Default best-of logic is still computed from ALL results
        Assert.Equal("scenario-b", report.BestBySharpe);
        Assert.Equal("scenario-a", report.BestByDrawdown);
        // All rows are present (unfiltered)
        Assert.Equal(2, report.Rows.Count);
    }

    [Fact]
    public void ScenarioComparison_FilterWithNullWinRate_ExcludesResultsWithNullWinRate()
    {
        var useCase = new ScenarioComparisonUseCase(Mock.Of<IReportExporter>(), Mock.Of<IBacktestResultRepository>());
        var r1 = MakeBacktestResult("no-wr", sharpe: 2.0m, maxDd: 0.05m, winRate: null);
        var r2 = MakeBacktestResult("has-wr", sharpe: 1.5m, maxDd: 0.10m, winRate: 0.6m);

        var filter = new ComparisonFilter(MinWinRate: 0.5m);
        var report = useCase.Compare(new List<BacktestResult> { r1, r2 }, filter, ComparisonSortKey.Sharpe);

        Assert.NotNull(report.RankedScenarioIds);
        Assert.Single(report.RankedScenarioIds);
        Assert.Equal("has-wr", report.RankedScenarioIds[0]);
    }

    [Fact]
    public void MonteCarloWorkflow_WithSeed_ProducesReproducibleResults()
    {
        var workflow = new MonteCarloWorkflow(null!);
        var source = MakeBacktestResult(trades: new List<ClosedTrade>
        {
            MakeTrade(100m), MakeTrade(-50m), MakeTrade(200m), MakeTrade(-30m), MakeTrade(150m)
        });
        var options = new MonteCarloOptions { SimulationCount = 100, Seed = 42 };

        var r1 = workflow.RunAsync(source, options);
        var r2 = workflow.RunAsync(source, options);

        Assert.Equal(r1.P10EndEquity, r2.P10EndEquity);
        Assert.Equal(r1.P50EndEquity, r2.P50EndEquity);
        Assert.Equal(r1.P90EndEquity, r2.P90EndEquity);
        Assert.Equal(r1.RuinProbability, r2.RuinProbability);
    }

    private static BacktestResult MakeBacktestResult(
        string scenarioId = "test",
        decimal sharpe = 1.0m,
        decimal maxDd = 0.05m,
        IReadOnlyList<ClosedTrade>? trades = null,
        decimal? winRate = 0.6m,
        int totalTrades = 10,
        decimal? calmar = null) =>
        new(Guid.NewGuid(),
            new ScenarioConfig(scenarioId, "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint> { new(T0, 100_000m) },
            trades ?? new List<ClosedTrade>(),
            100_000m, 105_000m, maxDd, sharpe, sharpe, calmar, null, null, null, null, null, totalTrades, winRate, 1.5m, 200m, -100m, 10m, null, null, 3, 5, 50);

    private static ClosedTrade MakeTrade(decimal netPnl) =>
        new("TEST", T0, T0.AddHours(1), 100m, 100m + netPnl, 1m,
            Core.Events.Direction.Long, netPnl, 0m, netPnl);
}
