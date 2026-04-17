namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Persistence interface for study records.
/// Implemented by Infrastructure (JSON files).
/// </summary>
public interface IStudyRepository
{
    /// <summary>Gets a study by ID, or null if not found.</summary>
    Task<StudyRecord?> GetAsync(string studyId, CancellationToken ct = default);

    /// <summary>Lists all studies for a strategy version.</summary>
    Task<IReadOnlyList<StudyRecord>> ListByVersionAsync(string strategyVersionId, CancellationToken ct = default);

    /// <summary>Lists all studies.</summary>
    Task<IReadOnlyList<StudyRecord>> ListAsync(CancellationToken ct = default);

    /// <summary>Saves or updates a study record.</summary>
    Task SaveAsync(StudyRecord study, CancellationToken ct = default);

    /// <summary>Deletes a study record.</summary>
    Task DeleteAsync(string studyId, CancellationToken ct = default);

    /// <summary>Saves the result JSON for a completed study.</summary>
    Task SaveResultAsync(string studyId, string resultJson, CancellationToken ct = default);
}
