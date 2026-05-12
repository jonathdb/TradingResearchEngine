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
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Estimated time remaining based on linear extrapolation.
    /// Null when <see cref="Current"/> is 0 or <see cref="Total"/> is 0 (indeterminate progress).
    /// Formula: (ElapsedTime / Current) * (Total - Current).
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining => Current > 0 && Total > 0
        ? TimeSpan.FromTicks((long)(ElapsedTime.Ticks / (double)Current * (Total - Current)))
        : null;

    /// <summary>Number of warnings accumulated so far.</summary>
    public int WarningCount => Warnings.Count;
}
