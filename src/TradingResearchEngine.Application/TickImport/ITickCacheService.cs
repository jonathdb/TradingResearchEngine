namespace TradingResearchEngine.Application.TickImport;

/// <summary>
/// Manages the per-day tick CSV cache. Provides coverage queries,
/// read access for timeframe generation, and write access for the downloader.
/// </summary>
public interface ITickCacheService
{
    /// <summary>Returns trading days in the range that are NOT yet cached for the symbol.</summary>
    Task<IReadOnlyList<DateTime>> GetMissingDaysAsync(
        string symbol, DateTime startDate, DateTime endDate, CancellationToken ct = default);

    /// <summary>Returns the date range of existing tick coverage for a symbol, or null if none.</summary>
    Task<(DateTime Earliest, DateTime Latest)?> GetCoverageAsync(
        string symbol, CancellationToken ct = default);

    /// <summary>Writes tick rows for a single day. Overwrites if file exists.</summary>
    Task WriteDayTicksAsync(
        string symbol, DateTime date, IReadOnlyList<TickCsvRow> ticks, CancellationToken ct = default);

    /// <summary>Streams all tick rows for the symbol across the given date range.</summary>
    IAsyncEnumerable<TickCsvRow> ReadTicksAsync(
        string symbol, DateTime startDate, DateTime endDate, CancellationToken ct = default);

    /// <summary>Returns the total tick count across all cached days for a symbol in the range.</summary>
    Task<long> GetTickCountAsync(
        string symbol, DateTime startDate, DateTime endDate, CancellationToken ct = default);
}
