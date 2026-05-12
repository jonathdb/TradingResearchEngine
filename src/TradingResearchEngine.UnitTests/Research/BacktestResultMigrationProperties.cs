using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.Research;

// Feature: research-platform-v9, Property 5: Legacy RunId Date Parsing Fallback

/// <summary>
/// Property-based tests verifying that <see cref="RunIdDateParser"/> correctly
/// extracts timestamps from legacy RunId prefixes in the format yyyyMMdd-HHmmss-{guid}.
/// </summary>
public sealed class BacktestResultMigrationProperties
{
    /// <summary>
    /// For any valid date components, formatting as yyyyMMdd-HHmmss-{guid} and parsing
    /// produces a DateTimeOffset matching the original date components.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ValidRunId_ParsesCorrectDate(
        PositiveInt yearWrap,
        PositiveInt monthWrap,
        PositiveInt dayWrap,
        PositiveInt hourWrap,
        PositiveInt minuteWrap,
        PositiveInt secondWrap)
    {
        // Generate valid date components
        var year = 2020 + (yearWrap.Get % 7);    // 2020–2026
        var month = (monthWrap.Get % 12) + 1;     // 1–12
        var maxDay = DateTime.DaysInMonth(year, month);
        var day = (dayWrap.Get % maxDay) + 1;     // 1–maxDay
        var hour = hourWrap.Get % 24;              // 0–23
        var minute = minuteWrap.Get % 60;          // 0–59
        var second = secondWrap.Get % 60;          // 0–59

        // Build RunId in the expected format
        var prefix = $"{year:D4}{month:D2}{day:D2}-{hour:D2}{minute:D2}{second:D2}";
        var runId = $"{prefix}-{Guid.NewGuid()}";

        var result = RunIdDateParser.TryParse(runId);

        if (result is null) return false;

        return result.Value.Year == year
            && result.Value.Month == month
            && result.Value.Day == day
            && result.Value.Hour == hour
            && result.Value.Minute == minute
            && result.Value.Second == second
            && result.Value.Offset == TimeSpan.Zero; // UTC
    }

    /// <summary>
    /// Null or empty RunId returns null.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NullOrShortRunId_ReturnsNull(NonNegativeInt lengthWrap)
    {
        var length = lengthWrap.Get % 15; // 0–14 (all too short)
        var runId = length == 0 ? null : new string('x', length);

        return RunIdDateParser.TryParse(runId) is null;
    }

    /// <summary>
    /// ParseOrMin returns MinValue for invalid RunIds instead of null.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ParseOrMin_InvalidRunId_ReturnsMinValue(NonNegativeInt lengthWrap)
    {
        var length = lengthWrap.Get % 15;
        var runId = length == 0 ? null : new string('a', length);

        return RunIdDateParser.ParseOrMin(runId) == DateTimeOffset.MinValue;
    }

    /// <summary>
    /// ParseOrMin returns the same value as TryParse for valid RunIds.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ParseOrMin_ValidRunId_MatchesTryParse(PositiveInt yearWrap, PositiveInt monthWrap)
    {
        var year = 2020 + (yearWrap.Get % 7);
        var month = (monthWrap.Get % 12) + 1;
        var runId = $"{year:D4}{month:D2}15-120000-{Guid.NewGuid()}";

        var tryParseResult = RunIdDateParser.TryParse(runId);
        var parseOrMinResult = RunIdDateParser.ParseOrMin(runId);

        if (tryParseResult is null) return false;
        return parseOrMinResult == tryParseResult.Value;
    }
}
