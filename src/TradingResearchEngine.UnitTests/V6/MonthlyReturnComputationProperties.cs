using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Helpers;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 18: MonthlyReturnComputation
/// Non-empty equity curve spanning multiple months → one return per calendar month.
/// **Validates: Requirements 13.2, 18.1**
/// </summary>
public class MonthlyReturnComputationProperties
{
    [Property(MaxTest = 100)]
    public bool MonthlyReturns_OnePerCalendarMonth(PositiveInt monthCount)
    {
        // Generate an equity curve spanning 1 to 12 months
        var months = (monthCount.Get % 12) + 1;
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var curve = new List<EquityCurvePoint>();
        var equity = 100_000m;

        for (int m = 0; m < months; m++)
        {
            var monthStart = baseDate.AddMonths(m);
            var monthEnd = monthStart.AddDays(15); // mid-month point
            curve.Add(new EquityCurvePoint(monthStart, equity));
            equity += 1000m; // small growth
            curve.Add(new EquityCurvePoint(monthEnd, equity));
        }

        var result = ChartComputationHelpers.ComputeMonthlyReturns(curve);

        // Should have exactly one return per calendar month
        if (result.Count != months) return false;

        // Each return should correspond to a distinct (year, month) pair
        var distinctMonths = result.Select(r => (r.Year, r.Month)).Distinct().Count();
        return distinctMonths == months;
    }

    [Property(MaxTest = 100)]
    public bool MonthlyReturns_EmptyCurve_ReturnsEmpty(bool _)
    {
        var result = ChartComputationHelpers.ComputeMonthlyReturns(new List<EquityCurvePoint>());
        return result.Count == 0;
    }
}
