using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Executes one engine run per unique parameter combination in the Cartesian product of a parameter grid.
/// Returns results ranked by Sharpe ratio descending.
/// </summary>
public sealed class ParameterSweepWorkflow : IResearchWorkflow<SweepOptions, SweepResult>
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly IOptions<SweepOptions> _options;

    /// <inheritdoc cref="ParameterSweepWorkflow"/>
    public ParameterSweepWorkflow(RunScenarioUseCase runScenario, IOptions<SweepOptions> options)
    {
        _runScenario = runScenario;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<SweepResult> RunAsync(ScenarioConfig baseConfig, SweepOptions options, CancellationToken ct = default)
    {
        var grid = baseConfig.ResearchWorkflowOptions is not null
            && baseConfig.ResearchWorkflowOptions.TryGetValue("ParameterGrid", out var gridObj)
            && gridObj is Dictionary<string, object> rawGrid
                ? ParseGrid(rawGrid)
                : new Dictionary<string, List<object>>();

        var combinations = CartesianProduct(grid);
        if (combinations.Count == 0)
            combinations.Add(new Dictionary<string, object>());

        var results = new List<BacktestResult>();
        var maxParallelism = options.MaxDegreeOfParallelism > 0
            ? options.MaxDegreeOfParallelism
            : _options.Value.MaxDegreeOfParallelism;

        await Parallel.ForEachAsync(
            combinations,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelism, CancellationToken = ct },
            async (combo, token) =>
            {
                var merged = MergeParameters(baseConfig, combo);
                var runResult = await _runScenario.RunAsync(merged, token);
                if (runResult.IsSuccess && runResult.Result is not null)
                {
                    lock (results) { results.Add(runResult.Result); }
                }
            });

        var ranked = results
            .OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue)
            .ToList();

        var sensitivity = ComputeSensitivity(results, grid);

        return new SweepResult(results, ranked, sensitivity);
    }

    private static Dictionary<string, List<object>> ParseGrid(Dictionary<string, object> raw)
    {
        var grid = new Dictionary<string, List<object>>();
        foreach (var (key, value) in raw)
        {
            if (value is IEnumerable<object> list)
                grid[key] = list.ToList();
            else
                grid[key] = new List<object> { value };
        }
        return grid;
    }

    private static List<Dictionary<string, object>> CartesianProduct(Dictionary<string, List<object>> grid)
    {
        var keys = grid.Keys.ToList();
        if (keys.Count == 0) return new List<Dictionary<string, object>>();

        var result = new List<Dictionary<string, object>> { new() };
        foreach (var key in keys)
        {
            var next = new List<Dictionary<string, object>>();
            foreach (var existing in result)
            {
                foreach (var value in grid[key])
                {
                    var copy = new Dictionary<string, object>(existing) { [key] = value };
                    next.Add(copy);
                }
            }
            result = next;
        }
        return result;
    }

    private static ScenarioConfig MergeParameters(ScenarioConfig baseConfig, Dictionary<string, object> overrides)
    {
        var merged = new Dictionary<string, object>(baseConfig.StrategyParameters);
        foreach (var (key, value) in overrides)
            merged[key] = value;

        return baseConfig with { StrategyParameters = merged };
    }

    /// <summary>
    /// For each parameter, computes the standard deviation of Sharpe ratio across its values.
    /// Low sensitivity = plateau (robust). High sensitivity = spike (curve-fit risk).
    /// </summary>
    private static Dictionary<string, decimal> ComputeSensitivity(
        List<BacktestResult> results, Dictionary<string, List<object>> grid)
    {
        var sensitivity = new Dictionary<string, decimal>();
        if (results.Count < 2 || grid.Count == 0) return sensitivity;

        foreach (var paramName in grid.Keys)
        {
            // Group results by this parameter's value (read from StrategyParameters)
            var groups = new Dictionary<string, List<decimal>>();
            foreach (var result in results)
            {
                if (result.ScenarioConfig.StrategyParameters.TryGetValue(paramName, out var val))
                {
                    var key = val?.ToString() ?? "null";
                    if (!groups.ContainsKey(key)) groups[key] = new List<decimal>();
                    groups[key].Add(result.SharpeRatio ?? 0m);
                }
            }

            // Compute std dev of mean Sharpe across parameter values
            if (groups.Count >= 2)
            {
                var means = groups.Values.Select(g => g.Average()).ToList();
                decimal mean = means.Average();
                decimal variance = means.Sum(m => (m - mean) * (m - mean)) / (means.Count - 1);
                sensitivity[paramName] = (decimal)Math.Sqrt((double)variance);
            }
        }

        return sensitivity;
    }
}
