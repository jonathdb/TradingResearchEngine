namespace TradingResearchEngine.Application.TickImport;

/// <summary>Persistence for tick import records.</summary>
public interface ITickImportRepository
{
    /// <summary>Gets a tick import record by ID, or null if not found.</summary>
    Task<TickImportRecord?> GetAsync(string importId, CancellationToken ct = default);

    /// <summary>Lists all tick import records.</summary>
    Task<IReadOnlyList<TickImportRecord>> ListAsync(CancellationToken ct = default);

    /// <summary>Saves or updates a tick import record.</summary>
    Task SaveAsync(TickImportRecord record, CancellationToken ct = default);

    /// <summary>Deletes a tick import record.</summary>
    Task DeleteAsync(string importId, CancellationToken ct = default);
}
