namespace TradingResearchEngine.Core.DataHandling;

/// <summary>
/// Abstraction for market data sources. Implement this interface in Infrastructure
/// to add new data providers without modifying Core or Application.
/// </summary>
public interface IDataProvider
{
    /// <summary>Streams bar records for the given symbol and interval over the specified range.</summary>
    IAsyncEnumerable<BarRecord> GetBars(
        string symbol, string interval,
        DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>Streams tick records for the given symbol over the specified range.</summary>
    IAsyncEnumerable<TickRecord> GetTicks(
        string symbol,
        DateTimeOffset from, DateTimeOffset to,
        CancellationToken ct = default);

    /// <summary>
    /// Returns an estimated bar count for progress reporting, or <c>null</c> if unknown.
    /// Must be lightweight — no full data preloading.
    /// Providers that know their data size (e.g. in-memory, CSV with line count) should override this.
    /// </summary>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>Estimated bar count, or <c>null</c> if the provider cannot estimate without preloading.</returns>
    ValueTask<int?> EstimateBarCountAsync(CancellationToken ct = default)
        => new((int?)null);
}
