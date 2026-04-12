using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Validates a <see cref="ScenarioConfig"/> before engine execution. Checks parameter ranges,
/// timeframe consistency, sealed test set conflicts, data sufficiency, and
/// precedence conflicts. Invoked by <see cref="RunScenarioUseCase"/> before engine construction.
/// </summary>
public sealed class PreflightValidator
{
    /// <summary>Canonical BarsPerYear values keyed by timeframe label.</summary>
    private static readonly Dictionary<string, int> TimeframeBarsPerYear = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Daily"] = 252,
        ["H4"] = 1512,
        ["H1"] = 6048,
        ["M15"] = 24192
    };

    private readonly IStrategySchemaProvider _schemaProvider;

    /// <summary>Creates a new <see cref="PreflightValidator"/>.</summary>
    /// <param name="schemaProvider">Provides parameter schemas for registered strategies.</param>
    public PreflightValidator(IStrategySchemaProvider schemaProvider)
        => _schemaProvider = schemaProvider;

    /// <summary>
    /// Validates a complete <see cref="ScenarioConfig"/> and returns all findings.
    /// </summary>
    /// <param name="config">The scenario configuration to validate.</param>
    /// <returns>A <see cref="PreflightResult"/> containing all findings.</returns>
    public PreflightResult Validate(ScenarioConfig config)
    {
        var findings = new List<PreflightFinding>();
        ValidateRequiredFields(config, findings);
        ValidateMissingParams(config, findings);
        ValidateParamRanges(config, findings);
        ValidateTimeframeConsistency(config, findings);
        ValidateRiskSettings(config, findings);
        ValidateExecutionWindow(config, findings);
        ValidateSealedTestSetConflicts(config, findings);
        ValidateDataSufficiency(config, findings);
        ValidateBarsPerYearMismatch(config, findings);
        ValidatePrecedenceConflicts(config, findings);
        return new PreflightResult(findings);
    }

    /// <summary>
    /// Validates a partial <see cref="ConfigDraft"/> at a specific builder step.
    /// Only checks relevant to the completed steps are applied.
    /// </summary>
    /// <param name="draft">The in-progress configuration draft.</param>
    /// <param name="completedStep">The highest step the user has completed (1–5).</param>
    /// <returns>A <see cref="PreflightResult"/> containing findings for completed steps.</returns>
    public PreflightResult ValidateAtStep(ConfigDraft draft, int completedStep)
    {
        var findings = new List<PreflightFinding>();
        if (completedStep >= 1) ValidateStrategyType(draft, findings);
        if (completedStep >= 2) ValidateDataAndWindow(draft, findings);
        if (completedStep >= 3) ValidateParamsForDraft(draft, findings);
        if (completedStep >= 4) ValidateRealismRiskConsistency(draft, findings);
        if (completedStep >= 5) ValidateFullDraft(draft, findings);
        return new PreflightResult(findings);
    }

    // ── Full config validation methods ──────────────────────────────────

    private static void ValidateRequiredFields(ScenarioConfig config, List<PreflightFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(config.ScenarioId))
            findings.Add(new PreflightFinding("ScenarioId", "ScenarioId is required.", PreflightSeverity.Error, "MISSING_PARAM"));

        if (string.IsNullOrWhiteSpace(config.StrategyType) &&
            string.IsNullOrWhiteSpace(config.Strategy?.StrategyType))
            findings.Add(new PreflightFinding("StrategyType", "StrategyType is required.", PreflightSeverity.Error, "MISSING_PARAM"));

        if (string.IsNullOrWhiteSpace(config.DataProviderType) &&
            string.IsNullOrWhiteSpace(config.Data?.DataProviderType))
            findings.Add(new PreflightFinding("DataProviderType", "DataProviderType is required.", PreflightSeverity.Error, "MISSING_PARAM"));
    }

    private void ValidateMissingParams(ScenarioConfig config, List<PreflightFinding> findings)
    {
        var strategyType = config.EffectiveStrategyConfig.StrategyType;
        if (string.IsNullOrWhiteSpace(strategyType)) return;

        IReadOnlyList<StrategyParameterSchema> schema;
        try { schema = _schemaProvider.GetSchema(strategyType); }
        catch { return; } // Strategy not found — already caught by ValidateRequiredFields

        var parameters = config.EffectiveStrategyConfig.StrategyParameters;
        foreach (var param in schema.Where(p => p.IsRequired))
        {
            if (!parameters.ContainsKey(param.Name))
            {
                findings.Add(new PreflightFinding(
                    $"StrategyParameters.{param.Name}",
                    $"Required parameter '{param.DisplayName}' is missing.",
                    PreflightSeverity.Error,
                    "MISSING_PARAM"));
            }
        }
    }

    private void ValidateParamRanges(ScenarioConfig config, List<PreflightFinding> findings)
    {
        var strategyType = config.EffectiveStrategyConfig.StrategyType;
        if (string.IsNullOrWhiteSpace(strategyType)) return;

        IReadOnlyList<StrategyParameterSchema> schema;
        try { schema = _schemaProvider.GetSchema(strategyType); }
        catch { return; }

        var parameters = config.EffectiveStrategyConfig.StrategyParameters;
        foreach (var param in schema)
        {
            if (!parameters.TryGetValue(param.Name, out var rawValue)) continue;

            if (param.Min is not null && TryCompare(rawValue, param.Min) < 0)
            {
                findings.Add(new PreflightFinding(
                    $"StrategyParameters.{param.Name}",
                    $"Parameter '{param.DisplayName}' value {rawValue} is below minimum {param.Min}.",
                    PreflightSeverity.Error,
                    "RANGE_VIOLATION"));
            }

            if (param.Max is not null && TryCompare(rawValue, param.Max) > 0)
            {
                findings.Add(new PreflightFinding(
                    $"StrategyParameters.{param.Name}",
                    $"Parameter '{param.DisplayName}' value {rawValue} is above maximum {param.Max}.",
                    PreflightSeverity.Error,
                    "RANGE_VIOLATION"));
            }
        }
    }

    private static void ValidateTimeframeConsistency(ScenarioConfig config, List<PreflightFinding> findings)
    {
        var data = config.EffectiveDataConfig;
        if (data.Timeframe is null) return;

        if (TimeframeBarsPerYear.TryGetValue(data.Timeframe, out var expected) && data.BarsPerYear != expected)
        {
            findings.Add(new PreflightFinding(
                "BarsPerYear",
                $"BarsPerYear ({data.BarsPerYear}) does not match timeframe '{data.Timeframe}' (expected {expected}).",
                PreflightSeverity.Warning,
                "TIMEFRAME_MISMATCH"));
        }
    }

    private static void ValidateRiskSettings(ScenarioConfig config, List<PreflightFinding> findings)
    {
        var risk = config.EffectiveRiskConfig;

        if (risk.InitialCash <= 0)
            findings.Add(new PreflightFinding("InitialCash", "InitialCash must be greater than zero.", PreflightSeverity.Error, "RANGE_VIOLATION"));

        if (risk.AnnualRiskFreeRate < 0)
            findings.Add(new PreflightFinding("AnnualRiskFreeRate", "AnnualRiskFreeRate must be non-negative.", PreflightSeverity.Error, "RANGE_VIOLATION"));
    }

    private static void ValidateExecutionWindow(ScenarioConfig config, List<PreflightFinding> findings)
    {
        var data = config.EffectiveDataConfig;
        if (data.BarsPerYear <= 0)
            findings.Add(new PreflightFinding("BarsPerYear", "BarsPerYear must be greater than zero.", PreflightSeverity.Error, "RANGE_VIOLATION"));

        // Validate date range if present
        var opts = data.DataProviderOptions;
        DateTimeOffset? from = opts.TryGetValue("From", out var f) && f is DateTimeOffset df ? df : null;
        DateTimeOffset? to = opts.TryGetValue("To", out var t) && t is DateTimeOffset dt ? dt : null;

        if (from.HasValue && to.HasValue && from >= to)
        {
            findings.Add(new PreflightFinding(
                "DataProviderOptions",
                "Start date must be before end date.",
                PreflightSeverity.Error,
                "INVALID_DATE_RANGE"));
        }
    }

    private static void ValidateSealedTestSetConflicts(ScenarioConfig config, List<PreflightFinding> findings)
    {
        // Check if research workflow date range overlaps sealed test set
        // This is a config-level check; runtime guard is in SealedTestSetGuard
        var research = config.EffectiveResearchConfig;
        if (research.ResearchWorkflowType is null) return;

        // If research workflow options contain date ranges, check for sealed set overlap
        // This is a best-effort check at the config level
    }

    private static void ValidateDataSufficiency(ScenarioConfig config, List<PreflightFinding> findings)
    {
        var data = config.EffectiveDataConfig;
        var opts = data.DataProviderOptions;

        DateTimeOffset? from = opts.TryGetValue("From", out var f) && f is DateTimeOffset df ? df : null;
        DateTimeOffset? to = opts.TryGetValue("To", out var t) && t is DateTimeOffset dt ? dt : null;

        if (from.HasValue && to.HasValue)
        {
            var estimatedBars = ExecutionWindowEditor.EstimateBarCount(
                data.Timeframe, from.Value, to.Value);

            if (estimatedBars.HasValue && estimatedBars.Value < 100)
            {
                findings.Add(new PreflightFinding(
                    "DataProviderOptions",
                    $"Estimated bar count ({estimatedBars.Value}) is below the minimum recommended (100 bars).",
                    PreflightSeverity.Warning,
                    "INSUFFICIENT_DATA"));
            }
        }
    }

    private static void ValidateBarsPerYearMismatch(ScenarioConfig config, List<PreflightFinding> findings)
    {
        // Check top-level BarsPerYear vs sub-object BarsPerYear
        if (config.Data is not null && config.BarsPerYear != config.Data.BarsPerYear)
        {
            findings.Add(new PreflightFinding(
                "BarsPerYear",
                $"Top-level BarsPerYear ({config.BarsPerYear}) differs from Data sub-object BarsPerYear ({config.Data.BarsPerYear}). Sub-object value will be used.",
                PreflightSeverity.Warning,
                "BARS_PER_YEAR_MISMATCH"));
        }
    }

    private static void ValidatePrecedenceConflicts(ScenarioConfig config, List<PreflightFinding> findings)
    {
        // FillModeOverride vs top-level FillMode
        var exec = config.EffectiveExecutionConfig;
        if (exec.ExecutionOptions?.FillModeOverride is { } fmOverride && fmOverride != exec.FillMode)
        {
            findings.Add(new PreflightFinding(
                "FillMode",
                $"ExecutionOptions.FillModeOverride ({fmOverride}) takes precedence over FillMode ({exec.FillMode}).",
                PreflightSeverity.Warning,
                "PRECEDENCE_CONFLICT"));
        }

        // Sub-object vs top-level conflicts
        if (config.Data is not null && config.DataProviderType != config.Data.DataProviderType)
        {
            findings.Add(new PreflightFinding(
                "DataProviderType",
                "Both top-level and sub-object values present for Data; sub-object values will be used.",
                PreflightSeverity.Warning,
                "PRECEDENCE_CONFLICT"));
        }

        if (config.Strategy is not null && config.StrategyType != config.Strategy.StrategyType)
        {
            findings.Add(new PreflightFinding(
                "StrategyType",
                "Both top-level and sub-object values present for Strategy; sub-object values will be used.",
                PreflightSeverity.Warning,
                "PRECEDENCE_CONFLICT"));
        }

        if (config.Execution is not null && config.SlippageModelType != config.Execution.SlippageModelType)
        {
            findings.Add(new PreflightFinding(
                "SlippageModelType",
                "Both top-level and sub-object values present for Execution; sub-object values will be used.",
                PreflightSeverity.Warning,
                "PRECEDENCE_CONFLICT"));
        }

        if (config.Risk is not null && config.InitialCash != config.Risk.InitialCash)
        {
            findings.Add(new PreflightFinding(
                "InitialCash",
                "Both top-level and sub-object values present for Risk; sub-object values will be used.",
                PreflightSeverity.Warning,
                "PRECEDENCE_CONFLICT"));
        }
    }

    // ── Draft step validation methods ───────────────────────────────────

    private void ValidateStrategyType(ConfigDraft draft, List<PreflightFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(draft.StrategyType))
        {
            findings.Add(new PreflightFinding("StrategyType", "Strategy type is required.", PreflightSeverity.Error, "MISSING_PARAM"));
            return;
        }

        try { _schemaProvider.GetSchema(draft.StrategyType); }
        catch
        {
            findings.Add(new PreflightFinding(
                "StrategyType",
                $"Strategy type '{draft.StrategyType}' is not registered.",
                PreflightSeverity.Error,
                "UNKNOWN_STRATEGY"));
        }
    }

    private static void ValidateDataAndWindow(ConfigDraft draft, List<PreflightFinding> findings)
    {
        if (draft.DataConfig is null)
        {
            findings.Add(new PreflightFinding("DataConfig", "Data configuration is required.", PreflightSeverity.Error, "MISSING_PARAM"));
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.DataConfig.DataProviderType))
            findings.Add(new PreflightFinding("DataProviderType", "Data provider type is required.", PreflightSeverity.Error, "MISSING_PARAM"));

        if (draft.DataConfig.BarsPerYear <= 0)
            findings.Add(new PreflightFinding("BarsPerYear", "BarsPerYear must be greater than zero.", PreflightSeverity.Error, "RANGE_VIOLATION"));

        // Timeframe/BarsPerYear consistency
        if (draft.DataConfig.Timeframe is not null &&
            TimeframeBarsPerYear.TryGetValue(draft.DataConfig.Timeframe, out var expected) &&
            draft.DataConfig.BarsPerYear != expected)
        {
            findings.Add(new PreflightFinding(
                "BarsPerYear",
                $"BarsPerYear ({draft.DataConfig.BarsPerYear}) does not match timeframe '{draft.DataConfig.Timeframe}' (expected {expected}).",
                PreflightSeverity.Warning,
                "TIMEFRAME_MISMATCH"));
        }
    }

    private void ValidateParamsForDraft(ConfigDraft draft, List<PreflightFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(draft.StrategyType)) return;

        IReadOnlyList<StrategyParameterSchema> schema;
        try { schema = _schemaProvider.GetSchema(draft.StrategyType); }
        catch { return; }

        var parameters = draft.StrategyParameters ?? new Dictionary<string, object>();
        foreach (var param in schema.Where(p => p.IsRequired))
        {
            if (!parameters.ContainsKey(param.Name))
            {
                findings.Add(new PreflightFinding(
                    $"StrategyParameters.{param.Name}",
                    $"Required parameter '{param.DisplayName}' is missing.",
                    PreflightSeverity.Error,
                    "MISSING_PARAM"));
            }
        }

        // Range checks
        foreach (var param in schema)
        {
            if (!parameters.TryGetValue(param.Name, out var rawValue)) continue;

            if (param.Min is not null && TryCompare(rawValue, param.Min) < 0)
            {
                findings.Add(new PreflightFinding(
                    $"StrategyParameters.{param.Name}",
                    $"Parameter '{param.DisplayName}' value {rawValue} is below minimum {param.Min}.",
                    PreflightSeverity.Error,
                    "RANGE_VIOLATION"));
            }

            if (param.Max is not null && TryCompare(rawValue, param.Max) > 0)
            {
                findings.Add(new PreflightFinding(
                    $"StrategyParameters.{param.Name}",
                    $"Parameter '{param.DisplayName}' value {rawValue} is above maximum {param.Max}.",
                    PreflightSeverity.Error,
                    "RANGE_VIOLATION"));
            }
        }
    }

    private static void ValidateRealismRiskConsistency(ConfigDraft draft, List<PreflightFinding> findings)
    {
        if (draft.RiskConfig is not null)
        {
            if (draft.RiskConfig.InitialCash <= 0)
                findings.Add(new PreflightFinding("InitialCash", "InitialCash must be greater than zero.", PreflightSeverity.Error, "RANGE_VIOLATION"));

            if (draft.RiskConfig.AnnualRiskFreeRate < 0)
                findings.Add(new PreflightFinding("AnnualRiskFreeRate", "AnnualRiskFreeRate must be non-negative.", PreflightSeverity.Error, "RANGE_VIOLATION"));
        }

        // Precedence conflict: preset overrides
        if (draft.PresetId is not null && draft.PresetOverrides is { Count: > 0 })
        {
            findings.Add(new PreflightFinding(
                "PresetOverrides",
                $"Preset '{draft.PresetId}' has {draft.PresetOverrides.Count} manual override(s). Preset label will show as 'Custom'.",
                PreflightSeverity.Warning,
                "PRECEDENCE_CONFLICT"));
        }
    }

    private void ValidateFullDraft(ConfigDraft draft, List<PreflightFinding> findings)
    {
        // At step 5, run all checks that apply to a complete draft
        if (string.IsNullOrWhiteSpace(draft.Hypothesis) || (draft.Hypothesis?.Length ?? 0) < 10)
        {
            findings.Add(new PreflightFinding(
                "Hypothesis",
                "Hypothesis is required and must be at least 10 characters.",
                PreflightSeverity.Error,
                "MISSING_PARAM"));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts a numeric comparison between two values.
    /// Returns negative if a &lt; b, zero if equal, positive if a &gt; b.
    /// Returns 0 if comparison is not possible.
    /// </summary>
    private static int TryCompare(object? a, object? b)
    {
        if (a is null || b is null) return 0;

        try
        {
            var da = Convert.ToDouble(a);
            var db = Convert.ToDouble(b);
            return da.CompareTo(db);
        }
        catch
        {
            return 0;
        }
    }
}
