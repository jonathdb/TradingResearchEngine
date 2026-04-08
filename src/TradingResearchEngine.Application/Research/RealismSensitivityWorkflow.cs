using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Runs the same strategy under FastResearch, StandardBacktest, and BrokerConservative
/// realism profiles and reports performance degradation across profiles.
/// </summary>
public sealed class RealismSensitivityWorkflow
{
    private readonly RunScenarioUseCase _runScenario;

    public RealismSensitivityWorkflow(RunScenarioUseCase runScenario) => _runScenario = runScenario;

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

        var results = new List<RealismProfileResult>();

        foreach (var profile in profiles)
        {
            ct.ThrowIfCancellationRequested();

            var config = baseConfig with
            {
                RealismProfile = profile,
                FillMode = profile == ExecutionRealismProfile.FastResearch
                    ? FillMode.SameBarClose
                    : FillMode.NextBarOpen
            };

            var runResult = await _runScenario.RunAsync(config, ct);
            if (runResult.IsSuccess && runResult.Result is not null)
            {
                var r = runResult.Result;
                decimal cagr = r.StartEquity > 0m ? (r.EndEquity - r.StartEquity) / r.StartEquity : 0m;
                results.Add(new RealismProfileResult(profile, r, cagr, r.SharpeRatio, r.MaxDrawdown, r.ProfitFactor));
            }
        }

        decimal fastSharpe = results.FirstOrDefault(r => r.Profile == ExecutionRealismProfile.FastResearch)?.Sharpe ?? 0m;
        decimal stdSharpe = results.FirstOrDefault(r => r.Profile == ExecutionRealismProfile.StandardBacktest)?.Sharpe ?? 0m;
        decimal consSharpe = results.FirstOrDefault(r => r.Profile == ExecutionRealismProfile.BrokerConservative)?.Sharpe ?? 0m;

        return new RealismSensitivityResult(
            results,
            fastSharpe - stdSharpe,
            stdSharpe - consSharpe);
    }
}
