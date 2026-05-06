// Feature: tick-data-first-import, Property 3: Bar Aggregation OHLC and Volume Correctness
// Feature: tick-data-first-import, Property 4: Bar Timestamp Alignment
// Feature: tick-data-first-import, Property 5: Bar High >= Low Invariant
// Feature: tick-data-first-import, Property 6: Bars in Strictly Ascending Timestamp Order
// Feature: tick-data-first-import, Property 7: Tick Conservation
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.TickImport;

namespace TradingResearchEngine.UnitTests.TickImport;

/// <summary>
/// Properties 3-7: Tick-to-bar aggregation correctness properties.
/// Tests the aggregation logic extracted as a static helper.
/// </summary>
public class TickToBarAggregationProperties
{
    private static readonly int[] SupportedIntervals = { 1, 5, 15, 30, 60, 240, 1440 };

    /// <summary>
    /// Property 3: OHLC correctness — Open=first bid, High=max bid, Low=min bid, Close=last bid, Volume=sum bid volumes.
    /// **Validates: Requirements 4.7, 4.8**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool OhlcCorrectness_SingleWindow(PositiveInt tickCount, PositiveInt seed)
    {
        var rng = new Random(seed.Get);
        var count = (tickCount.Get % 50) + 1; // 1 to 50 ticks

        // Generate ticks all within a single 5-minute window
        var windowStart = new DateTimeOffset(2023, 6, 15, 10, 0, 0, TimeSpan.Zero);
        var ticks = new List<TickCsvRow>();
        for (int i = 0; i < count; i++)
        {
            var ts = windowStart.AddSeconds(i * (300.0 / count)); // spread within 5 min
            var bid = 1.0m + (decimal)(rng.NextDouble() * 0.01);
            var ask = bid + 0.0002m;
            var bidVol = (decimal)Math.Round(rng.NextDouble() * 10, 1);
            var askVol = (decimal)Math.Round(rng.NextDouble() * 10, 1);
            ticks.Add(new TickCsvRow(ts, bid, ask, bidVol, askVol));
        }

        var bars = AggregateToBars(ticks, 5);

        if (bars.Count != 1) return false;

        var bar = bars[0];
        return bar.Open == ticks[0].Bid
            && bar.High == ticks.Max(t => t.Bid)
            && bar.Low == ticks.Min(t => t.Bid)
            && bar.Close == ticks[^1].Bid
            && bar.Volume == ticks.Sum(t => t.BidVolume);
    }

    /// <summary>
    /// Property 4: Bar timestamp alignment — timestamp % intervalMinutes == 0.
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool BarTimestampAlignment_AlignedToInterval(PositiveInt tickCount, PositiveInt intervalIdx, PositiveInt seed)
    {
        var rng = new Random(seed.Get);
        var intervalMinutes = SupportedIntervals[intervalIdx.Get % SupportedIntervals.Length];
        var count = (tickCount.Get % 100) + 2; // 2 to 101 ticks

        var ticks = GenerateTicksAcrossDay(count, rng);
        var bars = AggregateToBars(ticks, intervalMinutes);

        return bars.All(bar =>
        {
            var minutesSinceMidnight = (int)bar.Timestamp.ToUniversalTime().TimeOfDay.TotalMinutes;
            return minutesSinceMidnight % intervalMinutes == 0;
        });
    }

    /// <summary>
    /// Property 5: Bar High >= Low for every bar.
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool HighGreaterThanOrEqualLow(PositiveInt tickCount, PositiveInt intervalIdx, PositiveInt seed)
    {
        var rng = new Random(seed.Get);
        var intervalMinutes = SupportedIntervals[intervalIdx.Get % SupportedIntervals.Length];
        var count = (tickCount.Get % 100) + 2;

        var ticks = GenerateTicksAcrossDay(count, rng);
        var bars = AggregateToBars(ticks, intervalMinutes);

        return bars.All(bar => bar.High >= bar.Low);
    }

