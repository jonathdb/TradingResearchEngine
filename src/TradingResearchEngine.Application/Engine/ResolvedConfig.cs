namespace TradingResearchEngine.Application.Engine;

/// <summary>Provenance of a resolved configuration value.</summary>
public enum ConfigProvenance
{
    /// <summary>Value comes from the system default.</summary>
    Default,
    /// <summary>Value comes from an applied preset.</summary>
    Preset,
    /// <summary>Value was explicitly set by the user.</summary>
    Explicit,
    /// <summary>Value was overridden (e.g. ExecutionOptions override).</summary>
    Override
}

/// <summary>A single resolved value annotated with its provenance.</summary>
/// <param name="FieldName">The configuration field name.</param>
/// <param name="Value">The effective value that the engine will use.</param>
/// <param name="Provenance">Where this value originated.</param>
public sealed record ResolvedValue(
    string FieldName,
    object? Value,
    ConfigProvenance Provenance);

/// <summary>
/// The final effective configuration after applying defaults, presets, and overrides.
/// Every field is annotated with its provenance.
/// </summary>
/// <param name="DataValues">Resolved data configuration values.</param>
/// <param name="StrategyValues">Resolved strategy configuration values.</param>
/// <param name="RiskValues">Resolved risk configuration values.</param>
/// <param name="ExecutionValues">Resolved execution configuration values.</param>
/// <param name="ResearchValues">Resolved research configuration values.</param>
public sealed record ResolvedConfig(
    IReadOnlyList<ResolvedValue> DataValues,
    IReadOnlyList<ResolvedValue> StrategyValues,
    IReadOnlyList<ResolvedValue> RiskValues,
    IReadOnlyList<ResolvedValue> ExecutionValues,
    IReadOnlyList<ResolvedValue> ResearchValues);
