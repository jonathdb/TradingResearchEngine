using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.TickImport;

/// <summary>
/// Persistent record of a tick data import job. Tracks the full lifecycle
/// from download initiation through completion.
/// </summary>
public sealed record TickImportRecord(
    string ImportId,
    string Source,
    string Symbol,
    DateTimeOffset RequestedStart,
    DateTimeOffset RequestedEnd,
    TickImportStatus Status,
    long? TotalTickCount = null,
    string? ErrorDetail = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset? CompletedAt = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => ImportId;
}
