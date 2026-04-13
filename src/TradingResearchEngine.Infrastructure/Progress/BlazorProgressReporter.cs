using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Infrastructure.Progress;

/// <summary>
/// Blazor-specific <see cref="IProgressReporter"/> that wraps a callback
/// for pushing progress updates to the UI via InvokeAsync(StateHasChanged).
/// </summary>
public sealed class BlazorProgressReporter : IProgressReporter
{
    private readonly Action<int, int, string> _callback;

    public BlazorProgressReporter(Action<int, int, string> callback)
        => _callback = callback;

    /// <inheritdoc/>
    public void Report(int current, int total, string label)
        => _callback(current, total, label);

    /// <inheritdoc/>
    public void Report(ProgressSnapshot snapshot)
        => _callback(snapshot.Current, snapshot.Total, snapshot.CurrentItemLabel ?? snapshot.Stage);
}
