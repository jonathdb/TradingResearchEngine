using System.Collections.Concurrent;
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
/// Partitions data into sequential in-sample/out-of-sample windows.
/// When a <see cref="ParameterGrid"/> is provided, evaluates all parameter combinations
/// per in-sample window via <see cref="GridOptimizer"/> and applies the best to OOS.
/// When no grid is provided, falls back to the existing parameter sweep approach.
/// </summary>
public sealed class WalkForwardWorkflow : IResearchWorkflow<WalkForwardOptions, WalkForwardResult>
{
    private readonly ParameterSweepWorkflow _sweepWorkflow;
    private readonly RunScenarioUseCase _runScenario;
    private readonly IStrategyFactoryProvider _factoryProvider;
    private readonly GridOptimizer _gridOptimizer;
    private readonly ConcurrencyBudget _concurrencyBudget;

    /// <inheritdoc cref="WalkForwardWorkflow"/>
    public WalkForwardWorkflow(
        ParameterSweepWorkflow sweepWorkflow,
        RunScenarioUseCase runScenario,
        IStrategyFactoryProvider factoryProvider,
        ConcurrencyBudget concurrencyBudget)
    {
        _sweepWorkflow = sweepWorkflow;
        _runScenario = runScenario;
        _factoryProvider = factoryProvider;
        _gridOptimizer = new GridOptimizer();
        _concurrencyBudget = concurrencyBudget;
    }

    /// <inheritdoc/>
    public async Task<WalkForwardResult> RunAsync(
        ScenarioConfig baseConfig, WalkForwardOptions options, CancellationToken ct = default)
    {
        return await RunAsync(baseConfig, options, progress: null, ct);
    }

    /// <inheritdoc/>
    public async Task<WalkForwardResult> RunAsync(
        ScenarioConfig baseConfig, WalkForwardOptions options,
        IProgress<ProgressUpdate>? progress, CancellationToken ct = default)
    {
        if (options.StepSize <= TimeSpan.Zero)
            throw new ArgumentException("StepSize must be positive.", nameof(options));

        // Parse data range from config using typed extension methods
#pragma warning disable CS0618 // Legacy dictionary access for backward compatibility
        var dataOpts = baseConfig.DataProviderOptions;
#pragma warning restore CS0618
        var dataFrom = dataOpts.GetFrom();
        var dataTo = dataOpts.GetTo();
        var dataLength = dataTo - dataFrom;

        var windowLength = options.InSampleLength + options.OutOfSampleLength;
        if (dataLength < windowLength)
            throw new InvalidOperationException(
                $"Data range ({dataLength}) is too short for even one window. " +
                $"Minimum required: InSampleLength ({options.InSampleLength}) + OutOfSampleLength ({options.OutOfSampleLength}).");

        // V6: Pre-compute all window date ranges (pure date arithmetic, no I/O)
        var windowSpecs = PrecomputeWindows(options, dataFrom, dataTo);

        if (windowSpecs.Count == 0)
            throw new InvalidOperationException(
                $"Data range too short to form at least one complete window. " +
                $"Minimum required: InSampleLength ({options.InSampleLength}) + OutOfSampleLength ({options.OutOfSampleLength}).");

        // V6: Execute windows in parallel with bounded concurrency via global ConcurrencyBudget
        var results = new ConcurrentBag<WalkForwardWindow>();
        int completedWindows = 0;
        int totalWindows = windowSpecs.Count;

        // Resolve the strategy factory once — thread-safe for concurrent Create() calls
        var effectiveStrategy = baseConfig.EffectiveStrategyConfig;
        var factory = _factoryProvider.GetFactory(effectiveStrategy.StrategyType);

        await Parallel.ForEachAsync(windowSpecs, new ParallelOptions { CancellationToken = ct }, async (spec, token) =>
        {
            using var permit = await _concurrencyBudget.AcquireAsync(token);
            try
            {
                var window = await RunWindowAsync(baseConfig, options, spec, factory, token);
                if (window is not null) results.Add(window);
            }
            finally
            {
                int current = Interlocked.Increment(ref completedWindows);
                progress?.Report(new ProgressUpdate(current, totalWindows,
                    $"Completed window {current} of {totalWindows}"));
            }
        });

        // V6: Sort by window index after parallel collection
        var sorted = results.OrderBy(w => w.WindowIndex).ToList();

        if (sorted.Count == 0)
            throw new InvalidOperationException(
                $"Data range too short to form at least one complete window. " +
                $"Minimum required: InSampleLength ({options.InSampleLength}) + OutOfSampleLength ({options.OutOfSampleLength}).");

        var analytics = ComputeAnalytics(sorted);
        return new WalkForwardResult(sorted, ComputeMeanEfficiency(sorted), analytics);
    }

