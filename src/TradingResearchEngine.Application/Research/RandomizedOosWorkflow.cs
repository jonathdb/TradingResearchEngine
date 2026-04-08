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
}

/// <summary>Result of randomized OOS testing.</summary>
public sealed record RandomizedOosResult(
    IReadOnlyList<RandomizedOosIteration> Iterations,
    decimal MeanOosSharpe,
    decimal StdDevOosSharpe,
    decimal MeanIsSharpe);

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

        if (allBars.Count < 10)
            throw new InvalidOperationException($"Insufficient data: {allBars.Count} bars. Need at least 10.");

        var rng = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        int oosCount = Math.Max(1, (int)(allBars.Count * options.OosFraction));
        var iterations = new List<RandomizedOosIteration>();

        for (int iter = 0; iter < options.Iterations; iter++)
        {
            ct.ThrowIfCancellationRequested();

            // Randomly select OOS indices
            var indices = Enumerable.Range(0, allBars.Count).ToList();
            Shuffle(indices, rng);
            var oosIndices = new HashSet<int>(indices.Take(oosCount));

            var isBars = allBars.Where((_, i) => !oosIndices.Contains(i)).ToList();
            var oosBars = allBars.Where((_, i) => oosIndices.Contains(i)).OrderBy(b => b.Timestamp).ToList();

            // Create in-memory data provider configs with filtered bars
            // We pass the bar lists via a convention key that the engine can use
            var isConfig = baseConfig with
            {
                DataProviderType = "memory",
                DataProviderOptions = WithBarIndices(baseConfig.DataProviderOptions, isBars)
            };
            var oosConfig = baseConfig with
            {
                DataProviderType = "memory",
                DataProviderOptions = WithBarIndices(baseConfig.DataProviderOptions, oosBars)
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
        }

        if (iterations.Count == 0)
            throw new InvalidOperationException("All randomized OOS iterations failed.");

        var oosSharpes = iterations.Select(i => i.OutOfSampleResult.SharpeRatio ?? 0m).ToList();
        var isSharpes = iterations.Select(i => i.InSampleResult.SharpeRatio ?? 0m).ToList();

        return new RandomizedOosResult(
            iterations,
            oosSharpes.Average(),
            StdDev(oosSharpes),
            isSharpes.Average());
    }

    private static Dictionary<string, object> WithBarIndices(
        Dictionary<string, object> original, List<BarRecord> bars)
    {
        var copy = new Dictionary<string, object>(original)
        {
            ["FilteredBars"] = bars
        };
        return copy;
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static decimal StdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0m;
        decimal mean = values.Average();
        decimal variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return (decimal)Math.Sqrt((double)variance);
    }
}
