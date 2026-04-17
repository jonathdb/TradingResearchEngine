using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.UnitTests.V6;

public class BarsToHumanDurationTests
{
    [Theory]
    [InlineData(500, "M15", "~6 trading days of 15-minute data required")]
    [InlineData(1440, "M1", "~1 trading days of 1-minute data required")]
    [InlineData(252, "D1", "~252 trading days of daily data required")]
    [InlineData(96, "M15", "~1 trading days of 15-minute data required")]
    [InlineData(48, "M30", "~1 trading days of 30-minute data required")]
    [InlineData(24, "H1", "~1 trading days of 1-hour data required")]
    [InlineData(6, "H4", "~1 trading days of 4-hour data required")]
    public void KnownBarCounts_ProduceCorrectStrings(int bars, string timeframe, string expected)
    {
        var result = BarsPerYearDefaults.BarsToHumanDuration(bars, timeframe);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void UnknownTimeframe_ReturnsFallback()
    {
        var result = BarsPerYearDefaults.BarsToHumanDuration(500, "W1");
        Assert.Equal("500 bars", result);
    }

    [Fact]
    public void M1_500Bars_CorrectTradingDays()
    {
        // 500 bars at M1 = 500 / 1440 = ~1 trading day (ceiling)
        var result = BarsPerYearDefaults.BarsToHumanDuration(500, "M1");
        Assert.Contains("trading days of 1-minute data required", result);
        Assert.StartsWith("~", result);
    }

    [Fact]
    public void H2_100Bars_CorrectTradingDays()
    {
        // 100 bars at H2 = 100 / 12 = ~9 trading days (ceiling)
        var result = BarsPerYearDefaults.BarsToHumanDuration(100, "H2");
        Assert.Equal("~9 trading days of 2-hour data required", result);
    }

    [Fact]
    public void M5_1000Bars_CorrectTradingDays()
    {
        // 1000 bars at M5 = 1000 / 288 = ~4 trading days (ceiling)
        var result = BarsPerYearDefaults.BarsToHumanDuration(1000, "M5");
        Assert.Equal("~4 trading days of 5-minute data required", result);
    }
}
