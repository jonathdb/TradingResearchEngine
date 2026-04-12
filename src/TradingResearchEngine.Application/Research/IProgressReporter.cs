namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Reports progress for long-running operations (backtests, studies).
/// Blazor implementation uses <c>InvokeAsync(StateHasChanged)</c> to push UI updates.
/// The existing <see cref="Report(int, int, string)"/> overload is preserved for backward
/// compatibility; the <see cref="Report(ProgressSnapshot)"/> overload provides structured
/// progress with stage, elapsed time, and warnings.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reports progress of a long-running operation.
    /// </summary>
    /// <param name="current">Current step (e.g. bar 142, path 347).</param>
    /// <param name="total">Total steps (e.g. 847 bars, 1000 paths). Zero if indeterminate.</param>
    /// <param name="label">Human-readable label (e.g. "Simulating path 347 of 1000").</param>
    void Report(int current, int total, string label);

    /// <summary>
    /// Reports progress using a structured <see cref="ProgressSnapshot"/>.
    /// </summary>
    /// <param name="snapshot">The progress snapshot containing current/total, stage, elapsed time, and warnings.</param>
    void Report(ProgressSnapshot snapshot);
}
