namespace TradingResearchEngine.Application.PropFirm;

/// <summary>
/// Persistence interface for prop-firm evaluation records.
/// Wires checklist item 8 in ResearchChecklistService.
/// </summary>
public interface IPropFirmEvaluationRepository
{
    /// <summary>
    /// Returns true if at least one completed evaluation exists for the given strategy version.
    /// </summary>
    Task<bool> HasCompletedEvaluationAsync(string strategyVersionId, CancellationToken ct = default);

    /// <summary>
    /// Persists a prop-firm evaluation record for the given strategy version.
    /// </summary>
    Task SaveEvaluationAsync(string strategyVersionId, PropFirmEvaluationRecord record, CancellationToken ct = default);
}
