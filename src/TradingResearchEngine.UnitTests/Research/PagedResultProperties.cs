using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.Research;

// Feature: research-platform-v9, Property 3: PagedResult TotalPages Computation

/// <summary>
/// Property-based tests verifying that <see cref="PagedResult{T}.TotalPages"/>
/// correctly computes ceiling division for all valid inputs.
/// </summary>
public sealed class PagedResultProperties
{
    /// <summary>
    /// For any TotalCount >= 0 and PageSize > 0, TotalPages equals ⌈TotalCount / PageSize⌉.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TotalPages_EqualsCorrectCeilingDivision(NonNegativeInt totalCountWrap, PositiveInt pageSizeWrap)
    {
        var totalCount = totalCountWrap.Get % 10000; // keep reasonable range
        var pageSize = (pageSizeWrap.Get % 100) + 1; // 1–100

        var result = new PagedResult<string>(
            Items: Array.Empty<string>(),
            TotalCount: totalCount,
            Page: 1,
            PageSize: pageSize);

        var expected = (int)Math.Ceiling((double)totalCount / pageSize);
        return result.TotalPages == expected;
    }

    /// <summary>
    /// When PageSize is 0, TotalPages is 0 (avoids division by zero).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TotalPages_ZeroPageSize_ReturnsZero(NonNegativeInt totalCountWrap)
    {
        var totalCount = totalCountWrap.Get % 10000;

        var result = new PagedResult<string>(
            Items: Array.Empty<string>(),
            TotalCount: totalCount,
            Page: 1,
            PageSize: 0);

        return result.TotalPages == 0;
    }

    /// <summary>
    /// When TotalCount is 0, TotalPages is 0 regardless of PageSize.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TotalPages_ZeroItems_ReturnsZero(PositiveInt pageSizeWrap)
    {
        var pageSize = (pageSizeWrap.Get % 100) + 1;

        var result = new PagedResult<string>(
            Items: Array.Empty<string>(),
            TotalCount: 0,
            Page: 1,
            PageSize: pageSize);

        return result.TotalPages == 0;
    }

    /// <summary>
    /// When TotalCount is exactly divisible by PageSize, TotalPages equals TotalCount / PageSize.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TotalPages_ExactDivision_NoExtraPage(PositiveInt multiplierWrap, PositiveInt pageSizeWrap)
    {
        var pageSize = (pageSizeWrap.Get % 50) + 1;  // 1–50
        var multiplier = (multiplierWrap.Get % 20) + 1; // 1–20
        var totalCount = pageSize * multiplier;

        var result = new PagedResult<int>(
            Items: Array.Empty<int>(),
            TotalCount: totalCount,
            Page: 1,
            PageSize: pageSize);

        return result.TotalPages == multiplier;
    }

    /// <summary>
    /// When TotalCount has a remainder when divided by PageSize, TotalPages is one more than the integer division.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TotalPages_WithRemainder_AddsOnePage(PositiveInt multiplierWrap, PositiveInt pageSizeWrap, PositiveInt remainderWrap)
    {
        var pageSize = (pageSizeWrap.Get % 50) + 2;  // 2–51 (need room for remainder)
        var multiplier = (multiplierWrap.Get % 20) + 1; // 1–20
        var remainder = (remainderWrap.Get % (pageSize - 1)) + 1; // 1 to pageSize-1
        var totalCount = pageSize * multiplier + remainder;

        var result = new PagedResult<int>(
            Items: Array.Empty<int>(),
            TotalCount: totalCount,
            Page: 1,
            PageSize: pageSize);

        return result.TotalPages == multiplier + 1;
    }
}
