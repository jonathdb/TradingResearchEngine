using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Partitions data into sequential in-sample/out-of-sample windows.
/// Runs a parameter sweep on in-sample, then validates on out-of-sample.
/// </summary>
public sealed class WalkForwardWorkflow : IResearchWorkflow<WalkForwardOptions, WalkForwardResult>
{
    private readonly ParameterSweepWorkflow _sweepWorkflow;
    private readonly RunScenarioUseCase _runScenario;

    /// <inheritdoc cref="WalkForwardWorkflow"/>
    public WalkForwardWorkflow(ParameterSweepWorkflow sweepWorkflow, RunScenarioUseCase runScenario)
    {
        _sweepWorkflow = sweepWorkflow;
        _runScenario = runScenario;
    }

    /// <inheritdoc/>
    public async Task<WalkForwardResult> RunAsync(
        ScenarioConfig baseConfig, WalkForwardOptions options, CancellationToken ct = default)
    {
        if (options.StepSize <= TimeSpan.Zero)
            throw new ArgumentException("StepSize must be positive.", nameof(options));

        // Parse data range from config
        var dataOpts = baseConfig.DataProviderOptions;
        var dataFrom = dataOpts.TryGetValue("From", out var f) && f is DateTimeOffset df
            ? df : DateTimeOffset.MinValue;
        var dataTo = dataOpts.TryGetValue("To", out var t) && t is DateTimeOffset dt
            ? dt : DateTimeOffset.MaxValue;
        var dataLength = dataTo - dataFrom;

        var windowLength = options.InSampleLength + options.OutOfSampleLength;
        if (dataLength < windowLength)
            throw new InvalidOperationException(
                $"Data range ({dataLength}) is too short for even one window. " +
                $"Minimum required: InSampleLength ({options.InSampleLength}) + OutOfSampleLength ({options.OutOfSampleLength}).");

        var windows = new List<WalkForwardWindow>();
        int windowIndex = 0;
        var currentOffset = TimeSpan.Zero;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var isStart = options.EffectiveMode == WalkForwardMode.Anchored ? dataFrom : dataFrom + currentOffset;
            var isEnd = options.EffectiveMode == WalkForwardMode.Anchored
                ? dataFrom + options.InSampleLength + currentOffset  // Anchored: start fixed, end advances (expanding window)
                : isStart + options.InSampleLength;                  // Rolling: fixed-length window slides forward
            var oosStart = isEnd;
            var oosEnd = oosStart + options.OutOfSampleLength;

            // Stop if out-of-sample end exceeds data range
            if (oosEnd > dataTo) break;

            // Build in-sample config with restricted date range
            var isConfig = baseConfig with
            {
                DataProviderOptions = WithDateRange(baseConfig.DataProviderOptions, isStart, isEnd)
            };

            // Run sweep on in-sample segment
            var sweepOptions = new SweepOptions();
            var sweepResult = await _sweepWorkflow.RunAsync(isConfig, sweepOptions, ct);

            if (sweepResult.RankedBySharpe.Count == 0) break;

            var bestResult = sweepResult.RankedBySharpe[0];
            var bestParams = bestResult.ScenarioConfig.StrategyParameters;

            // Run engine on out-of-sample with best params and restricted date range
            var oosConfig = baseConfig with
            {
                StrategyParameters = new Dictionary<string, object>(bestParams),
                DataProviderOptions = WithDateRange(baseConfig.DataProviderOptions, oosStart, oosEnd)
            };
            var oosRunResult = await _runScenario.RunAsync(oosConfig, ct, autoSave: false);

            if (oosRunResult.IsSuccess && oosRunResult.Result is not null)
            {
                decimal? efficiency = (bestResult.SharpeRatio.HasValue && bestResult.SharpeRatio.Value != 0m)
                    ? oosRunResult.Result.SharpeRatio / bestResult.SharpeRatio
                    : null;

                windows.Add(new WalkForwardWindow(
                    windowIndex, bestResult, oosRunResult.Result,
                    new Dictionary<string, object>(bestParams), efficiency));
            }

            windowIndex++;
            currentOffset += options.StepSize;
        }

        if (windows.Count == 0)
            throw new InvalidOperationException(
                $"Data range too short to form at least one complete window. " +
                $"Minimum required: InSampleLength ({options.InSampleLength}) + OutOfSampleLength ({options.OutOfSampleLength}).");

        return new WalkForwardResult(windows, ComputeMeanEfficiency(windows));
    }

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

        return new WalkForwardSummary(
            windows, composite, avgOosSharpe, worstDd, drift, result.MeanEfficiencyRatio);
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
