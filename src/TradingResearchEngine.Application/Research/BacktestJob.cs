using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// An async execution unit for long-running backtests and studies.
/// Persisted via <see cref="IRepository{T}"/> so that job records survive application restart.
/// </summary>
/// <param name="JobId">Unique identifier for this job.</param>
/// <param name="JobType">The kind of execution this job represents.</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="SubmittedAt">Timestamp when the job was submitted.</param>
/// <param name="StartedAt">Timestamp when execution began, or <c>null</c> if not yet started.</param>
/// <param name="CompletedAt">Timestamp when execution finished, or <c>null</c> if still running.</param>
/// <param name="Progress">Latest progress snapshot, or <c>null</c> if no progress has been reported.</param>
/// <param name="ResultId">Identifier of the persisted result, or <c>null</c> if not yet available.</param>
/// <param name="ErrorMessage">User-friendly error message when the job has failed, or <c>null</c>.</param>
/// <param name="ReproducibilitySnapshot">Snapshot of all inputs needed to reproduce this run, or <c>null</c>.</param>
public sealed record BacktestJob(
    string JobId,
    JobType JobType,
    JobStatus Status,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    ProgressSnapshot? Progress = null,
    string? ResultId = null,
    string? ErrorMessage = null,
    ReproducibilitySnapshot? ReproducibilitySnapshot = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => JobId;
}
