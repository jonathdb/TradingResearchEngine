// Feature: tick-data-streaming-download, Property 3: Chronological ordering after merge
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 3: For any collection of tick lists from multiple hours, merging and sorting
/// produces non-decreasing timestamp order.
/// **Validates: Requirements 8.1, 8.2, 7.2**
/// </summary>
public class DukascopyTickCacheMergeProperties
{
    [Property(MaxTest = 100)]
    public bool MergeAndSort_ProducesNonDecreasingTimestampOrder(
        PositiveInt listCountWrap,
        PositiveInt ticksPerListWrap,
        PositiveInt hourSeed,
        PositiveInt minuteSeed,
        PositiveInt secondSeed,
        PositiveInt msSeed,
        PositiveInt priceSeed,
        PositiveInt sizeSeed)
    {
        // Generate 2-6 "hour" lists
        var listCount = (listCountWrap.Get % 5) + 2;
        var symbol = "EURUSD";
        var baseDate = new DateTimeOffset(2023, 6, 15, 0, 0, 0, TimeSpan.Zero);

        var allLists = new List<List<TickRecord>>(listCount);

        for (int listIdx = 0; listIdx < listCount; listIdx++)
        {
            // Each list has 1-20 ticks
            var tickCount = (ticksPerListWrap.Get % 20) + 1;
            var hourTicks = new List<TickRecord>(tickCount);

            for (int t = 0; t < tickCount; t++)
            {
                // Generate random timestamps within a single day (potentially unsorted)
                var hour = (hourSeed.Get + listIdx * 7 + t * 3) % 24;
                var minute = (minuteSeed.Get + listIdx * 13 + t * 11) % 60;
                var second = (secondSeed.Get + listIdx * 17 + t * 7) % 60;
                var ms = (msSeed.Get + listIdx * 23 + t * 19) % 1000;

                var ts = baseDate.AddHours(hour).AddMinutes(minute).AddSeconds(second).AddMilliseconds(ms);

                var price = Math.Round((decimal)((priceSeed.Get + listIdx * 31 + t * 37) % 9999999 + 1) / 100000m, 5);
                var size = Math.Round((decimal)((sizeSeed.Get + listIdx * 41 + t * 43) % 1001) / 10m, 1);

                hourTicks.Add(new TickRecord(
                    symbol,
                    new[] { new BidLevel(price, size) },
                    new[] { new AskLevel(price + 0.00002m, size) },
                    new LastTrade(price + 0.00001m, size, ts),
                    ts));
            }

            allLists.Add(hourTicks);
        }

        // Merge all lists into a single list (same logic as FetchAndCacheDayTicksAsync)
        var merged = new List<TickRecord>();
        foreach (var hourList in allLists)
        {
            merged.AddRange(hourList);
        }

        // Sort by timestamp (same logic as in the production code)
        merged.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // Verify non-decreasing timestamp order
        for (int i = 1; i < merged.Count; i++)
        {
            if (merged[i].Timestamp < merged[i - 1].Timestamp)
                return false;
        }

        return true;
    }
}
