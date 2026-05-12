namespace TradingResearchEngine.Application.Research;

/// <summary>
/// A snapshot entry representing a job's position in the execution queue.
/// Used by the UI to display queue position and estimated wait time.
/// </summary>
/// <param name="JobId">Unique job identifier.</param>
/// <param name="JobType">The type of execution.</param>
/// <param name="QueuedAt">When the job was submitted.</param>
/// <param name="Status">Current status (Queued or Running).</param>
/// <param name="Position">Queue position (1-based). Running jobs have position 0.</param>
public sealed record JobQueueEntry(
    string JobId,
    JobType JobType,
    DateTimeOffset QueuedAt,
    JobStatus Status,
    int Position);
