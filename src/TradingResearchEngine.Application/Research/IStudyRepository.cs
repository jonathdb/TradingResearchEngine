namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Persistence interface for study records.
/// Implemented by Infrastructure (JSON files).
/// V9: Adds paginated listing with optional filters.
/// </summary>
public interface IStudyRepository
{
    /// <summary>Gets a study by ID, or null if not found.</summary>
    Task<StudyRecord?> GetAsync(string studyId, CancellationToken ct = default);

    /// <summary>Lists all studies for a strategy version.</summary>
    Task<IReadOnlyList<StudyRecord>> ListByVersionAsync(string strategyVersionId, CancellationToken ct = default);

    /// <summary>Lists all studies.</summary>
    Task<IReadOnlyList<StudyRecord>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists studies with pagination and optional filters.
    /// Results are ordered by creation date descending (most recent first).
    /// </summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Maximum items per page.</param>
    /// <param name="typeFilter">Optional filter by study type.</param>
    /// <param name="statusFilter">Optional filter by study status.</param>
    /// <param name="strategyVersionId">Optional filter by strategy version.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A paginated result with items and total count metadata.</returns>
    /// <remarks>
    /// Implementation note: JSON file repository uses in-memory filtering.
    /// Safe for up to ~5000 records (&lt;100ms). Beyond that, use SQLite index.
    /// </remarks>
    Task<PagedResult<StudyRecord>> ListPagedAsync(
        int page,
        int pageSize,
        StudyType? typeFilter = null,
        StudyStatus? statusFilter = null,
        string? strategyVersionId = null,
        CancellationToken ct = default);

    /// <summary>Saves or updates a study record.</summary>
    Task SaveAsync(StudyRecord study, CancellationToken ct = default);

    /// <summary>Deletes a study record.</summary>
    Task DeleteAsync(string studyId, CancellationToken ct = default);

    /// <summary>Saves the result JSON for a completed study.</summary>
    Task SaveResultAsync(string studyId, string resultJson, CancellationToken ct = default);
}
