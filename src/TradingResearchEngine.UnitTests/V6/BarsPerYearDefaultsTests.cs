using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.UnitTests.V6;

public class BarsPerYearDefaultsTests
{
    [Theory]
    [InlineData("M1", 362880)]
    [InlineData("M5", 72576)]
    [InlineData("M15", 24192)]
    [InlineData("M30", 12096)]
    [InlineData("H1", 6048)]
    [InlineData("H2", 3024)]
    [InlineData("H4", 1512)]
    [InlineData("D1", 252)]
    public void AllTimeframeConstants_ArePositiveIntegers(string timeframe, int expected)
    {
        var result = BarsPerYearDefaults.ForTimeframe(timeframe);
        Assert.NotNull(result);
        Assert.True(result!.Value > 0);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void M1_Equals252Times1440()
    {
        Assert.Equal(252 * 1440, BarsPerYearDefaults.M1);
    }

    [Fact]
    public void ForTimeframe_UnknownTimeframe_ReturnsNull()
    {
        Assert.Null(BarsPerYearDefaults.ForTimeframe("W1"));
        Assert.Null(BarsPerYearDefaults.ForTimeframe("unknown"));
    }

    [Theory]
    [InlineData("Daily", 252)]
    [InlineData("1H", 6048)]
    [InlineData("4H", 1512)]
    [InlineData("15m", 24192)]
    public void ForTimeframe_AlternateLabels_ReturnsCorrectValue(string label, int expected)
    {
        Assert.Equal(expected, BarsPerYearDefaults.ForTimeframe(label));
    }

    [Fact]
    public void BarsToHumanDuration_KnownTimeframe_ReturnsFormattedString()
    {
        var result = BarsPerYearDefaults.BarsToHumanDuration(500, "M15");
        Assert.Contains("trading days", result);
        Assert.Contains("15-minute", result);
    }

    [Fact]
    public void BarsToHumanDuration_UnknownTimeframe_ReturnsBarsOnly()
    {
        var result = BarsPerYearDefaults.BarsToHumanDuration(500, "W1");
        Assert.Equal("500 bars", result);
    }
}
