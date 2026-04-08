using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Reruns a strategy under cost and delay perturbations to identify
/// strategies that only work under optimistic execution assumptions.
/// </summary>
public sealed class SensitivityAnalysisWorkflow
{
    private readonly RunScenarioUseCase _runScenario;

    public SensitivityAnalysisWorkflow(RunScenarioUseCase runScenario) => _runScenario = runScenario;

    /// <summary>Runs sensitivity analysis on a base config.</summary>
    public async Task<SensitivityResult> RunAsync(
        ScenarioConfig baseConfig, SensitivityOptions options, CancellationToken ct = default)
    {
        var baseResult = await _runScenario.RunAsync(baseConfig, ct);
        if (!baseResult.IsSuccess || baseResult.Result is null)
            throw new InvalidOperationException("Base scenario run failed.");

        var rows = new List<SensitivityRow>();
        var baseSharpe = baseResult.Result.SharpeRatio ?? 0m;

        // Base row
        rows.Add(ToRow("Base", baseResult.Result));

        // Spread perturbations (simulate via slippage model type override)
        decimal costDegradation = 0m;
        int costCount = 0;
        foreach (var mult in options.SpreadMultipliers)
        {
            var config = baseConfig with
            {
                RiskParameters = WithOverride(baseConfig.RiskParameters, "SpreadMultiplier", mult)
            };
            var result = await RunSafe(config, ct);
            if (result is not null)
            {
                rows.Add(ToRow($"Spread x{mult:F2}", result));
                costDegradation += baseSharpe - (result.SharpeRatio ?? 0m);
                costCount++;
            }
        }

        // Slippage perturbations
        foreach (var mult in options.SlippageMultipliers)
        {
            var config = baseConfig with
            {
                RiskParameters = WithOverride(baseConfig.RiskParameters, "SlippageMultiplier", mult)
            };
            var result = await RunSafe(config, ct);
            if (result is not null)
            {
                rows.Add(ToRow($"Slippage x{mult:F2}", result));
                costDegradation += baseSharpe - (result.SharpeRatio ?? 0m);
                costCount++;
            }
        }

        // Delay perturbations
        decimal delayDegradation = 0m;
        int delayCount = 0;
        if (options.TestEntryDelay)
        {
            var config = baseConfig with
            {
                RiskParameters = WithOverride(baseConfig.RiskParameters, "EntryDelayBars", 1)
            };
            var result = await RunSafe(config, ct);
            if (result is not null)
            {
                rows.Add(ToRow("Entry Delay +1 bar", result));
                delayDegradation += baseSharpe - (result.SharpeRatio ?? 0m);
                delayCount++;
            }
        }
        if (options.TestExitDelay)
        {
            var config = baseConfig with
            {
                RiskParameters = WithOverride(baseConfig.RiskParameters, "ExitDelayBars", 1)
            };
            var result = await RunSafe(config, ct);
            if (result is not null)
            {
                rows.Add(ToRow("Exit Delay +1 bar", result));
                delayDegradation += baseSharpe - (result.SharpeRatio ?? 0m);
                delayCount++;
            }
        }

        decimal costSensitivity = costCount > 0 ? costDegradation / costCount : 0m;
        decimal delaySensitivity = delayCount > 0 ? delayDegradation / delayCount : 0m;

        // Robustness = average Sharpe across all perturbations / base Sharpe
        var allSharpes = rows.Skip(1).Where(r => r.Sharpe.HasValue).Select(r => r.Sharpe!.Value).ToList();
        decimal robustness = baseSharpe != 0m && allSharpes.Count > 0
            ? allSharpes.Average() / baseSharpe
            : 0m;

        return new SensitivityResult(rows, costSensitivity, delaySensitivity, robustness);
    }

    private async Task<BacktestResult?> RunSafe(ScenarioConfig config, CancellationToken ct)
    {
        try
        {
            var result = await _runScenario.RunAsync(config, ct);
            return result.IsSuccess ? result.Result : null;
        }
        catch { return null; }
    }

    private static SensitivityRow ToRow(string name, BacktestResult r)
    {
        decimal cagr = r.StartEquity > 0m ? (r.EndEquity - r.StartEquity) / r.StartEquity : 0m;
        return new SensitivityRow(name, cagr, r.SharpeRatio, r.MaxDrawdown, r.ProfitFactor);
    }

    private static Dictionary<string, object> WithOverride(
        Dictionary<string, object> original, string key, object value)
    {
        var copy = new Dictionary<string, object>(original) { [key] = value };
        return copy;
    }
}
