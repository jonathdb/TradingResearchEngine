using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.PropFirm.Results;

namespace TradingResearchEngine.Application.PropFirm;

/// <summary>
/// Applies Conservative, Base, Strong presets (and optional user-defined) to prop-firm economics.
/// </summary>
public sealed class PropFirmVarianceWorkflow
{
    private readonly PropFirmEvaluator _evaluator;
    private readonly ILogger<PropFirmVarianceWorkflow> _logger;

    /// <inheritdoc cref="PropFirmVarianceWorkflow"/>
    public PropFirmVarianceWorkflow(PropFirmEvaluator evaluator, ILogger<PropFirmVarianceWorkflow> logger)
    {
        _evaluator = evaluator;
        _logger = logger;
    }

    /// <summary>
    /// Runs variance analysis across presets for an instant-funding configuration.
    /// </summary>
    public PropFirmVarianceResult Run(InstantFundingConfig baseConfig, Dictionary<string, object>? userPreset = null)
    {
        var presets = new List<(string Name, decimal GrossReturn, decimal Friction, decimal PassRate)>
        {
            ("Conservative", baseConfig.GrossMonthlyReturnPercent * 0.7m, baseConfig.PayoutFrictionFactor * 0.85m, baseConfig.DirectFundedProbabilityPercent * 0.8m),
            ("Base", baseConfig.GrossMonthlyReturnPercent, baseConfig.PayoutFrictionFactor, baseConfig.DirectFundedProbabilityPercent),
            ("Strong", baseConfig.GrossMonthlyReturnPercent * 1.3m, baseConfig.PayoutFrictionFactor * 1.1m, baseConfig.DirectFundedProbabilityPercent * 1.15m),
        };

        if (userPreset is not null)
        {
            var gross = userPreset.TryGetValue("GrossMonthlyReturnPercent", out var g) && g is decimal gd ? gd : baseConfig.GrossMonthlyReturnPercent;
            var friction = userPreset.TryGetValue("PayoutFrictionFactor", out var f) && f is decimal fd ? fd : baseConfig.PayoutFrictionFactor;
            var pass = userPreset.TryGetValue("PassRatePercent", out var p) && p is decimal pd ? pd : baseConfig.DirectFundedProbabilityPercent;
            presets.Add(("UserDefined", gross, friction, pass));
        }

        var variants = new List<PropFirmScenarioResult>();
        foreach (var (name, grossReturn, friction, passRate) in presets)
        {
            var adjusted = baseConfig with
            {
                GrossMonthlyReturnPercent = grossReturn,
                PayoutFrictionFactor = friction,
                DirectFundedProbabilityPercent = passRate
            };
            variants.Add(_evaluator.ComputeEconomics(adjusted, name));
        }

        return new PropFirmVarianceResult(variants);
    }
}
