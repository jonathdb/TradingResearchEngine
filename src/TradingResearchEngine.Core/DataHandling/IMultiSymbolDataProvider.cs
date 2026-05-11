namespace TradingResearchEngine.Core.DataHandling;

/// <summary>
/// Provides synchronised bar data across multiple symbols.
/// This is the foundation interface for multi-symbol backtesting support.
/// Implementations will align bars by timestamp across all requested symbols,
/// emitting a <see cref="MultiSymbolBarEvent"/> for each timestamp where at least one symbol has data.
/// </summary>
/// <remarks>
/// This is an interface-only definition for the current iteration.
/// No concrete implementation is required until multi-symbol backtesting is fully supported.
/// The existing <see cref="IDataProvider"/> remains unchanged and continues to serve single-symbol workflows.
/// </remarks>
public interface IMultiSymbolDataProvider
{
    /// <summary>
    /// Streams synchronised bar events for the specified symbols over the given time range.
    /// Each event contains bars for all symbols that have data at that timestamp.
    /// </summary>
    /// <param name="symbols">The list of symbols to retrieve data for.</param>
    /// <param name="interval">The bar interval (e.g., "1D", "1H", "5m").</param>
    /// <param name="from">The start of the date range (inclusive).</param>
    /// <param name="to">The end of the date range (inclusive).</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>An async enumerable of synchronised multi-symbol bar events.</returns>
    IAsyncEnumerable<MultiSymbolBarEvent> GetSynchronizedBarsAsync(
        IReadOnlyList<string> symbols,
        string interval,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
}

/// <summary>
/// A single timestamp's worth of bar data across multiple symbols.
/// Contains bars for all symbols that have data at the given timestamp.
/// Symbols without data at this timestamp are absent from the <see cref="Bars"/> dictionary.
/// </summary>
/// <param name="Timestamp">The common timestamp for all bars in this event.</param>
/// <param name="Bars">A dictionary mapping symbol names to their bar data at this timestamp.</param>
public sealed record MultiSymbolBarEvent(
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, BarRecord> Bars);
