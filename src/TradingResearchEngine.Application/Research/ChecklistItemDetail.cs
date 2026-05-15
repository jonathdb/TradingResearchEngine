namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Detailed information about a single research checklist item, including
/// its completion status, navigation path, confidence explanation, and criticality.
/// </summary>
/// <param name="Key">The unique identifier for this checklist item (e.g., "InitialBacktest").</param>
/// <param name="Label">Human-readable label for the checklist item.</param>
/// <param name="IsComplete">Whether this checklist item has been completed.</param>
/// <param name="IsCritical">Whether this item is a gating requirement for final validation.</param>
/// <param name="NavigationPath">The URL/route to the relevant workflow page that addresses this item.</param>
/// <param name="ConfidenceExplanation">Human-readable explanation of why confidence is low when this item is incomplete.</param>
public sealed record ChecklistItemDetail(
    string Key,
    string Label,
    bool IsComplete,
    bool IsCritical,
    string NavigationPath,
    string ConfidenceExplanation);

/// <summary>
/// Result of a checklist readiness check for final validation gating.
/// </summary>
/// <param name="IsReady">Whether all critical checklist items are complete.</param>
/// <param name="Warnings">Warnings to display when critical items are incomplete.</param>
/// <param name="IncompleteItems">The list of incomplete checklist items with navigation guidance.</param>
public sealed record ChecklistReadinessResult(
    bool IsReady,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ChecklistItemDetail> IncompleteItems);
