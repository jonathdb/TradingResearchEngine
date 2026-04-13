namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Snapshot of execution progress for a running job or workflow iteration.
/// </summary>
/// <param name="Current">Current step (e.g. bar 142, path 347).</param>
/// <param name="Total">Total steps (e.g. 847 bars, 1000 paths). Zero if indeterminate.</param>
/// <param name="Percentage">Completion percentage (0–100).</param>
/// <param name="Stage">Human-readable stage label (e.g. "Simulating", "Optimizing", "Evaluating").</param>
/// <param name="CurrentItemLabel">Optional label for the current item being processed.</param>
/// <param name="ElapsedTime">Wall-clock time elapsed since the job started.</param>
/// <param name="Warnings">Warnings accumulated during execution so far.</param>
public sealed record ProgressSnapshot(
    int Current,
    int Total,
    decimal Percentage,
    string Stage,
    string? CurrentItemLabel,
    TimeSpan ElapsedTime,
    IReadOnlyList<string> Warnings);
