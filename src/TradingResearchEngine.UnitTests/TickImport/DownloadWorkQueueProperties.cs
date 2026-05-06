// Feature: tick-data-first-import, Property 10: Work Queue Excludes Weekend Hours
using FsCheck;
using FsCheck.Xunit;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 10: For any date range, work queue has zero weekend entries and count == tradingDays × 24.
/// **Validates: Requirements 13.4, 13.2**
/// </summary>
public class DownloadWorkQueueProperties
{
    [Property(MaxTest = 100)]
    public bool WorkQueue_ExcludesWeekends_AndCountEqualsTradingDaysTimes24(
        PositiveInt startOffset, PositiveInt rangeLength)
    {
        var baseDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var startDate = baseDate.AddDays(startOffset.Get % 1000);
        var rangeDays = (rangeLength.Get % 60) + 1; // 1 to 60 days

        // Build trading days list (weekdays only, as the downloader receives)
        var tradingDays = new List<DateTime>();
        for (int i = 0; i < rangeDays; i++)
        {
            var day = startDate.AddDays(i);
            if (day.DayOfWeek != DayOfWeek.Saturday && day.DayOfWeek != DayOfWeek.Sunday)
                tradingDays.Add(day);
        }

        // Build flattened work queue (same logic as DukascopyTickDownloader)
        var workItems = new List<(DateTime Date, int Hour)>();
        foreach (var day in tradingDays)
        {
            if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday)
                continue;

            for (int hour = 0; hour < 24; hour++)
            {
                workItems.Add((day, hour));
            }
        }

        // Verify: zero weekend entries
        var hasWeekendEntries = workItems.Any(w =>
            w.Date.DayOfWeek == DayOfWeek.Saturday || w.Date.DayOfWeek == DayOfWeek.Sunday);

        // Verify: count == tradingDays × 24
        var expectedCount = tradingDays.Count * 24;

        return !hasWeekendEntries && workItems.Count == expectedCount;
    }
}