    /// <summary>Pre-computes all window date ranges without I/O.</summary>
    public static List<WindowSpec> PrecomputeWindows(
        WalkForwardOptions options, DateTimeOffset dataFrom, DateTimeOffset dataTo)
    {
        var specs = new List<WindowSpec>();
        int windowIndex = 0;
        var currentOffset = TimeSpan.Zero;

        while (true)
        {
            var isStart = options.EffectiveMode == WalkForwardMode.Anchored ? dataFrom : dataFrom + currentOffset;
            var isEnd = options.EffectiveMode == WalkForwardMode.Anchored
                ? dataFrom + options.InSampleLength + currentOffset
                : isStart + options.InSampleLength;
            var oosStart = isEnd;
            var oosEnd = oosStart + options.OutOfSampleLength;

            if (oosEnd > dataTo) break;

            specs.Add(new WindowSpec(windowIndex, isStart, isEnd, oosStart, oosEnd));
            windowIndex++;
            currentOffset += options.StepSize;
        }

        return specs;
    }

    /// <summary>Executes a single walk-forward window (IS optimization + OOS validation).</summary>
    private async Task<WalkForwardWindow?> RunWindowAsync(
        ScenarioConfig baseConfig, WalkForwardOptions options, WindowSpec spec,
        IStrategyFactory factory, CancellationToken ct)
    {
        // Build in-sample config with restricted date range
#pragma warning disable CS0618 // Legacy dictionary access for backward compatibility
        var isConfig = baseConfig with
        {
            DataProviderOptions = WithDateRange(baseConfig.DataProviderOptions, spec.IsStart, spec.IsEnd)
        };
#pragma warning restore CS0618

        Dictionary<string, object> bestParams;
        decimal? optimizationMetricValue;
        BacktestResult bestIsResult;

        if (options.Grid is not null && options.Grid.Ranges.Count > 0)
        {
            // Grid optimization path: evaluate all parameter combinations per IS window
            var (result, metricValue) = await RunGridOptimizationAsync(
                isConfig, options.Grid, options.Objective, factory, ct);

            if (result is null) return null;

            bestIsResult = result;
            bestParams = new Dictionary<string, object>(result.ScenarioConfig.StrategyParameters);
            optimizationMetricValue = metricValue;
        }
        else
        {
            // Legacy path: use parameter sweep workflow
            var sweepOptions = new SweepOptions();
            var sweepResult = await _sweepWorkflow.RunAsync(isConfig, sweepOptions, ct);

            if (sweepResult.RankedBySharpe.Count == 0) return null;

            bestIsResult = sweepResult.RankedBySharpe[0];
            bestParams = new Dictionary<string, object>(bestIsResult.ScenarioConfig.StrategyParameters);
            optimizationMetricValue = null;
        }

        // Run engine on out-of-sample with best params — creates its own EventQueue
#pragma warning disable CS0618 // Legacy dictionary access for backward compatibility
        var oosConfig = baseConfig with
        {
            StrategyParameters = new Dictionary<string, object>(bestParams),
            DataProviderOptions = WithDateRange(baseConfig.DataProviderOptions, spec.OosStart, spec.OosEnd)
        };
#pragma warning restore CS0618

        // Create an isolated strategy instance for this OOS iteration via factory
        var oosStrategyConfig = oosConfig.EffectiveStrategyConfig;
        var isolatedStrategy = factory.Create(oosStrategyConfig);

        // Reset strategy state before OOS window to ensure clean indicator state
        isolatedStrategy.Reset();
        isolatedStrategy.Initialize(oosStrategyConfig);

        var oosRunResult = await _runScenario.RunAsync(oosConfig, ct, autoSave: false, strategy: isolatedStrategy);

        if (oosRunResult.IsSuccess && oosRunResult.Result is not null)
        {
            decimal? efficiency = (bestIsResult.SharpeRatio.HasValue && bestIsResult.SharpeRatio.Value != 0m)
                ? oosRunResult.Result.SharpeRatio / bestIsResult.SharpeRatio
                : null;

            return new WalkForwardWindow(
                spec.WindowIndex, bestIsResult, oosRunResult.Result,
                bestParams, efficiency, optimizationMetricValue, options.Objective);
        }

        return null;
    }

