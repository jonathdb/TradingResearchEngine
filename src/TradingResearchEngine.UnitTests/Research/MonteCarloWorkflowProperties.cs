using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.Research;

/// <summary>Property-based tests for MonteCarloWorkflow correctness properties.</summary>
public class MonteCarloWorkflowProperties
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Feature: v51-engine-fixes, Property 10: Monte Carlo determinism with same seed and BlockSize
    [Property(MaxTest = 100)]
    public bool SameSeedAndBlockSize_ProduceIdenticalResults(PositiveInt seedWrap, PositiveInt blockWrap)
    {
        int seed = seedWrap.Get;
        int blockSize = Math.Max(1, blockWrap.Get % 10); // Keep BlockSize reasonable [1..10]
        var source = MakeBacktestResult();
        var options = new MonteCarloOptions
        {
            SimulationCount = 50,
            Seed = seed,
            BlockSize = blockSize
        };

        var workflow = new MonteCarloWorkflow(null!);
        var r1 = workflow.RunAsync(source, options);
        var r2 = workflow.RunAsync(source, options);

        return r1.P10EndEquity == r2.P10EndEquity
            && r1.P50EndEquity == r2.P50EndEquity
            && r1.P90EndEquity == r2.P90EndEquity
            && r1.RuinProbability == r2.RuinProbability
            && r1.SampledPaths.Count == r2.SampledPaths.Count
            && r1.SampledPaths.Zip(r2.SampledPaths)
                .All(pair => pair.First.EquityValues.SequenceEqual(pair.Second.EquityValues));
    }

    private static BacktestResult MakeBacktestResult()
    {
        var trades = new List<ClosedTrade>
        {
            MakeTrade(100m), MakeTrade(-50m), MakeTrade(200m),
            MakeTrade(-30m), MakeTrade(150m), MakeTrade(-80m),
            MakeTrade(60m), MakeTrade(-20m), MakeTrade(90m), MakeTrade(-10m)
        };

        return new BacktestResult(
            Guid.NewGuid(),
            new ScenarioConfig("mc-prop-test", "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint> { new(T0, 100_000m) },
            trades,
            100_000m, 105_000m, 0.05m, 1.0m, 1.0m, null, null, 10, 0.6m, 1.5m, 200m, -100m, 10m, null, null, 3, 5, 50);
    }

    private static ClosedTrade MakeTrade(decimal netPnl) =>
        new("TEST", T0, T0.AddHours(1), 100m, 100m + netPnl, 1m,
            Core.Events.Direction.Long, netPnl, 0m, netPnl);
}
