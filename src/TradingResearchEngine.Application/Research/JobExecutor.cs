using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Executes jobs asynchronously. Manages <see cref="CancellationTokenSource"/> per active job
/// in memory for cooperative cancellation. Persists job records via
/// <see cref="IRepository{BacktestJob}"/> on every status transition for durability.
/// Registered as a singleton in the host.
/// </summary>
/// <remarks>
/// <para><b>In-memory vs persisted state:</b> The <c>_active</c> dictionary holds
/// <see cref="CancellationTokenSource"/> instances for currently running jobs only. This is
/// ephemeral, process-scoped state used for cancellation. The authoritative job record
/// (status, progress, result ID, reproducibility snapshot) is always persisted via
/// <see cref="IRepository{BacktestJob}"/> on every status transition.</para>
///
/// <para><b>Restart recovery:</b> On startup, <see cref="RecoverOrphanedJobsAsync"/> queries
/// persisted jobs with <c>Status == Queued || Status == Running</c> and transitions them to
/// <c>Status = Failed</c> with <c>ErrorMessage = "Process restarted; job was not completed."</c>.
/// No replay or re-execution is attempted.</para>
/// </remarks>
public sealed class JobExecutor : IDisposable
{
    /// <summary>Number of hours after completion before a job is eligible for cleanup.</summary>
    private const int JobRetentionHours = 24;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();
    private readonly IRepository<BacktestJob> _jobRepo;
    private readonly ILogger<JobExecutor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobExecutor"/> class.
    /// </summary>
    /// <param name="jobRepo">Repository for persisting job records.</param>
    /// <param name="logger">Logger instance.</param>
    public JobExecutor(IRepository<BacktestJob> jobRepo, ILogger<JobExecutor> logger)
    {
        _jobRepo = jobRepo;
        _logger = logger;
    }

    /// <summary>
    /// Called once at host startup to mark orphaned Queued/Running jobs as Failed.
    /// Jobs left in Queued or Running state after a process restart cannot be resumed,
    /// so they are transitioned to Failed with an explanatory error message.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task RecoverOrphanedJobsAsync(CancellationToken ct = default)
    {
        var allJobs = await _jobRepo.ListAsync(ct);
        foreach (var job in allJobs)
        {
            if (job.Status is JobStatus.Queued or JobStatus.Running)
            {
                var failed = job with
                {
                    Status = JobStatus.Failed,
                    CompletedAt = DateTimeOffset.UtcNow,
                    ErrorMessage = "Process restarted; job was not completed."
                };
                await _jobRepo.SaveAsync(failed, ct);
                _logger.LogWarning("Recovered orphaned job {JobId} (was {Status})", job.JobId, job.Status);
            }
        }
    }

    /// <summary>
    /// Submits a new job for execution. The job is persisted in <see cref="JobStatus.Queued"/>
    /// status and a <see cref="CancellationTokenSource"/> is registered for cancellation support.
    /// </summary>
    /// <param name="jobType">The type of execution to perform.</param>
    /// <param name="config">The scenario configuration to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unique job identifier.</returns>
    public async Task<string> SubmitAsync(JobType jobType, ScenarioConfig config, CancellationToken ct = default)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var job = new BacktestJob(
            JobId: jobId,
            JobType: jobType,
            Status: JobStatus.Queued,
            SubmittedAt: DateTimeOffset.UtcNow,
            Config: config);

        await _jobRepo.SaveAsync(job, ct);

        var cts = new CancellationTokenSource();
        _active[jobId] = cts;

