namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Enumerates the types of test-set lifecycle transitions that are audited.
/// </summary>
public enum TestSetAuditAction
{
    /// <summary>The sealed test set was unlocked for final validation.</summary>
    Unlock,

    /// <summary>The sealed test set was re-sealed after a previous unlock.</summary>
    Reseal
}

/// <summary>
/// A single audit entry recording a test-set lifecycle transition.
/// </summary>
/// <param name="StrategyVersionId">The strategy version whose test set was affected.</param>
/// <param name="Timestamp">When the transition occurred.</param>
/// <param name="Action">Whether this was an unlock or re-seal event.</param>
/// <param name="Reason">Optional human-readable reason for the transition.</param>
public sealed record TestSetAuditEntry(
    Guid StrategyVersionId,
    DateTimeOffset Timestamp,
    TestSetAuditAction Action,
    string? Reason);

/// <summary>
/// Records and retrieves audit entries for sealed test-set lifecycle transitions.
/// Every unlock (transition to FinalTest) and re-seal (transition back from FinalTest)
/// is persisted for accountability.
/// </summary>
public interface ITestSetAuditLog
{
    /// <summary>
    /// Records that the sealed test set was unlocked for the given strategy version.
    /// </summary>
    /// <param name="versionId">The strategy version ID.</param>
    /// <param name="reason">Optional reason for the unlock.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordUnlockAsync(Guid versionId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Records that the sealed test set was re-sealed for the given strategy version.
    /// </summary>
    /// <param name="versionId">The strategy version ID.</param>
    /// <param name="reason">Optional reason for the re-seal.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordResealAsync(Guid versionId, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Retrieves all audit entries for the given strategy version in chronological order.
    /// </summary>
    /// <param name="versionId">The strategy version ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All audit entries ordered by timestamp ascending.</returns>
    Task<IReadOnlyList<TestSetAuditEntry>> GetEntriesAsync(Guid versionId, CancellationToken ct = default);
}
