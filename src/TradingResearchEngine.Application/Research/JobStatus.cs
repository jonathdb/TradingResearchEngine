namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Lifecycle status of a <see cref="BacktestJob"/>.
/// </summary>
public enum JobStatus
{
    /// <summary>Job has been submitted but has not started executing.</summary>
    Queued,

    /// <summary>Job is currently executing.</summary>
    Running,

    /// <summary>Job finished successfully and a result is available.</summary>
    Completed,

    /// <summary>Job terminated due to an error.</summary>
    Failed,

    /// <summary>Job was cancelled by the user.</summary>
    Cancelled
}
