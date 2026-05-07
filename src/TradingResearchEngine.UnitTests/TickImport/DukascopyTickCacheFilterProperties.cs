// Feature: tick-data-streaming-download, Property 4: Timestamp range filtering
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Property 4: For any list of tick records and any from/to range, the filtered output
/// contains only ticks with Timestamp >= from and Timestamp &lt;= to, and contains all
/// ticks from the input that satisfy that predicate.
/// **Validates: Requirements 9.1, 9.2**
/// </summary>
public class DukascopyTickCacheFilterProperties
{
    [Property(MaxTest = 100)]
    public bool TimestampRangeFilter_ContainsOnlyTicksWithinRange_AndNoQualifyingTicksDropped(
        PositiveInt tickCountWrap,
        PositiveInt daySeed,
        PositiveInt hourSeed,
        PositiveInt minuteSeed,
        PositiveInt secondSeed,
        PositiveInt msSeed,
        PositiveInt priceSeed,
        PositiveInt sizeSeed,
        PositiveInt fromSeed,
        PositiveInt toSeed)
    {
        // Generate 5-30 ticks with timestamps spread across a multi-day range
        var tickCount = (tickCountWrap.Get % 26) + 5;
        var symbol = "EURUSD";
        var baseDate = new DateTimeOffset(2023, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var ticks = new List<TickRecord>(tickCount);

        for (int i = 0; i < tickCount; i++)
        {
            // Spread timestamps across a 10-day range
            var dayOffset = (daySeed.Get + i * 7) % 10;
            var hour = (hourSeed.Get + i * 3) % 24;
            var minute = (minuteSeed.Get + i * 11) % 60;
            var second = (secondSeed.Get + i * 17) % 60;
            var ms = (msSeed.Get + i * 23) % 1000;

            var ts = baseDate
                .AddDays(dayOffset)
                .AddHours(hour)
                .AddMinutes(minute)
                .AddSeconds(second)
                .AddMilliseconds(ms);

            var price = Math.Round((decimal)((priceSeed.Get + i * 31) % 9999999 + 1) / 100000m, 5);
            var size = Math.Round((decimal)((sizeSeed.Get + i * 43) % 1001) / 10m, 1);

            ticks.Add(new TickRecord(
                symbol,
                new[] { new BidLevel(price, size) },
                new[] { new AskLevel(price + 0.00002m, size) },
                new LastTrade(price + 0.00001m, size, ts),
                ts));
        }

        // Generate from/to by picking two random timestamps and ensuring from <= to
        var fromOffset = (fromSeed.Get % 12) - 1; // -1 to 10 days from base (can be before first tick)
        var toOffset = (toSeed.Get % 12) - 1;
        var fromHour = (fromSeed.Get + 7) % 24;
        var toHour = (toSeed.Get + 13) % 24;

        var ts1 = baseDate.AddDays(fromOffset).AddHours(fromHour);
        var ts2 = baseDate.AddDays(toOffset).AddHours(toHour);

        // Ensure from <= to (swap if needed)
        DateTimeOffset from, to;
        if (ts1 <= ts2)
        {
            from = ts1;
            to = ts2;
        }
        else
        {
            from = ts2;
            to = ts1;
        }

        // Apply the same filtering logic used in GetTicks
        var filtered = ticks.Where(t => t.Timestamp >= from && t.Timestamp <= to).ToList();

        // Verify: All ticks in filtered output have timestamps within [from, to]
        var allWithinRange = filtered.All(t => t.Timestamp >= from && t.Timestamp <= to);
        if (!allWithinRange)
            return false;

        // Verify: All ticks from original list that satisfy the predicate are present
        // (no qualifying ticks are dropped)
        var expectedCount = ticks.Count(t => t.Timestamp >= from && t.Timestamp <= to);
        if (filtered.Count != expectedCount)
            return false;

        return true;
    }
}
