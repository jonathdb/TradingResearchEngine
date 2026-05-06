namespace TradingResearchEngine.Application.TickImport;

/// <summary>
/// Abstraction for downloading tick data from an external provider.
/// Implementations live in Infrastructure.
/// </summary>
public interface ITickDownloader
{
    /// <summary>
    /// Downloads tick data for all hours in the given trading days.
    /// Yields results as they complete.
    /// </summary>
    IAsyncEnumerable<TickDownloadItem> DownloadAsync(
        string symbol,
        IReadOnlyList<DateTime> tradingDays,
        IProgress<(int current, int total)>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// The set of symbols supported by this downloader.
    /// Used for validation before starting an import.
    /// </summary>
    IReadOnlySet<string> SupportedSymbols { get; }
}

/// <summary>Result of downloading a single hour's tick data.</summary>
public sealed record TickDownloadItem(
    DateTime Date, int Hour, IReadOnlyList<TickCsvRow> Ticks);
