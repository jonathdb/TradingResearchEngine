using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.TickImport;

/// <summary>
/// Links a generated bar CSV file to its source tick import and timeframe.
/// </summary>
public sealed record GeneratedTimeframeRecord(
    string RecordId,
    string TickImportId,
    string Timeframe,
    string OutputFilePath,
    string OutputFileId,
    int BarCount,
    DateTimeOffset FirstBar,
    DateTimeOffset LastBar,
    DateTimeOffset GeneratedAt) : IHasId
{
    /// <inheritdoc/>
    public string Id => RecordId;
}
