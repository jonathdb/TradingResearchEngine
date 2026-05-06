namespace TradingResearchEngine.Core.DataHandling;

/// <summary>
/// Extends <see cref="IDataProvider"/> with real-time streaming capability.
/// Implementations emit bars as they become available, either from a live
/// data source or via simulated playback of historical data.
/// </summary>
public interface IStreamingDataProvider : IDataProvider
{
    /// <summary>
    /// Streams bars as they become available (real-time or simulated playback).
    /// </summary>
    /// <param name="symbol">The instrument symbol to stream.</param>
    /// <param name="interval">The bar interval (e.g. "1h", "15m", "1d").</param>
    /// <param name="ct">Cancellation token to terminate the stream.</param>
    /// <returns>An async enumerable of bar records as they arrive.</returns>
    IAsyncEnumerable<BarRecord> StreamAsync(
        string symbol, string interval, CancellationToken ct);
}