    /// <summary>
    /// Runs grid optimization for a single in-sample window: generates all parameter combinations
    /// from the grid, runs a backtest for each, and selects the best via <see cref="GridOptimizer"/>.
    /// Each window's optimization is independent — no information leaks between windows.
    /// </summary>
    private async Task<(BacktestResult? Best, decimal? MetricValue)> RunGridOptimizationAsync(
        ScenarioConfig isConfig, ParameterGrid grid, OptimizationObjective objective,
        IStrategyFactory factory, CancellationToken ct)
    {
        var combinations = GenerateCombinations(grid);
        if (combinations.Count == 0) return (null, null);

        // Run a backtest for each parameter combination using only the IS data
        var candidates = new ConcurrentBag<BacktestResult>();

        await Parallel.ForEachAsync(
            combinations,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            async (combo, token) =>
            {
                var merged = MergeParameters(isConfig, combo);
                var iterationStrategyConfig = merged.EffectiveStrategyConfig;
                var isolatedStrategy = factory.Create(iterationStrategyConfig);
                var runResult = await _runScenario.RunAsync(merged, token, autoSave: false, strategy: isolatedStrategy);
                if (runResult.IsSuccess && runResult.Result is not null)
                {
                    candidates.Add(runResult.Result);
                }
            });

        if (candidates.IsEmpty) return (null, null);

        // Use GridOptimizer to select the best combination based on the configured objective
        var optimizationResult = _gridOptimizer.Optimize(candidates.ToList(), objective);

        if (optimizationResult.BestParameters.Count == 0) return (null, null);

        // Find the candidate that matches the best parameters
        var bestCandidate = candidates.FirstOrDefault(c =>
            ParametersMatch(c.ScenarioConfig.StrategyParameters, optimizationResult.BestParameters));

        return (bestCandidate, optimizationResult.ObjectiveValue);
    }

    /// <summary>
    /// Generates all parameter combinations from a <see cref="ParameterGrid"/> by computing
    /// the Cartesian product of all ranges. Each range generates values from Start to End
    /// (inclusive) stepping by Step.
    /// </summary>
    internal static List<Dictionary<string, object>> GenerateCombinations(ParameterGrid grid)
    {
        if (grid.Ranges.Count == 0) return new List<Dictionary<string, object>>();

        var result = new List<Dictionary<string, object>> { new() };

        foreach (var range in grid.Ranges)
        {
            if (range.Step <= 0m)
                throw new ArgumentException(
                    $"Parameter '{range.Name}' has non-positive Step ({range.Step}). Step must be > 0.");

            var values = GenerateRangeValues(range);
            if (values.Count == 0) continue;

            var next = new List<Dictionary<string, object>>();
            foreach (var existing in result)
            {
                foreach (var value in values)
                {
                    var copy = new Dictionary<string, object>(existing) { [range.Name] = value };
                    next.Add(copy);
                }
            }
            result = next;
        }

        return result;
    }

    /// <summary>
    /// Generates all values for a single parameter range: [Start, End] inclusive with Step increment.
    /// </summary>
    private static List<object> GenerateRangeValues(ParameterRange range)
    {
        var values = new List<object>();
        for (var v = range.Start; v <= range.End; v += range.Step)
        {
            values.Add(v);
        }
        return values;
    }

