namespace TradingResearchEngine.Application.MarketData;

/// <summary>
/// Persistence interface for market data import records.
/// Implemented by Infrastructure (JSON files).
/// </summary>
public interface IMarketDataImportRepository
{
    /// <summary>Gets an import record by ID, or null if not found.</summary>
    Task<MarketDataImportRecord?> GetAsync(string importId, CancellationToken ct = default);

    /// <summary>Lists all import records.</summary>
    Task<IReadOnlyList<MarketDataImportRecord>> ListAsync(CancellationToken ct = default);

    /// <summary>Saves or updates an import record.</summary>
    Task SaveAsync(MarketDataImportRecord record, CancellationToken ct = default);

    /// <summary>Deletes an import record.</summary>
    Task DeleteAsync(string importId, CancellationToken ct = default);
}
