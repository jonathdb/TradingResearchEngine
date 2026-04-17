using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.PropFirm;

/// <summary>
/// Persisted record of a prop-firm evaluation for a strategy version.
/// Tracks whether a strategy has been evaluated against firm rules.
/// </summary>
public sealed record PropFirmEvaluationRecord(
    /// <summary>Unique identifier for this evaluation.</summary>
    string EvaluationId,
    /// <summary>The strategy version that was evaluated.</summary>
    string StrategyVersionId,
    /// <summary>Name of the prop firm whose rules were applied.</summary>
    string FirmName,
    /// <summary>Name of the challenge phase evaluated.</summary>
    string PhaseName,
    /// <summary>Whether the strategy passed the evaluation.</summary>
    bool Passed,
    /// <summary>When the evaluation was performed.</summary>
    DateTimeOffset EvaluatedAt) : IHasId
{
    /// <inheritdoc/>
    public string Id => EvaluationId;
}
