namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Configuration for <see cref="Research.JobWorkerService"/> polling and concurrency.
/// </summary>
public sealed class JobWorkerOptions
{
    /// <summary>Interval between queue polls. Default 2 seconds.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Maximum number of jobs processed concurrently by the worker.
    /// Default 1 prevents CPU saturation when multiple queued jobs exist
    /// simultaneously alongside sweep jobs with their own internal
    /// MaxDegreeOfParallelism.
    /// </summary>
    public int MaxConcurrentJobs { get; set; } = 1;
}