        _logger.LogInformation("Job {JobId} submitted (type={JobType})", jobId, jobType);
        return jobId;
    }

    /// <summary>
    /// Transitions a job to <see cref="JobStatus.Running"/> and persists the update.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task MarkRunningAsync(string jobId, CancellationToken ct = default)
    {
        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        if (job is null) return;

        var running = job with
        {
            Status = JobStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        await _jobRepo.SaveAsync(running, ct);
    }

    /// <summary>
    /// Transitions a job to <see cref="JobStatus.Completed"/> with the given result identifier.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="resultId">The identifier of the persisted result.</param>
    /// <param name="reproducibility">Optional reproducibility snapshot.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task MarkCompletedAsync(
        string jobId,
        string resultId,
        ReproducibilitySnapshot? reproducibility = null,
        CancellationToken ct = default)
    {
        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        if (job is null) return;

        var completed = job with
        {
            Status = JobStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            ResultId = resultId,
            ReproducibilitySnapshot = reproducibility
        };
        await _jobRepo.SaveAsync(completed, ct);
        CleanupActive(jobId);
    }

    /// <summary>
    /// Transitions a job to <see cref="JobStatus.Failed"/> with the given error message.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="errorMessage">User-friendly error message (no stack traces).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task MarkFailedAsync(string jobId, string errorMessage, CancellationToken ct = default)
    {
        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        if (job is null) return;

        var failed = job with
        {
            Status = JobStatus.Failed,
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = errorMessage
        };
        await _jobRepo.SaveAsync(failed, ct);
        CleanupActive(jobId);
    }

    /// <summary>
    /// Updates the progress snapshot on a running job and persists the update.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="progress">The latest progress snapshot.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdateProgressAsync(string jobId, ProgressSnapshot progress, CancellationToken ct = default)
    {
        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        if (job is null) return;

        var updated = job with { Progress = progress };
        await _jobRepo.SaveAsync(updated, ct);
    }

    /// <summary>
    /// Returns the current state of a job, or <c>null</c> if not found.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The job record, or <c>null</c>.</returns>
    public Task<BacktestJob?> GetJobAsync(string jobId, CancellationToken ct = default)
        => _jobRepo.GetByIdAsync(jobId, ct);

    /// <summary>
    /// Cancels a running job by triggering its <see cref="CancellationTokenSource"/>.
    /// The job is transitioned to <see cref="JobStatus.Cancelled"/> and persisted.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the job was found and cancelled; <c>false</c> otherwise.</returns>
    public async Task<bool> CancelAsync(string jobId, CancellationToken ct = default)
    {
        if (_active.TryGetValue(jobId, out var cts))
        {
            await cts.CancelAsync();
        }

        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        if (job is null) return false;

        if (job.Status is not (JobStatus.Queued or JobStatus.Running))
            return false;

        var cancelled = job with
        {
            Status = JobStatus.Cancelled,
            CompletedAt = DateTimeOffset.UtcNow,
            ErrorMessage = "Job cancelled by user."
        };
        await _jobRepo.SaveAsync(cancelled, ct);
        CleanupActive(jobId);

        _logger.LogInformation("Job {JobId} cancelled", jobId);
        return true;
    }

    /// <summary>
    /// Returns all persisted job records.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All job records.</returns>
    public Task<IReadOnlyList<BacktestJob>> ListJobsAsync(CancellationToken ct = default)
        => _jobRepo.ListAsync(ct);

    /// <summary>
    /// Returns the <see cref="CancellationToken"/> for an active job, or
    /// <see cref="CancellationToken.None"/> if the job is not tracked.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <returns>The cancellation token for the job.</returns>
    public CancellationToken GetCancellationToken(string jobId)
        => _active.TryGetValue(jobId, out var cts) ? cts.Token : CancellationToken.None;

    /// <summary>
    /// Enqueues a new backtest job for background execution. Returns the job identifier
    /// immediately without blocking the caller (e.g. the Blazor SignalR circuit).
    /// This is the primary entry point used by the Strategy Builder UI.
    /// </summary>
    /// <param name="jobType">The type of execution to perform.</param>
    /// <param name="config">The scenario configuration to execute.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The unique job identifier.</returns>
    public Task<string> EnqueueAsync(JobType jobType, ScenarioConfig config, CancellationToken ct = default)
        => SubmitAsync(jobType, config, ct);

    /// <summary>
    /// Returns the current <see cref="JobStatus"/> for the specified job,
    /// or <c>null</c> if the job does not exist.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The job status, or <c>null</c> if not found.</returns>
    public async Task<JobStatus?> GetStatusAsync(string jobId, CancellationToken ct = default)
    {
        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        return job?.Status;
    }

    /// <summary>
    /// Returns the result identifier for a completed job, or <c>null</c> if the job
    /// has not completed or does not exist.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result identifier, or <c>null</c>.</returns>
    public async Task<string?> GetResultIdAsync(string jobId, CancellationToken ct = default)
    {
        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        return job?.ResultId;
    }

    /// <summary>
    /// Returns the error message for a failed job, or <c>null</c> if the job
    /// has not failed or does not exist.
    /// </summary>
    /// <param name="jobId">The job identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The error message, or <c>null</c>.</returns>
    public async Task<string?> GetErrorAsync(string jobId, CancellationToken ct = default)
    {
        var job = await _jobRepo.GetByIdAsync(jobId, ct);
        return job?.ErrorMessage;
    }

    /// <summary>
    /// Removes all persisted jobs that completed (or failed) more than 24 hours ago.
    /// Called periodically to prevent unbounded growth of the job store.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of jobs removed.</returns>
    public async Task<int> CleanupExpiredJobsAsync(CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-JobRetentionHours);
        var allJobs = await _jobRepo.ListAsync(ct);
        var expired = allJobs
            .Where(j => j.CompletedAt.HasValue && j.CompletedAt.Value < cutoff)
            .ToList();

        foreach (var job in expired)
        {
            await _jobRepo.DeleteAsync(job.JobId, ct);
            _logger.LogDebug("Cleaned up expired job {JobId} (completed at {CompletedAt})",
                job.JobId, job.CompletedAt);
        }

        if (expired.Count > 0)
            _logger.LogInformation("Cleaned up {Count} expired jobs older than {Hours}h",
                expired.Count, JobRetentionHours);

        return expired.Count;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var cts in _active.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _active.Clear();
    }

    private void CleanupActive(string jobId)
    {
        if (_active.TryRemove(jobId, out var cts))
            cts.Dispose();
    }
}
