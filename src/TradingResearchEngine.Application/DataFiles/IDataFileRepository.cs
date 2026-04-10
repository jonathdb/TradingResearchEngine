namespace TradingResearchEngine.Application.DataFiles;

/// <summary>
/// Persistence interface for data file records.
/// Implemented by Infrastructure (JSON files).
/// </summary>
public interface IDataFileRepository
{
    /// <summary>Gets a data file record by ID, or null if not found.</summary>
    Task<DataFileRecord?> GetAsync(string fileId, CancellationToken ct = default);

    /// <summary>Lists all registered data file records.</summary>
    Task<IReadOnlyList<DataFileRecord>> ListAsync(CancellationToken ct = default);

    /// <summary>Saves or updates a data file record.</summary>
    Task SaveAsync(DataFileRecord record, CancellationToken ct = default);

    /// <summary>Deletes a data file record.</summary>
    Task DeleteAsync(string fileId, CancellationToken ct = default);
}
