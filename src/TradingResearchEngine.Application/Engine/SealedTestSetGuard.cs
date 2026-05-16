using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Validates that a study's date range does not overlap with the sealed test set
/// on a strategy version. Called by study orchestration before dispatching any workflow.
/// Also records audit entries when the test set is unlocked or re-sealed.
/// </summary>
public sealed class SealedTestSetGuard
{
    private readonly ITestSetAuditLog _auditLog;

    /// <summary>
    /// Creates a new <see cref="SealedTestSetGuard"/> with the specified audit log.
    /// </summary>
    /// <param name="auditLog">The audit log for recording test-set transitions.</param>
    public SealedTestSetGuard(ITestSetAuditLog auditLog)
    {
        _auditLog = auditLog;
    }

    /// <summary>
    /// Throws <see cref="SealedTestSetViolationException"/> if the study date range
    /// overlaps the sealed test set on the given version.
    /// </summary>
    /// <param name="version">The strategy version to check.</param>
    /// <param name="studyStart">Start of the study date range (inclusive).</param>
    /// <param name="studyEnd">End of the study date range (exclusive).</param>
    public static void Validate(StrategyVersion version, DateTimeOffset studyStart, DateTimeOffset studyEnd)
    {
        if (version.SealedTestSet is not { IsSealed: true } sealed_) return;

        if (sealed_.Overlaps(studyStart, studyEnd))
        {
            throw new SealedTestSetViolationException(
                $"Study date range [{studyStart:yyyy-MM-dd}, {studyEnd:yyyy-MM-dd}) overlaps the sealed test set " +
                $"[{sealed_.Start:yyyy-MM-dd}, {sealed_.End:yyyy-MM-dd}). " +
                "Use the Final Validation action to run against the sealed set.");
        }
    }

    /// <summary>
    /// Extracts the study date range from a <see cref="ScenarioConfig"/> and validates
    /// against the sealed test set.
    /// </summary>
    public static void ValidateConfig(StrategyVersion version, ScenarioConfig config)
    {
#pragma warning disable CS0618 // Legacy dictionary access for backward compatibility
        var dataOpts = config.DataProviderOptions;
#pragma warning restore CS0618
        var from = dataOpts.TryGetValue("From", out var f) && f is DateTimeOffset df
            ? df : DateTimeOffset.MinValue;
        var to = dataOpts.TryGetValue("To", out var t) && t is DateTimeOffset dt
            ? dt : DateTimeOffset.MaxValue;

        Validate(version, from, to);
    }

    /// <summary>
    /// Records an audit entry when the sealed test set is unlocked (phase transitions to FinalTest).
    /// </summary>
    /// <param name="versionId">The strategy version ID.</param>
    /// <param name="reason">Optional reason for the unlock.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task RecordUnlockAsync(Guid versionId, string? reason = null, CancellationToken ct = default)
        => _auditLog.RecordUnlockAsync(versionId, reason, ct);

    /// <summary>
    /// Records an audit entry when the sealed test set is re-sealed (phase transitions back from FinalTest).
    /// </summary>
    /// <param name="versionId">The strategy version ID.</param>
    /// <param name="reason">Optional reason for the re-seal.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task RecordResealAsync(Guid versionId, string? reason = null, CancellationToken ct = default)
        => _auditLog.RecordResealAsync(versionId, reason, ct);
}
