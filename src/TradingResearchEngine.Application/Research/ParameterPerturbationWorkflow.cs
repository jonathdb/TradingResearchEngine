using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>Options for parameter perturbation Monte Carlo.</summary>
public sealed class PerturbationOptions
{
    /// <summary>Number of perturbation runs. Each run jitters all numeric parameters.</summary>
    public int RunCount { get; set; } = 50;

    /// <summary>Maximum percentage jitter applied to each parameter (e.g. 0.10 = ±10%).</summary>
    public decimal JitterPercent { get; set; } = 0.10m;

    /// <summary>Optional RNG seed for reproducibility.</summary>
    public int? Seed { get; set; }
}

/// <summary>Result of parameter perturbation analysis.</summary>
public sealed record PerturbationResult(
    IReadOnlyList<BacktestResult> Results,
    decimal MeanSharpe,
    decimal StdDevSharpe,
    decimal MeanExpectancy,
    decimal WorstSharpe,
    decimal BestSharpe,
    int TotalRunCount);

/// <summary>
/// Jitters each numeric strategy parameter by ±N% and re-runs the engine per perturbation.
/// Measures how sensitive results are to small parameter changes — a key curve-fitting detector.
/// Uses <see cref="ConcurrencyBudget"/> for bounded parallel execution and pre-generates
/// jitter seeds sequentially from the master RNG to ensure deterministic results regardless
/// of scheduling order.
/// </summary>
public sealed class ParameterPerturbationWorkflow
    : IResearchWorkflow<PerturbationOptions, PerturbationResult>
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly ConcurrencyBudget _concurrencyBudget;

    /// <inheritdoc cref="ParameterPerturbationWorkflow"/>
    public ParameterPerturbationWorkflow(RunScenarioUseCase runScenario, ConcurrencyBudget concurrencyBudget)
    {
        _runScenario = runScenario;
        _concurrencyBudget = concurrencyBudget;
    }

    /// <inheritdoc/>
    public async Task<PerturbationResult> RunAsync(
        ScenarioConfig baseConfig, PerturbationOptions options, CancellationToken ct = default)
    {
        return await RunAsync(baseConfig, options, progress: null, ct);
    }

    /// <inheritdoc/>
    public async Task<PerturbationResult> RunAsync(
        ScenarioConfig baseConfig, PerturbationOptions options,
        IProgress<ProgressUpdate>? progress, CancellationToken ct = default)
    {
        if (options.RunCount < 1)
            throw new ArgumentException("RunCount must be >= 1.", nameof(options));

        // 1. Pre-generate jitter seeds sequentially from master RNG (deterministic)
        var masterRng = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        var seeds = new int[options.RunCount];
        for (int i = 0; i < options.RunCount; i++)
            seeds[i] = masterRng.Next();

        // 2. Dispatch perturbation runs in parallel with ConcurrencyBudget
        var results = new BacktestResult?[options.RunCount];
        int completedCount = 0;
        int progressInterval = Math.Max(1, options.RunCount / 100);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, options.RunCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _concurrencyBudget.Available,
                CancellationToken = ct
            },
            async (i, token) =>
            {
                using var permit = await _concurrencyBudget.AcquireAsync(token);

                // Each iteration uses its own deterministic RNG seeded from the pre-generated seed
                var iterationRng = new Random(seeds[i]);
                var jittered = JitterParameters(baseConfig.StrategyParameters, options.JitterPercent, iterationRng);
                var config = baseConfig with { StrategyParameters = jittered };
                var runResult = await _runScenario.RunAsync(config, token, autoSave: false);

                if (runResult.IsSuccess && runResult.Result is not null)
                    results[i] = runResult.Result;

                // Report progress
                int completed = Interlocked.Increment(ref completedCount);
                if (progress is not null && completed % progressInterval == 0)
                {
                    progress.Report(new ProgressUpdate(completed, options.RunCount,
                        $"Completed perturbation run {completed} of {options.RunCount}"));
                }
            });

        // Final progress report
        progress?.Report(new ProgressUpdate(options.RunCount, options.RunCount,
            $"Completed {options.RunCount} perturbation runs"));

        // 3. Collect into indexed array — filter out failed runs
        var successfulResults = results.Where(r => r is not null).ToList()!;

        if (successfulResults.Count == 0)
            throw new InvalidOperationException("All perturbation runs failed.");

        var sharpes = successfulResults.Select(r => r!.SharpeRatio ?? 0m).ToList();
        var expectancies = successfulResults.Select(r => r!.Expectancy ?? 0m).ToList();

        decimal meanSharpe = sharpes.Average();
        decimal stdDevSharpe = StdDev(sharpes);

        // 4. Report correct total run count
        return new PerturbationResult(
            successfulResults!,
            meanSharpe,
            stdDevSharpe,
            expectancies.Average(),
            sharpes.Min(),
            sharpes.Max(),
            options.RunCount);
    }

    private static Dictionary<string, object> JitterParameters(
        Dictionary<string, object> original, decimal jitterPercent, Random rng)
    {
        var result = new Dictionary<string, object>(original);
        foreach (var key in original.Keys.ToList())
        {
            if (original[key] is decimal d)
                result[key] = d * (1m + (decimal)(rng.NextDouble() * 2 - 1) * jitterPercent);
            else if (original[key] is double dbl)
                result[key] = dbl * (1.0 + (rng.NextDouble() * 2 - 1) * (double)jitterPercent);
            else if (original[key] is int n)
                result[key] = Math.Max(1, (int)(n * (1.0 + (rng.NextDouble() * 2 - 1) * (double)jitterPercent)));
        }
        return result;
    }

    private static decimal StdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0m;
        decimal mean = values.Average();
        decimal variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return (decimal)Math.Sqrt((double)variance);
    }
}
