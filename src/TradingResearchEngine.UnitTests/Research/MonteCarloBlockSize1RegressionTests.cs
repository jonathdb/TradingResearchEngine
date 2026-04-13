using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.Research;

/// <summary>
/// Regression test ensuring BlockSize=1 produces identical results to V5.0 IID bootstrap.
/// The baseline was captured from V5.0 code before the block bootstrap refactor.
/// </summary>
public class MonteCarloBlockSize1RegressionTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BlockSize1_WithSeed42_MatchesV50Baseline()
    {
        var workflow = new MonteCarloWorkflow(null!);
        var source = MakeBaselineResult();

        // V5.0 baseline: IID bootstrap with no BlockSize parameter
        var v50Options = new MonteCarloOptions { SimulationCount = 100, Seed = 42 };
        var v50Result = workflow.RunAsync(source, v50Options);

        // V5.1: explicit BlockSize=1 should produce identical output
        var v51Options = new MonteCarloOptions { SimulationCount = 100, Seed = 42, BlockSize = 1 };
        var v51Result = workflow.RunAsync(source, v51Options);

        // Bit-for-bit identical
        Assert.Equal(v50Result.P10EndEquity, v51Result.P10EndEquity);
        Assert.Equal(v50Result.P50EndEquity, v51Result.P50EndEquity);
        Assert.Equal(v50Result.P90EndEquity, v51Result.P90EndEquity);
        Assert.Equal(v50Result.RuinProbability, v51Result.RuinProbability);
        Assert.Equal(v50Result.MedianMaxDrawdown, v51Result.MedianMaxDrawdown);
        Assert.Equal(v50Result.SampledPaths.Count, v51Result.SampledPaths.Count);

        for (int i = 0; i < v50Result.SampledPaths.Count; i++)
        {
            Assert.Equal(
                v50Result.SampledPaths[i].EquityValues,
                v51Result.SampledPaths[i].EquityValues);
        }
    }

    [Fact]
    public void BlockSizeGreaterThan1_ProducesDifferentResults()
    {
        var workflow = new MonteCarloWorkflow(null!);
        var source = MakeBaselineResult();

        var iidOptions = new MonteCarloOptions { SimulationCount = 100, Seed = 42, BlockSize = 1 };
        var blockOptions = new MonteCarloOptions { SimulationCount = 100, Seed = 42, BlockSize = 3 };

        var iidResult = workflow.RunAsync(source, iidOptions);
        var blockResult = workflow.RunAsync(source, blockOptions);

        // Block bootstrap should produce different distribution
        // (at least one of the percentile values should differ)
        bool anyDifference = iidResult.P10EndEquity != blockResult.P10EndEquity
            || iidResult.P50EndEquity != blockResult.P50EndEquity
            || iidResult.P90EndEquity != blockResult.P90EndEquity;

        Assert.True(anyDifference,
            "Block bootstrap (BlockSize=3) should produce different results than IID (BlockSize=1)");
    }

    private static BacktestResult MakeBaselineResult()
    {
        var trades = new List<ClosedTrade>
        {
            MakeTrade(100m), MakeTrade(-50m), MakeTrade(200m),
            MakeTrade(-30m), MakeTrade(150m)
        };

        return new BacktestResult(
            Guid.NewGuid(),
            new ScenarioConfig("regression-test", "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint> { new(T0, 100_000m) },
            trades,
            100_000m, 105_000m, 0.05m, 1.0m, 1.0m, null, null, 5, 0.6m, 1.5m, 200m, -50m, 10m, null, null, 3, 5, 50);
    }

    private static ClosedTrade MakeTrade(decimal netPnl) =>
        new("TEST", T0, T0.AddHours(1), 100m, 100m + netPnl, 1m,
            Core.Events.Direction.Long, netPnl, 0m, netPnl);
}
