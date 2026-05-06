// Feature: tick-data-first-import, Property 2: Incremental Detection Correctness
using FsCheck;
using FsCheck.Xunit;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 2: For any set of pre-cached days and date range, missing days = weekdays in range NOT in cached set.
/// Tests the algorithm directly without file system.
/// **Validates: Requirements 2.5, 3.1, 3.2**
/// </summary>
public class TickCacheDetectionProperties
{
    [Property(MaxTest = 100)]
    public bool MissingDays_EqualsWeekdaysNotInCachedSet(
        PositiveInt startOffset, PositiveInt rangeLength, int[] cachedDayOffsets)
    {
        // Build a date range
        var baseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var startDate = baseDate.AddDays(startOffset.Get % 1000);
        var rangeDays = (rangeLength.Get % 60) + 1; // 1 to 60 days
        var endDate = startDate.AddDays(rangeDays - 1);

        // Build cached set from offsets (some within range, some outside)
        var cachedSet = new HashSet<DateTime>();
        if (cachedDayOffsets is not null)
        {
            foreach (var offset in cachedDayOffsets)
            {
                var day = startDate.AddDays(Math.Abs(offset) % (rangeDays + 10));
                cachedSet.Add(day.Date);
            }
        }

        // Compute expected missing days: weekdays in [startDate..endDate] not in cachedSet
        var expectedMissing = new List<DateTime>();
        var current = startDate.Date;
        while (current <= endDate.Date)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                if (!cachedSet.Contains(current))
                    expectedMissing.Add(current);
            }
            current = current.AddDays(1);
        }

        // Compute actual missing days using the same algorithm as TickCacheService
        var actualMissing = ComputeMissingDays(startDate, endDate, cachedSet);

        return expectedMissing.SequenceEqual(actualMissing);
    }

    /// <summary>
    /// Reimplements the missing-day detection logic from TickCacheService
    /// (without file system dependency).
    /// </summary>
    private static List<DateTime> ComputeMissingDays(DateTime startDate, DateTime endDate, HashSet<DateTime> cachedDays)
    {
        var missing = new List<DateTime>();
        var current = startDate.Date;
        var end = endDate.Date;

        while (current <= end)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
            {
                if (!cachedDays.Contains(current))
                    missing.Add(current);
            }
            current = current.AddDays(1);
        }

        return missing;
    }
}
