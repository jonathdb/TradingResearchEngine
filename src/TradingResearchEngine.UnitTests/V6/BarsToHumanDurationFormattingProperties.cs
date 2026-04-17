using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 20: BarsToHumanDuration Formatting
/// Positive bar count + known timeframe → string matching
/// "~{tradingDays} trading days of {label} data required"
/// **Validates: Requirement 24.1**
/// </summary>
public class BarsToHumanDurationFormattingProperties
{
    private static readonly (string timeframe, string label, int barsPerDay)[] KnownTimeframes =
    {
        ("M1", "1-minute", 1440),
        ("M5", "5-minute", 288),
        ("M15", "15-minute", 96),
        ("M30", "30-minute", 48),
        ("H1", "1-hour", 24),
        ("H2", "2-hour", 12),
        ("H4", "4-hour", 6),
        ("D1", "daily", 1),
    };

    [Property(MaxTest = 100)]
    public bool KnownTimeframe_MatchesExpectedFormat(PositiveInt barsWrap, PositiveInt tfWrap)
    {
        int bars = (barsWrap.Get % 10000) + 1; // 1..10000
        var (timeframe, label, barsPerDay) = KnownTimeframes[tfWrap.Get % KnownTimeframes.Length];

        var result = BarsPerYearDefaults.BarsToHumanDuration(bars, timeframe);

        int expectedDays = (int)System.Math.Ceiling((double)bars / barsPerDay);
        var expected = $"~{expectedDays} trading days of {label} data required";

        return result == expected;
    }

    [Property(MaxTest = 100)]
    public bool UnknownTimeframe_ReturnsBarsOnly(PositiveInt barsWrap)
    {
        int bars = (barsWrap.Get % 10000) + 1;
        var result = BarsPerYearDefaults.BarsToHumanDuration(bars, "UNKNOWN_TF");

        return result == $"{bars} bars";
    }
}
