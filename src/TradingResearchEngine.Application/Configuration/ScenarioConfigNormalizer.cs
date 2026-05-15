using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Transforms any <see cref="ScenarioConfig"/> (legacy flat-field or modern sub-object)
/// into the canonical V5+ sub-object shape in memory.
/// <para>
/// This normalizer does NOT modify source files on disk. It is an in-memory transformation
/// applied after deserialization. Canonical shape is persisted only on explicit save operations.
/// </para>
/// <para>
/// All downstream validation and runtime code should consume the normalized config,
/// ensuring a single validation path regardless of the original config format.
/// </para>
/// </summary>
public static class ScenarioConfigNormalizer
{
    /// <summary>
    /// Normalizes a <see cref="ScenarioConfig"/> to the canonical V5+ sub-object shape.
    /// If the config already has all sub-objects populated (canonical form), it is returned as-is.
    /// If flat fields are present but sub-objects are null, the flat fields are transformed
    /// into the corresponding sub-object shape.
    /// </summary>
    /// <param name="raw">The raw config as loaded from disk or constructed by the UI.</param>
    /// <returns>A config with all sub-objects populated in canonical form.</returns>
    public static ScenarioConfig Normalize(ScenarioConfig raw)
    {
        if (IsCanonical(raw))
            return raw;

        return raw with
        {
            Data = raw.Data ?? BuildDataConfig(raw),
            Strategy = raw.Strategy ?? BuildStrategyConfig(raw),
            Risk = raw.Risk ?? BuildRiskConfig(raw),
            Execution = raw.Execution ?? BuildExecutionConfig(raw),
            Research = raw.Research ?? BuildResearchConfig(raw)
        };
    }

    /// <summary>
    /// Determines whether the config is already in canonical V5+ form
    /// (all sub-objects are populated).
    /// </summary>
    /// <param name="config">The config to check.</param>
    /// <returns><c>true</c> if all sub-objects are non-null; otherwise <c>false</c>.</returns>
    public static bool IsCanonical(ScenarioConfig config) =>
        config.Data is not null
        && config.Strategy is not null
        && config.Risk is not null
        && config.Execution is not null
        && config.Research is not null;

    private static DataConfig BuildDataConfig(ScenarioConfig raw) =>
        new(raw.DataProviderType, raw.DataProviderOptions, raw.Timeframe, raw.BarsPerYear);

    private static StrategyConfig BuildStrategyConfig(ScenarioConfig raw) =>
        new(raw.StrategyType, raw.StrategyParameters);

    private static RiskConfig BuildRiskConfig(ScenarioConfig raw) =>
        new(raw.RiskParameters, raw.InitialCash, raw.AnnualRiskFreeRate);

    private static ExecutionConfig BuildExecutionConfig(ScenarioConfig raw) =>
        new(raw.SlippageModelType, raw.CommissionModelType, raw.FillMode,
            raw.RealismProfile, raw.ExecutionOptions, raw.SessionOptions);

    private static ResearchConfig BuildResearchConfig(ScenarioConfig raw) =>
        new(raw.ResearchWorkflowType, raw.ResearchWorkflowOptions, raw.RandomSeed, raw.TraceOptions);
}
