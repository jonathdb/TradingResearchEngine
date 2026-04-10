using System.Collections.Concurrent;

namespace TradingResearchEngine.Application.Research;

/// <summary>Progress update from a running study.</summary>
public sealed record StudyProgressUpdate(
    string StudyId, int Current, int Total, string Label);

/// <summary>Completion notification from a finished study.</summary>
public sealed record StudyCompletionUpdate(
    string StudyId, StudyStatus Status, string? ErrorMessage);

/// <summary>Snapshot of an active study.</summary>
public sealed record ActiveStudy(
    string StudyId, string StrategyVersionId, StudyType Type,
    int Current, int Total, DateTimeOffset StartedAt);

/// <summary>
/// Manages background execution of long-running studies. Singleton service.
/// The concrete implementation must be registered in the Web host because it
/// manages Task.Run lifetime and must create its own DI scope per study execution.
/// This Application-layer class provides the abstraction and event contracts.
/// </summary>
public class BackgroundStudyService : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeCts = new();
    private readonly ConcurrentDictionary<string, ActiveStudy> _activeStudies = new();

    /// <summary>Raised on each progress step of a running study.</summary>
    public event Action<StudyProgressUpdate>? OnProgress;

    /// <summary>Raised when a study completes (success, failure, or cancellation).</summary>
    public event Action<StudyCompletionUpdate>? OnCompleted;

    /// <summary>
    /// Registers a study as active and returns a <see cref="CancellationToken"/> for it.
    /// The caller (typically the Web host) is responsible for actually running the study
    /// on a background task and calling <see cref="ReportProgress"/> / <see cref="Complete"/>.
    /// </summary>
    public CancellationToken RegisterStudy(
        string studyId, string strategyVersionId, StudyType type, int totalCount)
    {
        var cts = new CancellationTokenSource();
        _activeCts[studyId] = cts;
        _activeStudies[studyId] = new ActiveStudy(
            studyId, strategyVersionId, type, 0, totalCount, DateTimeOffset.UtcNow);
        return cts.Token;
    }

    /// <summary>Reports progress for an active study.</summary>
    public void ReportProgress(string studyId, int current, int total, string label)
    {
        if (_activeStudies.TryGetValue(studyId, out var active))
            _activeStudies[studyId] = active with { Current = current, Total = total };

        OnProgress?.Invoke(new StudyProgressUpdate(studyId, current, total, label));
    }

    /// <summary>Marks a study as complete and removes it from active tracking.</summary>
    public void Complete(string studyId, StudyStatus status, string? errorMessage = null)
    {
        _activeStudies.TryRemove(studyId, out _);
        if (_activeCts.TryRemove(studyId, out var cts))
            cts.Dispose();

        OnCompleted?.Invoke(new StudyCompletionUpdate(studyId, status, errorMessage));
    }

    /// <summary>Cancels a running study.</summary>
    public void CancelStudy(string studyId)
    {
        if (_activeCts.TryGetValue(studyId, out var cts))
            cts.Cancel();
    }

    /// <summary>Returns a snapshot of all currently active studies.</summary>
    public IReadOnlyList<ActiveStudy> GetActiveStudies() =>
        _activeStudies.Values.ToList();

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var cts in _activeCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _activeCts.Clear();
        _activeStudies.Clear();
        GC.SuppressFinalize(this);
    }
}
