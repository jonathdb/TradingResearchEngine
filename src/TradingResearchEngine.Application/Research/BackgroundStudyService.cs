using System.Collections.Concurrent;
using TradingResearchEngine.Core.Engine;

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
/// <para>
/// <b>Important:</b> This is NOT a background worker (not an <see cref="Microsoft.Extensions.Hosting.IHostedService"/>
/// or <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>). It is an event/progress coordination service
/// that tracks active studies, emits progress events, and manages cancellation tokens.
/// Actual job execution is handled by <see cref="JobWorkerService"/>.
/// </para>
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

    /// <summary>
    /// Creates an <see cref="IProgress{ProgressUpdate}"/> adapter that routes progress
    /// reports to this service's <see cref="OnProgress"/> event for the specified study.
    /// </summary>
    /// <param name="studyId">The study identifier to associate progress with.</param>
    /// <returns>An <see cref="IProgress{ProgressUpdate}"/> instance that emits progress events.</returns>
    public IProgress<ProgressUpdate> CreateProgressReporter(string studyId)
    {
        return new StudyProgressAdapter(this, studyId);
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

    /// <summary>
    /// Adapter that bridges <see cref="IProgress{ProgressUpdate}"/> to
    /// <see cref="BackgroundStudyService.ReportProgress"/>.
    /// Thread-safe: invoked from parallel workflow iterations.
    /// </summary>
    private sealed class StudyProgressAdapter : IProgress<ProgressUpdate>
    {
        private readonly BackgroundStudyService _service;
        private readonly string _studyId;

        public StudyProgressAdapter(BackgroundStudyService service, string studyId)
        {
            _service = service;
            _studyId = studyId;
        }

        public void Report(ProgressUpdate value)
        {
            _service.ReportProgress(_studyId, value.CurrentStep, value.TotalSteps, value.Message);
        }
    }
}
