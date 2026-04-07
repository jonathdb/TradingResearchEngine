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
}
