namespace TradingResearchEngine.Application.TickImport;

/// <summary>Persistence for generated timeframe records.</summary>
public interface IGeneratedTimeframeRepository
{
    /// <summary>Gets a generated timeframe record by ID, or null if not found.</summary>
    Task<GeneratedTimeframeRecord?> GetAsync(string recordId, CancellationToken ct = default);

    /// <summary>Lists all generated timeframe records for a specific tick import.</summary>
    Task<IReadOnlyList<GeneratedTimeframeRecord>> ListByImportAsync(
        string tickImportId, CancellationToken ct = default);

    /// <summary>Saves or updates a generated timeframe record.</summary>
    Task SaveAsync(GeneratedTimeframeRecord record, CancellationToken ct = default);

    /// <summary>Deletes a generated timeframe record.</summary>
    Task DeleteAsync(string recordId, CancellationToken ct = default);
}