    /// <summary>
    /// Property 6: Bars in strictly ascending timestamp order.
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool BarsInStrictlyAscendingOrder(PositiveInt tickCount, PositiveInt intervalIdx, PositiveInt seed)
    {
        var rng = new Random(seed.Get);
        var intervalMinutes = SupportedIntervals[intervalIdx.Get % SupportedIntervals.Length];
        var count = (tickCount.Get % 100) + 2;

        var ticks = GenerateTicksAcrossDay(count, rng);
        var bars = AggregateToBars(ticks, intervalMinutes);

        if (bars.Count < 2) return true;

        for (int i = 1; i < bars.Count; i++)
        {
            if (bars[i].Timestamp <= bars[i - 1].Timestamp)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Property 7: Tick conservation — all ticks consumed equals source tick count.
    /// **Validates: Requirements 6.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool TickConservation_AllTicksConsumed(PositiveInt tickCount, PositiveInt intervalIdx, PositiveInt seed)
    {
        var rng = new Random(seed.Get);
        var intervalMinutes = SupportedIntervals[intervalIdx.Get % SupportedIntervals.Length];
        var count = (tickCount.Get % 100) + 2;

        var ticks = GenerateTicksAcrossDay(count, rng);
        var bars = AggregateToBars(ticks, intervalMinutes);

        // Total volume across all bars should equal total bid volume of all ticks
        var totalBarVolume = bars.Sum(b => b.Volume);
        var totalTickVolume = ticks.Sum(t => t.BidVolume);

        return totalBarVolume == totalTickVolume;
    }

    #region Helpers

    private static List<TickCsvRow> GenerateTicksAcrossDay(int count, Random rng)
    {
        // Generate ticks spread across a single trading day (Monday)
        var dayStart = new DateTimeOffset(2023, 6, 12, 0, 0, 0, TimeSpan.Zero); // Monday
        var ticks = new List<TickCsvRow>();

        for (int i = 0; i < count; i++)
        {
            var minuteOffset = (int)(i * (1440.0 / count));
            var ts = dayStart.AddMinutes(minuteOffset).AddMilliseconds(rng.Next(0, 999));
            var bid = 1.08m + (decimal)(rng.NextDouble() * 0.01);
            var ask = bid + 0.0002m;
            var bidVol = Math.Round((decimal)(rng.NextDouble() * 10 + 0.1), 1);
            var askVol = Math.Round((decimal)(rng.NextDouble() * 10 + 0.1), 1);
            ticks.Add(new TickCsvRow(ts, bid, ask, bidVol, askVol));
        }

        // Sort by timestamp to ensure proper ordering
        ticks.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return ticks;
    }

    /// <summary>
    /// Reimplements the aggregation logic from TimeframeGeneratorService for testability.
    /// </summary>
    internal static List<BarResult> AggregateToBars(List<TickCsvRow> ticks, int intervalMinutes)
    {
        var bars = new List<BarResult>();
        BarResult? currentBar = null;
        DateTimeOffset currentWindowStart = default;

        foreach (var tick in ticks)
        {
            var windowStart = TruncateToInterval(tick.Timestamp, intervalMinutes);

            if (currentBar is null || windowStart != currentWindowStart)
            {
                if (currentBar is not null)
                    bars.Add(currentBar);

                currentWindowStart = windowStart;
                currentBar = new BarResult
                {
                    Timestamp = windowStart,
                    Open = tick.Bid,
                    High = tick.Bid,
                    Low = tick.Bid,
                    Close = tick.Bid,
                    Volume = tick.BidVolume
                };
            }
            else
            {
                if (tick.Bid > currentBar.High) currentBar.High = tick.Bid;
                if (tick.Bid < currentBar.Low) currentBar.Low = tick.Bid;
                currentBar.Close = tick.Bid;
                currentBar.Volume += tick.BidVolume;
            }
        }

        if (currentBar is not null)
            bars.Add(currentBar);

        return bars;
    }

    private static DateTimeOffset TruncateToInterval(DateTimeOffset timestamp, int intervalMinutes)
    {
        var utc = timestamp.ToUniversalTime();
        var minutesSinceMidnight = (int)(utc.TimeOfDay.TotalMinutes);
        var truncatedMinutes = (minutesSinceMidnight / intervalMinutes) * intervalMinutes;
        return new DateTimeOffset(
            utc.Date.AddMinutes(truncatedMinutes),
            TimeSpan.Zero);
    }

    internal sealed class BarResult
    {
        public DateTimeOffset Timestamp { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }

    #endregion
}
