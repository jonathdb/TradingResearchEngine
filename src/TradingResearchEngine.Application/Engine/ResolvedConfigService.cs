using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Resolves a <see cref="ScenarioConfig"/> with optional preset into a fully annotated
/// <see cref="ResolvedConfig"/>. Each value is annotated with its provenance:
/// Default → Preset → Explicit → Override.
/// </summary>
public sealed class ResolvedConfigService
{
    private readonly IRepository<ConfigPreset> _presetRepo;

    /// <summary>Creates a new <see cref="ResolvedConfigService"/>.</summary>
    /// <param name="presetRepo">Repository for loading config presets.</param>
    public ResolvedConfigService(IRepository<ConfigPreset> presetRepo)
        => _presetRepo = presetRepo;

    /// <summary>
    /// Resolves the effective configuration values with provenance annotations.
    /// Precedence: Default → Preset → Explicit → Override.
    /// </summary>
    /// <param name="config">The scenario configuration.</param>
    /// <param name="presetId">Optional preset ID to apply before explicit values.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="ResolvedConfig"/> with all values annotated by provenance.</returns>
    public async Task<ResolvedConfig> ResolveAsync(
        ScenarioConfig config,
        string? presetId = null,
        CancellationToken ct = default)
    {
        ConfigPreset? preset = null;
        if (presetId is not null)
        {
            preset = await _presetRepo.GetByIdAsync(presetId, ct);
            // Fall back to built-in presets if not found in repository
            preset ??= DefaultConfigPresets.All.FirstOrDefault(p => p.PresetId == presetId);
        }

        var dataValues = ResolveDataValues(config, preset);
        var strategyValues = ResolveStrategyValues(config);
        var riskValues = ResolveRiskValues(config, preset);
        var executionValues = ResolveExecutionValues(config, preset);
        var researchValues = ResolveResearchValues(config);

        return new ResolvedConfig(dataValues, strategyValues, riskValues, executionValues, researchValues);
    }

    private static List<ResolvedValue> ResolveDataValues(ScenarioConfig config, ConfigPreset? preset)
    {
        var effective = config.EffectiveDataConfig;
        var values = new List<ResolvedValue>();

        var dpProvenance = config.Data is not null ? ConfigProvenance.Explicit : ConfigProvenance.Default;
        values.Add(new ResolvedValue("DataProviderType", effective.DataProviderType, dpProvenance));
        values.Add(new ResolvedValue("Timeframe", effective.Timeframe, dpProvenance));
        values.Add(new ResolvedValue("BarsPerYear", effective.BarsPerYear, dpProvenance));

        return values;
    }

    private static List<ResolvedValue> ResolveStrategyValues(ScenarioConfig config)
    {
        var effective = config.EffectiveStrategyConfig;
        var values = new List<ResolvedValue>();

        var provenance = config.Strategy is not null ? ConfigProvenance.Explicit : ConfigProvenance.Default;
        values.Add(new ResolvedValue("StrategyType", effective.StrategyType, provenance));

        foreach (var kvp in effective.StrategyParameters)
        {
            values.Add(new ResolvedValue($"StrategyParameters.{kvp.Key}", kvp.Value, ConfigProvenance.Explicit));
        }

        return values;
    }

    private static List<ResolvedValue> ResolveRiskValues(ScenarioConfig config, ConfigPreset? preset)
    {
        var effective = config.EffectiveRiskConfig;
        var values = new List<ResolvedValue>();

        // Determine provenance for each risk field
        var cashProvenance = DetermineRiskProvenance(config, preset, "InitialCash");
        var rfrProvenance = DetermineRiskProvenance(config, preset, "AnnualRiskFreeRate");

        values.Add(new ResolvedValue("InitialCash", effective.InitialCash, cashProvenance));
        values.Add(new ResolvedValue("AnnualRiskFreeRate", effective.AnnualRiskFreeRate, rfrProvenance));

        return values;
    }

    private static List<ResolvedValue> ResolveExecutionValues(ScenarioConfig config, ConfigPreset? preset)
    {
        var effective = config.EffectiveExecutionConfig;
        var values = new List<ResolvedValue>();

        values.Add(new ResolvedValue("SlippageModelType", effective.SlippageModelType,
            DetermineExecutionProvenance(config, preset, "SlippageModelType")));
        values.Add(new ResolvedValue("CommissionModelType", effective.CommissionModelType,
            DetermineExecutionProvenance(config, preset, "CommissionModelType")));
        values.Add(new ResolvedValue("FillMode", effective.FillMode,
            DetermineExecutionProvenance(config, preset, "FillMode")));
        values.Add(new ResolvedValue("RealismProfile", effective.RealismProfile,
            DetermineExecutionProvenance(config, preset, "RealismProfile")));

        // FillMode override takes highest precedence
        if (effective.ExecutionOptions?.FillModeOverride is { } fmOverride)
        {
            values.Add(new ResolvedValue("EffectiveFillMode", fmOverride, ConfigProvenance.Override));
        }

        return values;
    }

    private static List<ResolvedValue> ResolveResearchValues(ScenarioConfig config)
    {
        var effective = config.EffectiveResearchConfig;
        var values = new List<ResolvedValue>();

        var provenance = config.Research is not null ? ConfigProvenance.Explicit : ConfigProvenance.Default;
        values.Add(new ResolvedValue("ResearchWorkflowType", effective.ResearchWorkflowType, provenance));
        values.Add(new ResolvedValue("RandomSeed", effective.RandomSeed, provenance));

        return values;
    }

    private static ConfigProvenance DetermineRiskProvenance(
        ScenarioConfig config, ConfigPreset? preset, string fieldName)
    {
        // Explicit sub-object wins
        if (config.Risk is not null) return ConfigProvenance.Explicit;
        // Preset risk overrides
        if (preset?.RiskConfig is not null) return ConfigProvenance.Preset;
        // Top-level values are treated as explicit if they differ from defaults
        return ConfigProvenance.Default;
    }

    private static ConfigProvenance DetermineExecutionProvenance(
        ScenarioConfig config, ConfigPreset? preset, string fieldName)
    {
        // Explicit sub-object wins
        if (config.Execution is not null) return ConfigProvenance.Explicit;
        // Preset execution config
        if (preset is not null) return ConfigProvenance.Preset;
        // Top-level values
        return ConfigProvenance.Default;
    }
}
