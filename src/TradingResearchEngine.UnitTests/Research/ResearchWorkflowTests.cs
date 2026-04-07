using TradingResearchEngine.Application.Configuration;
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
        var useCase = new ScenarioComparisonUseCase();
        var single = new List<BacktestResult> { MakeBacktestResult() };

        Assert.Throws<ArgumentException>(() => useCase.Compare(single));
    }

    [Fact]
    public void ScenarioComparison_TwoResults_ReturnsBestBySharpeAndDrawdown()
    {
        var useCase = new ScenarioComparisonUseCase();
        var r1 = MakeBacktestResult("scenario-a", sharpe: 1.5m, maxDd: 0.10m);
        var r2 = MakeBacktestResult("scenario-b", sharpe: 2.0m, maxDd: 0.20m);

        var report = useCase.Compare(new List<BacktestResult> { r1, r2 });

        Assert.Equal("scenario-b", report.BestBySharpe);
        Assert.Equal("scenario-a", report.BestByDrawdown);
        Assert.Equal(2, report.Rows.Count);
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
        IReadOnlyList<ClosedTrade>? trades = null) =>
        new(Guid.NewGuid(),
            new ScenarioConfig(scenarioId, "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint> { new(T0, 100_000m) },
            trades ?? new List<ClosedTrade>(),
            100_000m, 105_000m, maxDd, sharpe, sharpe, null, null, 10, 0.6m, 1.5m, 200m, -100m, 10m, null, null, 3, 5, 50);

    private static ClosedTrade MakeTrade(decimal netPnl) =>
        new("TEST", T0, T0.AddHours(1), 100m, 100m + netPnl, 1m,
            Core.Events.Direction.Long, netPnl, 0m, netPnl);
}
