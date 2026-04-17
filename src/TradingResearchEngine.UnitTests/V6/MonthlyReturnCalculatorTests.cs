using TradingResearchEngine.Application.Helpers;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>Unit tests for ChartComputationHelpers.ComputeMonthlyReturns.</summary>
public sealed class MonthlyReturnCalculatorTests
{
    [Fact]
    public void ComputeMonthlyReturns_ThreeMonths_ReturnsThreeEntries()
    {
        // Arrange: equity curve spanning Jan, Feb, Mar 2024
        var curve = new List<EquityCurvePoint>
        {
            new(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), 100_000m),
            new(new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero), 105_000m),
            new(new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero), 105_000m),
            new(new DateTimeOffset(2024, 2, 28, 0, 0, 0, TimeSpan.Zero), 110_000m),
            new(new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero), 110_000m),
            new(new DateTimeOffset(2024, 3, 31, 0, 0, 0, TimeSpan.Zero), 108_000m),
        };

        // Act
        var result = ChartComputationHelpers.ComputeMonthlyReturns(curve);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(2024, result[0].Year);
        Assert.Equal(1, result[0].Month);
        // Jan: (105000 - 100000) / 100000 * 100 = 5%
        Assert.Equal(5.0m, result[0].ReturnPercent);
        // Feb: (110000 - 105000) / 105000 * 100 ≈ 4.76%
        Assert.InRange(result[1].ReturnPercent, 4.7m, 4.8m);
        // Mar: (108000 - 110000) / 110000 * 100 ≈ -1.82%
        Assert.InRange(result[2].ReturnPercent, -1.9m, -1.8m);
    }

    [Fact]
    public void ComputeMonthlyReturns_SingleBar_ReturnsSingleMonthReturn()
    {
        var curve = new List<EquityCurvePoint>
        {
            new(new DateTimeOffset(2024, 5, 15, 0, 0, 0, TimeSpan.Zero), 50_000m),
        };

        var result = ChartComputationHelpers.ComputeMonthlyReturns(curve);

        Assert.Single(result);
        Assert.Equal(2024, result[0].Year);
        Assert.Equal(5, result[0].Month);
        // Single point: (50000 - 50000) / 50000 * 100 = 0%
        Assert.Equal(0m, result[0].ReturnPercent);
    }

    [Fact]
    public void ComputeMonthlyReturns_ReturnsNormalisedAsPercentages()
    {
        var curve = new List<EquityCurvePoint>
        {
            new(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), 100_000m),
            new(new DateTimeOffset(2024, 1, 31, 0, 0, 0, TimeSpan.Zero), 110_000m),
        };

        var result = ChartComputationHelpers.ComputeMonthlyReturns(curve);

        // Should be 10 (percent), not 0.10 (decimal)
        Assert.Equal(10.0m, result[0].ReturnPercent);
    }

    [Fact]
    public void ComputeMonthlyReturns_EmptyCurve_ReturnsEmpty()
    {
        var result = ChartComputationHelpers.ComputeMonthlyReturns(new List<EquityCurvePoint>());
        Assert.Empty(result);
    }

    [Fact]
    public void ComputeMonthlyReturns_NullCurve_ReturnsEmpty()
    {
        var result = ChartComputationHelpers.ComputeMonthlyReturns(null!);
        Assert.Empty(result);
    }
}
