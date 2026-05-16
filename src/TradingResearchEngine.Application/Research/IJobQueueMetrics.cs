namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Provides observable depth metrics for the background job queue.
/// Sourced from <see cref="JobExecutor"/> progress cache and repository queries.
/// </summary>
public interface IJobQueueMetrics
{
    /// <summary>Number of jobs currently in <see cref="JobStatus.Queued"/> state.</summary>
    int PendingCount { get; }

    /// <summary>Number of jobs currently in <see cref="JobStatus.Running"/> or <see cref="JobStatus.Retrying"/> state.</summary>
    int RunningCount { get; }

    /// <summary>Number of jobs that have reached <see cref="JobStatus.Failed"/> state.</summary>
    int FailedCount { get; }

    /// <summary>Number of jobs that have reached <see cref="JobStatus.Completed"/> state.</summary>
    int CompletedCount { get; }

    /// <summary>
    /// Refreshes the metrics snapshot from the underlying job repository.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A snapshot of the current queue depth metrics.</returns>
    Task<JobQueueMetricsSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

/// <summary>
/// Immutable snapshot of job queue depth metrics at a point in time.
/// </summary>
/// <param name="PendingCount">Number of jobs in <see cref="JobStatus.Queued"/> state.</param>
/// <param name="RunningCount">Number of jobs in <see cref="JobStatus.Running"/> or <see cref="JobStatus.Retrying"/> state.</param>
/// <param name="FailedCount">Number of jobs in <see cref="JobStatus.Failed"/> state.</param>
/// <param name="CompletedCount">Number of jobs in <see cref="JobStatus.Completed"/> state.</param>
/// <param name="Timestamp">UTC timestamp when the snapshot was taken.</param>
public sealed record JobQueueMetricsSnapshot(
    int PendingCount,
    int RunningCount,
    int FailedCount,
    int CompletedCount,
    DateTimeOffset Timestamp);
