using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Executes one engine run per unique parameter combination in the Cartesian product of a parameter grid.
/// Returns results ranked by Sharpe ratio descending.
/// </summary>
public sealed class ParameterSweepWorkflow : IResearchWorkflow<SweepOptions, SweepResult>
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly IStrategyFactoryProvider _factoryProvider;
    private readonly IOptions<SweepOptions> _options;

    /// <inheritdoc cref="ParameterSweepWorkflow"/>
    public ParameterSweepWorkflow(
        RunScenarioUseCase runScenario,
        IStrategyFactoryProvider factoryProvider,
        IOptions<SweepOptions> options)
    {
        _runScenario = runScenario;
        _factoryProvider = factoryProvider;
        _options = options;
    }

    /// <inheritdoc/>
    public async Task<SweepResult> RunAsync(ScenarioConfig baseConfig, SweepOptions options, CancellationToken ct = default)
    {
        return await RunAsync(baseConfig, options, progress: null, ct);
    }

    /// <inheritdoc/>
    public async Task<SweepResult> RunAsync(
        ScenarioConfig baseConfig, SweepOptions options,
        IProgress<ProgressUpdate>? progress, CancellationToken ct = default)
    {
        var grid = baseConfig.ResearchWorkflowOptions is not null
            && baseConfig.ResearchWorkflowOptions.TryGetValue("ParameterGrid", out var gridObj)
            && gridObj is Dictionary<string, object> rawGrid
                ? ParseGrid(rawGrid)
                : new Dictionary<string, List<object>>();

        var combinations = CartesianProduct(grid);
        if (combinations.Count == 0)
            combinations.Add(new Dictionary<string, object>());

        // Resolve the strategy factory once — thread-safe for concurrent Create() calls
        var effectiveStrategy = baseConfig.EffectiveStrategyConfig;
        var factory = _factoryProvider.GetFactory(effectiveStrategy.StrategyType);

        var results = new ConcurrentBag<BacktestResult>();
        // V6: Formalize SemaphoreSlim concurrency pattern — each combination creates its own EventQueue
        var maxConcurrency = Math.Max(1, Environment.ProcessorCount - 1);
        var semaphore = new SemaphoreSlim(maxConcurrency);
        int completedCount = 0;
        int totalCombinations = combinations.Count;

        await Parallel.ForEachAsync(
            combinations,
            new ParallelOptions { CancellationToken = ct },
            async (combo, token) =>
            {
                await semaphore.WaitAsync(token);
                try
                {
                    var merged = MergeParameters(baseConfig, combo);
                    // Each iteration creates an isolated strategy via factory — never reuse a single IStrategy
                    var iterationStrategyConfig = merged.EffectiveStrategyConfig;
                    var isolatedStrategy = factory.Create(iterationStrategyConfig);
                    var runResult = await _runScenario.RunAsync(merged, token, autoSave: false, strategy: isolatedStrategy);
                    if (runResult.IsSuccess && runResult.Result is not null)
                    {
                        results.Add(runResult.Result);
                    }
                }
                finally
                {
                    semaphore.Release();
                    int current = Interlocked.Increment(ref completedCount);
                    progress?.Report(new ProgressUpdate(current, totalCombinations,
                        $"Completed {current} of {totalCombinations} parameter combinations"));
                }
            });

        var ranked = options.SortBy switch
        {
            SweepSortMetric.MaxDrawdown => results.OrderBy(r => r.MaxDrawdown).ToList(),
            SweepSortMetric.ProfitFactor => results.OrderByDescending(r => r.ProfitFactor ?? decimal.MinValue).ToList(),
            SweepSortMetric.WinRate => results.OrderByDescending(r => r.WinRate ?? decimal.MinValue).ToList(),
            SweepSortMetric.CalmarRatio => results.OrderByDescending(r => r.CalmarRatio ?? decimal.MinValue).ToList(),
            _ => results.OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue).ToList()
        };

        var sensitivity = ComputeSensitivity(ranked, grid);

        // Build cells with multi-metric values for heatmap rendering
        var cells = ranked.Select(r => new SweepCell(
            Parameters: r.ScenarioConfig.StrategyParameters as IReadOnlyDictionary<string, object>
                ?? new Dictionary<string, object>(),
            SharpeRatio: r.SharpeRatio,
            MaxDrawdown: r.MaxDrawdown,
            WinRate: r.WinRate,
            ProfitFactor: r.ProfitFactor,
            TotalTrades: r.TotalTrades)).ToList();

        return new SweepResult(ranked.ToList(), ranked, sensitivity, cells);
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
        // DeepClone ensures all dictionary properties are independent per parallel worker
        var cloned = baseConfig.DeepClone();
        var merged = new Dictionary<string, object>(cloned.StrategyParameters);
        foreach (var (key, value) in overrides)
            merged[key] = value;

        return cloned with { StrategyParameters = merged };
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