    /// <summary>
    /// Merges parameter overrides into a base config, creating an independent copy.
    /// </summary>
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
    /// Checks whether two parameter dictionaries have matching values for all keys in <paramref name="target"/>.
    /// </summary>
    private static bool ParametersMatch(
        Dictionary<string, object> candidate, Dictionary<string, object> target)
    {
        foreach (var (key, value) in target)
        {
            if (!candidate.TryGetValue(key, out var candidateValue))
                return false;

            // Compare as strings to handle decimal/int/double boxing differences
            if (!string.Equals(candidateValue?.ToString(), value?.ToString(), StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>V6: Describes a pre-computed walk-forward window's date boundaries.</summary>
    public sealed record WindowSpec(
        int WindowIndex,
        DateTimeOffset IsStart,
        DateTimeOffset IsEnd,
        DateTimeOffset OosStart,
        DateTimeOffset OosEnd);

    private static Dictionary<string, object> WithDateRange(
        Dictionary<string, object> original, DateTimeOffset from, DateTimeOffset to)
    {
        var copy = new Dictionary<string, object>(original)
        {
            ["From"] = from,
            ["To"] = to
        };
        return copy;
    }

    /// <summary>
    /// Computes enriched walk-forward analytics from completed windows.
    /// Includes OOS profitability rate, concatenated equity curve, parameter drift, and parameter history.
    /// </summary>
    internal static WalkForwardAnalytics ComputeAnalytics(IReadOnlyList<WalkForwardWindow> windows)
    {
        var oosProfitabilityRate = ComputeOosProfitabilityRate(windows);
        var concatenatedCurve = StitchOosEquityCurves(windows);
        var driftScore = ComputeParameterDrift(windows);
        var parameterHistory = windows
            .Where(w => w.SelectedParameters.Count > 0 && w.OptimizationMetricValue.HasValue)
            .Select(w => new ParameterWindowSnapshot(
                w.WindowIndex,
                new Dictionary<string, object>(w.SelectedParameters),
                w.OptimizationMetricValue!.Value))
            .ToList();

        return new WalkForwardAnalytics(
            oosProfitabilityRate,
            concatenatedCurve,
            driftScore,
            parameterHistory);
    }

    /// <summary>
    /// Computes the fraction of OOS windows that are profitable.
    /// A window is profitable when its OOS result has EndEquity greater than StartEquity.
    /// </summary>
    internal static decimal ComputeOosProfitabilityRate(IReadOnlyList<WalkForwardWindow> windows)
    {
        if (windows.Count == 0) return 0m;

        int profitableCount = windows.Count(w => w.OutOfSampleResult.EndEquity > w.OutOfSampleResult.StartEquity);
        return (decimal)profitableCount / windows.Count;
    }

    private static decimal? ComputeMeanEfficiency(IReadOnlyList<WalkForwardWindow> windows)
    {
        var ratios = windows.Where(w => w.EfficiencyRatio.HasValue).Select(w => w.EfficiencyRatio!.Value).ToList();
        return ratios.Count > 0 ? ratios.Average() : null;
    }

    /// <summary>Builds a <see cref="WalkForwardSummary"/> from a completed walk-forward result.</summary>
    public static WalkForwardSummary BuildSummary(WalkForwardResult result)
    {
        var windows = result.Windows;
        var composite = StitchOosEquityCurves(windows);
        var avgOosSharpe = windows
            .Where(w => w.OutOfSampleResult.SharpeRatio.HasValue)
            .Select(w => w.OutOfSampleResult.SharpeRatio!.Value)
            .DefaultIfEmpty(0m)
            .Average();
        var worstDd = windows
            .Select(w => w.OutOfSampleResult.MaxDrawdown)
            .DefaultIfEmpty(0m)
            .Max();
        var drift = ComputeParameterDrift(windows);
        var oosProfitabilityRate = ComputeOosProfitabilityRate(windows);

        return new WalkForwardSummary(
            windows, composite, avgOosSharpe, worstDd, drift, result.MeanEfficiencyRatio, oosProfitabilityRate);
    }

    /// <summary>
    /// Stitches OOS equity curves by chaining end equity of window N as start of window N+1.
    /// </summary>
    private static List<Core.Portfolio.EquityCurvePoint> StitchOosEquityCurves(
        IReadOnlyList<WalkForwardWindow> windows)
    {
        var composite = new List<Core.Portfolio.EquityCurvePoint>();

        foreach (var window in windows)
        {
            var curve = window.OutOfSampleResult.EquityCurve;
            if (curve.Count == 0) continue;

            decimal windowStart = curve[0].TotalEquity;
            foreach (var point in curve)
            {
                decimal adjusted = point.TotalEquity - windowStart + (composite.Count > 0
                    ? composite[^1].TotalEquity
                    : windowStart);
                composite.Add(point with { TotalEquity = adjusted });
            }
        }
        return composite;
    }

    /// <summary>
    /// Computes parameter drift score: normalised standard deviation of selected parameter values
    /// across windows. High drift = parameters are unstable across time.
    /// </summary>
    private static decimal ComputeParameterDrift(IReadOnlyList<WalkForwardWindow> windows)
    {
        if (windows.Count < 2) return 0m;

        // Collect all parameter names
        var allKeys = windows
            .SelectMany(w => w.SelectedParameters.Keys)
            .Distinct()
            .ToList();

        if (allKeys.Count == 0) return 0m;

        decimal totalDrift = 0m;
        int paramCount = 0;

        foreach (var key in allKeys)
        {
            var values = windows
                .Where(w => w.SelectedParameters.ContainsKey(key))
                .Select(w =>
                {
                    var val = w.SelectedParameters[key];
                    if (val is decimal d) return d;
                    if (val is int i) return (decimal)i;
                    if (val is double dbl) return (decimal)dbl;
                    if (decimal.TryParse(val?.ToString(), out var parsed)) return parsed;
                    return (decimal?)null;
                })
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();

            if (values.Count < 2) continue;

            decimal mean = values.Average();
            if (mean == 0m) continue;

            decimal variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
            decimal stdDev = (decimal)Math.Sqrt((double)variance);
            totalDrift += stdDev / Math.Abs(mean); // coefficient of variation
            paramCount++;
        }

        return paramCount > 0 ? totalDrift / paramCount : 0m;
    }
}
