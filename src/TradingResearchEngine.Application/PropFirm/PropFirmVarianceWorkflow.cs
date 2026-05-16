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
    /// <param name="baseConfig">The base instant-funding configuration to vary.</param>
    /// <param name="userPreset">Optional typed overrides for a user-defined scenario.</param>
    public PropFirmVarianceResult Run(InstantFundingConfig baseConfig, PropFirmPresetOverrides? userPreset = null)
    {
        var presets = new List<(string Name, decimal GrossReturn, decimal Friction, decimal PassRate)>
        {
            ("Conservative", baseConfig.GrossMonthlyReturnPercent * 0.7m, baseConfig.PayoutFrictionFactor * 0.85m, baseConfig.DirectFundedProbabilityPercent * 0.8m),
            ("Base", baseConfig.GrossMonthlyReturnPercent, baseConfig.PayoutFrictionFactor, baseConfig.DirectFundedProbabilityPercent),
            ("Strong", baseConfig.GrossMonthlyReturnPercent * 1.3m, baseConfig.PayoutFrictionFactor * 1.1m, baseConfig.DirectFundedProbabilityPercent * 1.15m),
        };

        if (userPreset is not null)
        {
            var gross = userPreset.GrossMonthlyReturnPercent ?? baseConfig.GrossMonthlyReturnPercent;
            var friction = userPreset.PayoutFrictionFactor ?? baseConfig.PayoutFrictionFactor;
            var pass = userPreset.PassRatePercent ?? baseConfig.DirectFundedProbabilityPercent;
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
