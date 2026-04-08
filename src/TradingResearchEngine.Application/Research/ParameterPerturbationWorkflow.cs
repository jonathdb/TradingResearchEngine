using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
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
    decimal BestSharpe);

/// <summary>
/// Jitters each numeric strategy parameter by ±N% and re-runs the engine per perturbation.
/// Measures how sensitive results are to small parameter changes — a key curve-fitting detector.
/// </summary>
public sealed class ParameterPerturbationWorkflow
    : IResearchWorkflow<PerturbationOptions, PerturbationResult>
{
    private readonly RunScenarioUseCase _runScenario;

    /// <inheritdoc cref="ParameterPerturbationWorkflow"/>
    public ParameterPerturbationWorkflow(RunScenarioUseCase runScenario)
        => _runScenario = runScenario;

    /// <inheritdoc/>
    public async Task<PerturbationResult> RunAsync(
        ScenarioConfig baseConfig, PerturbationOptions options, CancellationToken ct = default)
    {
        var rng = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        var results = new List<BacktestResult>();

        for (int i = 0; i < options.RunCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            var jittered = JitterParameters(baseConfig.StrategyParameters, options.JitterPercent, rng);
            var config = baseConfig with { StrategyParameters = jittered };
            var runResult = await _runScenario.RunAsync(config, ct, autoSave: false);

            if (runResult.IsSuccess && runResult.Result is not null)
                results.Add(runResult.Result);
        }

        if (results.Count == 0)
            throw new InvalidOperationException("All perturbation runs failed.");

        var sharpes = results.Select(r => r.SharpeRatio ?? 0m).ToList();
        var expectancies = results.Select(r => r.Expectancy ?? 0m).ToList();

        decimal meanSharpe = sharpes.Average();
        decimal stdDevSharpe = StdDev(sharpes);

        return new PerturbationResult(
            results, meanSharpe, stdDevSharpe,
            expectancies.Average(), sharpes.Min(), sharpes.Max());
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
