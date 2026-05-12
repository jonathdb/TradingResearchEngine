using System.Globalization;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Parses the creation timestamp from a legacy RunId prefix.
/// RunIds follow the format <c>yyyyMMdd-HHmmss-{guid}</c>.
/// Used as a fallback when <see cref="TradingResearchEngine.Core.Results.BacktestResult.CreatedAt"/>
/// is <c>default</c> (i.e., the record was persisted before V9 added explicit timestamps).
/// </summary>
public static class RunIdDateParser
{
    private const string DateFormat = "yyyyMMdd-HHmmss";
    private const int PrefixLength = 15; // "yyyyMMdd-HHmmss" = 15 chars

    /// <summary>
    /// Attempts to parse a <see cref="DateTimeOffset"/> from the RunId prefix.
    /// Returns <c>null</c> if the RunId is null, too short, or does not match the expected format.
    /// </summary>
    /// <param name="runId">The RunId string (e.g., "20240315-143022-abc123...").</param>
    /// <returns>The parsed <see cref="DateTimeOffset"/> in UTC, or <c>null</c> on failure.</returns>
    public static DateTimeOffset? TryParse(string? runId)
    {
        if (string.IsNullOrWhiteSpace(runId) || runId.Length < PrefixLength)
            return null;

        if (DateTime.TryParseExact(
                runId[..PrefixLength],
                DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dt))
        {
            return new DateTimeOffset(dt, TimeSpan.Zero);
        }

        return null;
    }

    /// <summary>
    /// Parses the RunId prefix or returns <see cref="DateTimeOffset.MinValue"/> if parsing fails.
    /// Used for populating <c>CreatedAt</c> on legacy records during deserialization.
    /// </summary>
    public static DateTimeOffset ParseOrMin(string? runId)
        => TryParse(runId) ?? DateTimeOffset.MinValue;
}
