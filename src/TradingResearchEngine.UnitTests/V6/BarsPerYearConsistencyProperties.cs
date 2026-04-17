using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 13: BarsPerYearConsistency
/// For any known timeframe, ForTimeframe(tf) equals 252 × barsPerDay(tf).
/// **Validates: Requirements 5.1, 5.2**
/// </summary>
public class BarsPerYearConsistencyProperties
{
    private static readonly (string timeframe, int barsPerDay)[] KnownTimeframes =
    {
        ("M1", 1440),
        ("M5", 288),
        ("M15", 96),
        ("M30", 48),
        ("H1", 24),
        ("H2", 12),
        ("H4", 6),
        ("D1", 1),
    };

    [Property(MaxTest = 100)]
    public bool BarsPerYear_Equals252TimesBarsPerDay(PositiveInt indexWrap)
    {
        var (timeframe, barsPerDay) = KnownTimeframes[indexWrap.Get % KnownTimeframes.Length];
        var result = BarsPerYearDefaults.ForTimeframe(timeframe);

        return result.HasValue && result.Value == 252 * barsPerDay;
    }
}
