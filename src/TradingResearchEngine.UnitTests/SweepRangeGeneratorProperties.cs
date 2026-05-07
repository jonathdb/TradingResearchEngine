using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Web.Features.Research.Sweep;

namespace TradingResearchEngine.UnitTests;

// Feature: web-only-ux-overhaul, Property 1: Range generation produces correct sequence

/// <summary>
/// Property 1: Range generation produces correct sequence.
/// For any valid inputs (Low ≤ High, Increment > 0), SweepRangeGenerator.Generate
/// returns a list where: first element equals Low, each subsequent element equals
/// previous + Increment, all elements ≤ High, and list length equals
/// floor((high - low) / increment) + 1.
/// **Validates: Requirements 7.2**
/// </summary>
// Feature: web-only-ux-overhaul, Property 2: Range generation rejects invalid inputs

/// <summary>
/// Property 2: Range generation rejects invalid inputs.
/// For any inputs where Increment ≤ 0 OR Low > High, SweepRangeGenerator.Generate
/// returns null.
/// **Validates: Requirements 7.3, 7.4**
/// </summary>
public class SweepRangeGeneratorProperties
{
    [Property(MaxTest = 100)]
    public bool RangeGeneration_ProducesCorrectSequence(
        PositiveInt lowWrap,
        PositiveInt rangeWrap,
        PositiveInt incrementWrap)
    {
        // Generate valid inputs: Low in [-10000, 10000], range [0, 10000], increment (0, 1000]
        var low = ((decimal)(lowWrap.Get % 20001) - 10000m) / 100m;   // -100.00 to 100.00
        var range = (decimal)(rangeWrap.Get % 10001) / 100m;           // 0.00 to 100.00
        var high = low + range;                                         // always >= low
        var increment = (decimal)((incrementWrap.Get % 10000) + 1) / 100m; // 0.01 to 100.00

        var result = SweepRangeGenerator.Generate(low, high, increment);

        // Result must not be null for valid inputs
        if (result is null)
            return false;

        // First element equals Low
        if (result[0] != low)
            return false;

        // Each subsequent element equals previous + Increment
        for (int i = 1; i < result.Count; i++)
        {
            if (result[i] != result[i - 1] + increment)
                return false;
        }

        // All elements ≤ High
        for (int i = 0; i < result.Count; i++)
        {
            if (result[i] > high)
                return false;
        }

        // List length equals floor((high - low) / increment) + 1
        var expectedLength = (int)Math.Floor((high - low) / increment) + 1;
        if (result.Count != expectedLength)
            return false;

        return true;
    }

    /// <summary>
    /// Property 2a: For any low, high, and increment ≤ 0, Generate returns null.
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RangeGeneration_RejectsNonPositiveIncrement(int lowRaw, int highRaw, NonNegativeInt nonNegIncrWrap)
    {
        var low = (decimal)lowRaw / 100m;
        var high = (decimal)highRaw / 100m;
        // Ensure increment is <= 0: negate the non-negative value
        var increment = -(decimal)nonNegIncrWrap.Get / 100m; // always <= 0

        var result = SweepRangeGenerator.Generate(low, high, increment);

        return result is null;
    }

    /// <summary>
    /// Property 2b: For any low > high with positive increment, Generate returns null.
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool RangeGeneration_RejectsLowGreaterThanHigh(PositiveInt gapWrap, int highRaw, PositiveInt incrementWrap)
    {
        var high = (decimal)highRaw / 100m;
        // Ensure low > high by adding a positive gap
        var gap = (decimal)((gapWrap.Get % 10000) + 1) / 100m; // 0.01 to 100.00
        var low = high + gap; // always > high
        var increment = (decimal)((incrementWrap.Get % 10000) + 1) / 100m; // 0.01 to 100.00 (positive)

        var result = SweepRangeGenerator.Generate(low, high, increment);

        return result is null;
    }
}
