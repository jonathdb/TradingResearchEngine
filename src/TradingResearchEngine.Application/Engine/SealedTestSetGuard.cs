using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Validates that a study's date range does not overlap with the sealed test set
/// on a strategy version. Called by study orchestration before dispatching any workflow.
/// </summary>
public static class SealedTestSetGuard
{
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
        var dataOpts = config.DataProviderOptions;
        var from = dataOpts.TryGetValue("From", out var f) && f is DateTimeOffset df
            ? df : DateTimeOffset.MinValue;
        var to = dataOpts.TryGetValue("To", out var t) && t is DateTimeOffset dt
            ? dt : DateTimeOffset.MaxValue;

        Validate(version, from, to);
    }
}
