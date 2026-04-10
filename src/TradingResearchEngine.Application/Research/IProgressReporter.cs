namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Reports progress for long-running operations (backtests, studies).
/// Blazor implementation uses <c>InvokeAsync(StateHasChanged)</c> to push UI updates.
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
}
