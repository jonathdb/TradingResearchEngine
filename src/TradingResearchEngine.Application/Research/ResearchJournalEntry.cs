using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Research;

/// <summary>Action type for journal entries.</summary>
public enum JournalAction
{
    /// <summary>Strategy promoted to next development stage.</summary>
    Promoted,
    /// <summary>Strategy rejected and not promoted.</summary>
    Rejected,
    /// <summary>Strategy revised with parameter or logic changes.</summary>
    Revised,
    /// <summary>Free-text note added by the user.</summary>
    Noted
}

/// <summary>
/// An audit trail entry recording a research decision about a strategy.
/// Captures stage transitions, rejections, revisions, and free-text notes.
/// </summary>
public sealed record ResearchJournalEntry(
    string EntryId,
    string StrategyId,
    string? StrategyVersionId,
    DateTimeOffset Timestamp,
    JournalAction Action,
    string Reason,
    DevelopmentStage? FromStage = null,
    DevelopmentStage? ToStage = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => EntryId;
}
