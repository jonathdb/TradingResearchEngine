using TradingResearchEngine.Web.Features.Research.Sweep;

namespace TradingResearchEngine.UnitTests;

/// <summary>
/// Example-based unit tests for <see cref="SweepRangeGenerator"/>.
/// Validates Requirements 7.2, 7.3, 7.4.
/// </summary>
public class SweepRangeGeneratorTests
{
    /// <summary>
    /// Increment of zero is invalid and must return null.
    /// **Validates: Requirement 7.3**
    /// </summary>
    [Fact]
    public void SweepRangeGenerator_ZeroIncrement_ReturnsNull()
    {
        var result = SweepRangeGenerator.Generate(low: 1m, high: 10m, increment: 0m);

        Assert.Null(result);
    }

    /// <summary>
    /// Negative increment is invalid and must return null.
    /// **Validates: Requirement 7.3**
    /// </summary>
    [Fact]
    public void SweepRangeGenerator_NegativeIncrement_ReturnsNull()
    {
        var result = SweepRangeGenerator.Generate(low: 1m, high: 10m, increment: -2m);

        Assert.Null(result);
    }

    /// <summary>
    /// Low greater than High is invalid and must return null.
    /// **Validates: Requirement 7.4**
    /// </summary>
    [Fact]
    public void SweepRangeGenerator_LowGreaterThanHigh_ReturnsNull()
    {
        var result = SweepRangeGenerator.Generate(low: 15m, high: 5m, increment: 1m);

        Assert.Null(result);
    }

    /// <summary>
    /// When Low equals High, the range contains exactly one element equal to Low.
    /// **Validates: Requirement 7.2 (edge case)**
    /// </summary>
    [Fact]
    public void SweepRangeGenerator_LowEqualsHigh_ReturnsSingleElement()
    {
        var result = SweepRangeGenerator.Generate(low: 5m, high: 5m, increment: 1m);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(5m, result[0]);
    }
}
