using System.Collections.Concurrent;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Runs the same strategy under FastResearch, StandardBacktest, and BrokerConservative
/// realism profiles and reports performance degradation across profiles.
/// Uses <see cref="ConcurrencyBudget"/> for bounded parallel execution since all three
/// profiles are independent runs.
/// </summary>
public sealed class RealismSensitivityWorkflow
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly ConcurrencyBudget _concurrencyBudget;

    public RealismSensitivityWorkflow(RunScenarioUseCase runScenario, ConcurrencyBudget concurrencyBudget)
    {
        _runScenario = runScenario;
        _concurrencyBudget = concurrencyBudget;
    }

    /// <summary>Runs the strategy under all three realism profiles.</summary>
    public async Task<RealismSensitivityResult> RunAsync(
        ScenarioConfig baseConfig, RealismSensitivityOptions options, CancellationToken ct = default)
    {
        var profiles = new[]
        {
            ExecutionRealismProfile.FastResearch,
            ExecutionRealismProfile.StandardBacktest,
            ExecutionRealismProfile.BrokerConservative
        };

        var results = new ConcurrentBag<RealismProfileResult>();

        await Parallel.ForEachAsync(profiles, new ParallelOptions { CancellationToken = ct }, async (profile, token) =>
        {
            using var permit = await _concurrencyBudget.AcquireAsync(token);

            var config = baseConfig with
            {
                RealismProfile = profile,
                FillMode = profile == ExecutionRealismProfile.FastResearch
                    ? FillMode.SameBarClose
                    : FillMode.NextBarOpen
            };

            var runResult = await _runScenario.RunAsync(config, token, autoSave: false);
            if (runResult.IsSuccess && runResult.Result is not null)
            {
                var r = runResult.Result;
                decimal cagr = r.StartEquity > 0m ? (r.EndEquity - r.StartEquity) / r.StartEquity : 0m;
                results.Add(new RealismProfileResult(profile, r, cagr, r.SharpeRatio, r.MaxDrawdown, r.ProfitFactor));
            }
        });

        var sortedResults = results.OrderBy(r => r.Profile).ToList();

        decimal fastSharpe = sortedResults.FirstOrDefault(r => r.Profile == ExecutionRealismProfile.FastResearch)?.Sharpe ?? 0m;
        decimal stdSharpe = sortedResults.FirstOrDefault(r => r.Profile == ExecutionRealismProfile.StandardBacktest)?.Sharpe ?? 0m;
        decimal consSharpe = sortedResults.FirstOrDefault(r => r.Profile == ExecutionRealismProfile.BrokerConservative)?.Sharpe ?? 0m;

        return new RealismSensitivityResult(
            sortedResults,
            fastSharpe - stdSharpe,
            stdSharpe - consSharpe);
    }
}
