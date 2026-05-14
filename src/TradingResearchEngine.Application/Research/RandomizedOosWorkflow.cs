using System.Runtime.CompilerServices;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>Options for randomized out-of-sample testing.</summary>
public sealed class RandomizedOosOptions
{
    /// <summary>Fraction of bars to withhold as OOS (e.g. 0.20 = 20%).</summary>
    public decimal OosFraction { get; set; } = 0.20m;

    /// <summary>Number of random OOS partitions to run.</summary>
    public int Iterations { get; set; } = 10;

    /// <summary>Optional RNG seed for reproducibility.</summary>
    public int? Seed { get; set; }

    /// <summary>
    /// Number of bars prepended to the OOS window as indicator warmup context.
    /// These bars are fed to the engine but not counted in OOS performance measurement.
    /// Default is 200 to accommodate common long-period indicators.
    /// </summary>
    public int WarmupBars { get; set; } = 200;
}

/// <summary>
/// Result of randomized OOS testing.
/// <para><see cref="MeanOosSharpe"/> is computed as the arithmetic mean of succeeded iterations only.</para>
/// </summary>
public sealed record RandomizedOosResult(
    IReadOnlyList<RandomizedOosIteration> Iterations,
    decimal MeanOosSharpe,
    decimal StdDevOosSharpe,
    decimal MeanIsSharpe,
    /// <summary>Number of iterations that threw or returned invalid results.</summary>
    int FailedIterationCount,
    /// <summary>Realism advisories emitted when failure rate exceeds thresholds.</summary>
    IReadOnlyList<string>? Advisories = null);

/// <summary>A single iteration of randomized OOS testing.</summary>
public sealed record RandomizedOosIteration(
    int IterationIndex,
    BacktestResult InSampleResult,
    BacktestResult OutOfSampleResult,
    decimal? EfficiencyRatio);

