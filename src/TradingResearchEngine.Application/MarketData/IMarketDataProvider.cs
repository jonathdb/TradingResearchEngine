using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Application.MarketData;

/// <summary>
/// Provider-agnostic interface for downloading market data from an external source.
/// Implementations live in Infrastructure. Each provider normalizes output to the
/// canonical CSV schema (Timestamp,Open,High,Low,Close,Volume) before writing.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>Human-readable source name (e.g. "Dukascopy").</summary>
    string SourceName { get; }

    /// <summary>Returns the list of symbols this provider supports.</summary>
    Task<IReadOnlyList<MarketSymbolInfo>> GetSupportedSymbolsAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Downloads data for the given parameters and writes a canonical CSV to <paramref name="outputPath"/>.
    /// <paramref name="requestedStart"/> is inclusive, <paramref name="requestedEnd"/> is exclusive.
    /// Returns metadata derived from the written output stream.
    /// </summary>
    Task<CsvWriteResult> DownloadToFileAsync(
        string symbol,
        string timeframe,
        DateTimeOffset requestedStart,
        DateTimeOffset requestedEnd,
        string outputPath,
        IProgressReporter? progress = null,
        CancellationToken ct = default);
}
