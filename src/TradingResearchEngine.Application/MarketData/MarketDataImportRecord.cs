using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.MarketData;

/// <summary>
/// Persistent record of a market data import job. Tracks the full lifecycle
/// from download through normalization to DataFileRecord registration.
/// </summary>
public sealed record MarketDataImportRecord(
    string ImportId,
    string Source,
    string Symbol,
    string Timeframe,
    DateTimeOffset RequestedStart,
    DateTimeOffset RequestedEnd,
    MarketDataImportStatus Status,
    string? OutputFilePath = null,
    string? OutputFileId = null,
    int? DownloadedChunkCount = null,
    int? TotalChunkCount = null,
    string? ErrorDetail = null,
    string CandleBasis = "Bid",
    DateTimeOffset CreatedAt = default,
    DateTimeOffset? CompletedAt = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => ImportId;
}