/// <summary>
/// Randomly selects non-contiguous bar indices as OOS, trains on the rest, tests on the withheld bars.
/// Addresses the BuildAlpha concern that fixed OOS periods can be misleading.
/// </summary>
public sealed class RandomizedOosWorkflow
    : IResearchWorkflow<RandomizedOosOptions, RandomizedOosResult>
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly IDataProvider _dataProvider;

    /// <inheritdoc cref="RandomizedOosWorkflow"/>
    public RandomizedOosWorkflow(RunScenarioUseCase runScenario, IDataProvider dataProvider)
    {
        _runScenario = runScenario;
        _dataProvider = dataProvider;
    }

    /// <inheritdoc/>
    public async Task<RandomizedOosResult> RunAsync(
        ScenarioConfig baseConfig, RandomizedOosOptions options, CancellationToken ct = default)
    {
        if (options.OosFraction <= 0m || options.OosFraction >= 1m)
            throw new ArgumentException("OosFraction must be between 0 and 1 exclusive.", nameof(options));
        if (options.Iterations < 1)
            throw new ArgumentException("Iterations must be >= 1.", nameof(options));

        // Load all bars into memory for partitioning
        var dataOpts = baseConfig.DataProviderOptions;
        string symbol = dataOpts.TryGetValue("Symbol", out var s) ? s?.ToString() ?? "" : "";
        string interval = dataOpts.TryGetValue("Interval", out var iv) ? iv?.ToString() ?? "1D" : "1D";
        var from = dataOpts.TryGetValue("From", out var f) && f is DateTimeOffset df ? df : DateTimeOffset.MinValue;
        var to = dataOpts.TryGetValue("To", out var t) && t is DateTimeOffset dt ? dt : DateTimeOffset.MaxValue;

        var allBars = new List<BarRecord>();
        await foreach (var bar in _dataProvider.GetBars(symbol, interval, from, to, ct))
            allBars.Add(bar);

        int oosCount = Math.Max(1, (int)(allBars.Count * options.OosFraction));
        int warmupBuffer = options.WarmupBars;

        // Validate sufficient data for OOS fraction + warmup
        if (allBars.Count < oosCount + warmupBuffer + 10)
            throw new InvalidOperationException(
                $"Insufficient data for OOS fraction {options.OosFraction} with warmup {warmupBuffer} bars. " +
                $"Have {allBars.Count} bars, need at least {oosCount + warmupBuffer + 10}.");

        var rng = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        var iterations = new List<RandomizedOosIteration>();
        int failedCount = 0;

        for (int iter = 0; iter < options.Iterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            // Pick a random OOS start index leaving room for a full OOS window
            int maxOosStart = allBars.Count - oosCount;
            int oosStart = rng.Next(warmupBuffer, maxOosStart + 1);

            var oosBars = allBars.Skip(oosStart).Take(oosCount).ToList();
            var isBars = allBars.Take(oosStart)
                               .Concat(allBars.Skip(oosStart + oosCount))
                               .ToList();

            // Prepend warmup bars before OOS start for indicator warmup context
            int warmupStart = Math.Max(0, oosStart - warmupBuffer);
            var oosWithWarmup = allBars.Skip(warmupStart).Take(oosStart - warmupStart)
                                       .Concat(oosBars).ToList();

            // Create in-memory data provider configs with filtered bars
            var isConfig = baseConfig.DeepClone() with
            {
                DataProviderType = "memory",
                DataProviderOptions = WithBarIndices(baseConfig.DataProviderOptions, isBars)
            };
            var oosConfig = baseConfig.DeepClone() with
            {
                DataProviderType = "memory",
                DataProviderOptions = WithBarIndices(baseConfig.DataProviderOptions, oosWithWarmup,
                    warmupBars: oosStart - warmupStart)
            };

            var isResult = await _runScenario.RunAsync(isConfig, ct, autoSave: false);
            var oosResult = await _runScenario.RunAsync(oosConfig, ct, autoSave: false);

            if (isResult.IsSuccess && isResult.Result is not null
                && oosResult.IsSuccess && oosResult.Result is not null)
            {
                decimal? efficiency = (isResult.Result.SharpeRatio.HasValue && isResult.Result.SharpeRatio.Value != 0m)
                    ? oosResult.Result.SharpeRatio / isResult.Result.SharpeRatio
                    : null;

                iterations.Add(new RandomizedOosIteration(iter, isResult.Result, oosResult.Result, efficiency));
            }
            else
            {
                Interlocked.Increment(ref failedCount);
            }
        }

        if (iterations.Count == 0)
            throw new InvalidOperationException("All randomized OOS iterations failed.");

        var oosSharpes = iterations.Select(i => i.OutOfSampleResult.SharpeRatio ?? 0m).ToList();
        var isSharpes = iterations.Select(i => i.InSampleResult.SharpeRatio ?? 0m).ToList();

        // Emit realism advisory when failure rate exceeds 20%
        List<string>? advisories = null;
        if (failedCount > 0 && (double)failedCount / options.Iterations > 0.20)
        {
            advisories = [$"High OOS iteration failure rate: {failedCount}/{options.Iterations} iterations failed ({(double)failedCount / options.Iterations:P0}). Mean OOS Sharpe is computed over {iterations.Count} succeeded iterations only."];
        }

        return new RandomizedOosResult(
            iterations,
            oosSharpes.Average(),
            StdDev(oosSharpes),
            isSharpes.Average(),
            failedCount,
            advisories);
    }

    private static Dictionary<string, object> WithBarIndices(
        Dictionary<string, object> original, List<BarRecord> bars, int warmupBars = 0)
    {
        var copy = new Dictionary<string, object>(original)
        {
            ["FilteredBars"] = bars
        };
        if (warmupBars > 0)
            copy["WarmupBars"] = warmupBars;
        return copy;
    }

    private static decimal StdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0m;
        var doubles = values.Select(v => (double)v).ToList();
        double mean = doubles.Average();
        double variance = doubles.Sum(v => (v - mean) * (v - mean)) / doubles.Count;
        return (decimal)Math.Sqrt(variance);
    }
}
