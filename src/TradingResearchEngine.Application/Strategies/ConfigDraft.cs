using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// An in-progress strategy configuration being assembled in the builder.
/// Persisted via <c>IRepository&lt;ConfigDraft&gt;</c> on every step transition.
/// Promoted to a <see cref="StrategyVersion"/> on save.
/// </summary>
/// <param name="DraftId">Unique identifier for this draft.</param>
/// <param name="CurrentStep">The builder step the user has completed (1–5).</param>
/// <param name="MaxVisitedStep">The highest step the user has visited. Navigation beyond this is prevented.</param>
/// <param name="StrategyName">User-provided strategy name. Null until entered.</param>
/// <param name="StrategyType">Strategy registry key. Null until selected.</param>
/// <param name="TemplateId">Template ID when <see cref="SourceType"/> is Template.</param>
/// <param name="SourceType">How this draft was initiated.</param>
/// <param name="Hypothesis">User's hypothesis for the expected market edge.</param>
/// <param name="ExpectedFailureMode">How the strategy is most likely to fail.</param>
/// <param name="DataConfig">Data provider settings. Null until Step 2.</param>
/// <param name="StrategyParameters">Strategy-specific parameter values. Null until Step 3.</param>
/// <param name="ExecutionConfig">Execution realism settings. Null until Step 4.</param>
/// <param name="RiskConfig">Risk parameter settings. Null until Step 4.</param>
/// <param name="PresetId">Applied preset ID, if any.</param>
/// <param name="PresetOverrides">Fields the user overrode after applying a preset.</param>
/// <param name="CreatedAt">When this draft was first created.</param>
/// <param name="UpdatedAt">When this draft was last modified.</param>
/// <param name="StrategyId">Strategy identity when editing an existing strategy. Null for new strategies.</param>
/// <param name="StrategyVersionId">Strategy version when editing an existing version. Null for new strategies.</param>
/// <param name="SessionGuid">Transient session GUID for new strategy drafts. Used as draft key when StrategyId is null.</param>
public sealed record ConfigDraft(
    string DraftId,
    int CurrentStep,
    /// <summary>The highest step the user has visited. Navigation beyond this is prevented.</summary>
    int MaxVisitedStep,
    string? StrategyName,
    string? StrategyType,
    string? TemplateId,
    SourceType SourceType,
    string? Hypothesis,
    string? ExpectedFailureMode,
    DataConfig? DataConfig,
    Dictionary<string, object>? StrategyParameters,
    ExecutionConfig? ExecutionConfig,
    RiskConfig? RiskConfig,
    string? PresetId,
    Dictionary<string, object>? PresetOverrides,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? StrategyId = null,
    string? StrategyVersionId = null,
    string? SessionGuid = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => DraftId;

    /// <summary>
    /// Computed draft key: (StrategyId:StrategyVersionId) when editing an existing strategy version,
    /// or the SessionGuid (falling back to DraftId) when creating a new strategy.
    /// </summary>
    public string DraftKey => StrategyId is not null && StrategyVersionId is not null
        ? $"{StrategyId}:{StrategyVersionId}"
        : SessionGuid ?? DraftId;
}
