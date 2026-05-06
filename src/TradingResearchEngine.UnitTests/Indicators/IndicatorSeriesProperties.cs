using FsCheck;
using FsCheck.Xunit;
using Skender.Stock.Indicators;
using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.UnitTests.Indicators;

// Feature: trading-research-engine, Property 7: Indicator streaming matches batch computation
// Feature: trading-research-engine, Property 8: Indicator IsWarm transition

/// <summary>
/// Property-based tests for indicator streaming vs batch equivalence and IsWarm transition.
/// **Validates: Requirements 14.3, 14.4, 15.2, 15.4**
/// </summary>
public class IndicatorSeriesProperties
{
    private const decimal Tolerance = 1e-10m;

    /// <summary>
    /// Generates a list of BarRecord values with valid OHLCV data.
    /// Prices are constrained to realistic ranges to avoid Skender computation issues.
    /// </summary>
    private static List<BarRecord> GenerateBars(int count, int seed)
    {
        var rng = new Random(seed);
        var bars = new List<BarRecord>(count);
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var price = 100m;

        for (int i = 0; i < count; i++)
        {
            // Random walk with bounded price changes
            var change = (decimal)(rng.NextDouble() * 4.0 - 2.0); // -2 to +2
            price = Math.Max(10m, price + change);

            var open = price + (decimal)(rng.NextDouble() * 2.0 - 1.0);
            var close = price + (decimal)(rng.NextDouble() * 2.0 - 1.0);
            var high = Math.Max(open, close) + (decimal)(rng.NextDouble() * 2.0);
            var low = Math.Min(open, close) - (decimal)(rng.NextDouble() * 2.0);
            low = Math.Max(1m, low); // Ensure positive
            high = Math.Max(low + 0.01m, high); // Ensure high > low
            var volume = (decimal)(rng.NextDouble() * 1_000_000 + 1000);

            bars.Add(new BarRecord(
                Symbol: "TEST",
                Interval: "D1",
                Open: Math.Round(open, 4),
                High: Math.Round(high, 4),
                Low: Math.Round(low, 4),
                Close: Math.Round(close, 4),
                Volume: Math.Round(volume, 2),
                Timestamp: baseDate.AddDays(i)));
        }

        return bars;
    }

    private static List<Quote> BarsToQuotes(List<BarRecord> bars)
    {
        return bars.Select(b => new Quote
        {
            Date = b.Timestamp.UtcDateTime,
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume
        }).ToList();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 7: Indicator streaming matches batch computation
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For SMA: streaming results match batch computation on the complete sequence.
    /// SMA is purely window-based, so once the window contains enough data,
    /// the streaming result at position i should equal the batch result at position i.
    /// **Validates: Requirements 14.3, 14.4, 15.4**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool StreamingMatchesBatch_Sma_ResultsMatchBatchForFullSequence(PositiveInt seedWrap)
    {
        var period = 5; // Small period so we can test with manageable sequences
        var barCount = period * 3; // Enough bars to be well past warm-up
        var seed = seedWrap.Get;
        var bars = GenerateBars(barCount, seed);

        // Streaming: add bars one by one
        var indicator = new SmaIndicator(period);
        foreach (var bar in bars)
        {
            indicator.Add(bar);
        }

        // Batch: compute on complete sequence
        var quotes = BarsToQuotes(bars);
        var batchResults = quotes.GetSma(period).ToList();

        // Compare results from the warm-up point onward
        // The streaming indicator appends one result per Add call
        if (indicator.Results.Count != barCount) return false;

        for (int i = period - 1; i < barCount; i++)
        {
            var streamVal = indicator.Results[i].Sma;
            var batchVal = batchResults[i].Sma;

            if (streamVal is null && batchVal is null) continue;
            if (streamVal is null || batchVal is null) return false;

            var diff = Math.Abs((decimal)streamVal.Value - (decimal)batchVal.Value);
            if (diff > Tolerance) return false;
        }

        return true;
    }

    /// <summary>
    /// For EMA with sequences within the bounded window capacity:
    /// streaming results match batch computation exactly.
    /// When the total bar count is within WarmupPeriod × 2, the adapter
    /// has the full history available and results should match batch.
    /// **Validates: Requirements 14.3, 14.4, 15.4**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool StreamingMatchesBatch_Ema_ResultsMatchBatchWithinWindow(PositiveInt seedWrap)
    {
        var period = 5;
        // Keep bar count within the window capacity (period * 2 = 10)
        var barCount = period * 2;
        var seed = seedWrap.Get;
        var bars = GenerateBars(barCount, seed);

        // Streaming: add bars one by one
        var indicator = new EmaIndicator(period);
        foreach (var bar in bars)
        {
            indicator.Add(bar);
        }

        // Batch: compute on complete sequence
        var quotes = BarsToQuotes(bars);
        var batchResults = quotes.GetEma(period).ToList();

        // Compare results from the warm-up point onward
        if (indicator.Results.Count != barCount) return false;

        for (int i = period - 1; i < barCount; i++)
        {
            var streamVal = indicator.Results[i].Ema;
            var batchVal = batchResults[i].Ema;

            if (streamVal is null && batchVal is null) continue;
            if (streamVal is null || batchVal is null) return false;

            var diff = Math.Abs((decimal)streamVal.Value - (decimal)batchVal.Value);
            if (diff > Tolerance) return false;
        }

        return true;
    }

    /// <summary>
    /// For SMA with longer sequences exceeding the window:
    /// the last streaming result still matches the last batch result,
    /// because SMA only depends on the most recent N values.
    /// **Validates: Requirements 14.3, 14.4, 15.4**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool StreamingMatchesBatch_Sma_LastResultMatchesForLongSequence(PositiveInt seedWrap, PositiveInt extraBarsWrap)
    {
        var period = 10;
        var extraBars = (extraBarsWrap.Get % 50) + period * 3; // 30 to 80 bars total
        var barCount = extraBars;
        var seed = seedWrap.Get;
        var bars = GenerateBars(barCount, seed);

        // Streaming: add bars one by one
        var indicator = new SmaIndicator(period);
        foreach (var bar in bars)
        {
            indicator.Add(bar);
        }

        // Batch: compute on complete sequence
        var quotes = BarsToQuotes(bars);
        var batchResults = quotes.GetSma(period).ToList();

        // The last result should match
        var lastStreamVal = indicator.Results[^1].Sma;
        var lastBatchVal = batchResults[^1].Sma;

        if (lastStreamVal is null || lastBatchVal is null) return false;

        var diff = Math.Abs((decimal)lastStreamVal.Value - (decimal)lastBatchVal.Value);
        return diff <= Tolerance;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 8: Indicator IsWarm transition
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For any SMA indicator, IsWarm is false until Results.Count >= WarmupPeriod,
    /// then true thereafter. The transition occurs exactly at the WarmupPeriod-th Add call.
    /// **Validates: Requirements 15.2**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool IsWarmTransition_Sma_FalseUntilWarmupThenTrue(PositiveInt periodWrap, PositiveInt seedWrap)
    {
        var period = (periodWrap.Get % 20) + 2; // Period between 2 and 21
        var barCount = period * 3; // Well past warm-up
        var seed = seedWrap.Get;
        var bars = GenerateBars(barCount, seed);

        var indicator = new SmaIndicator(period);

        for (int i = 0; i < bars.Count; i++)
        {
            indicator.Add(bars[i]);

            var expectedWarm = indicator.Results.Count >= period;
            if (indicator.IsWarm != expectedWarm) return false;
        }

        return true;
    }

    /// <summary>
    /// For any EMA indicator, IsWarm is false until Results.Count >= WarmupPeriod,
    /// then true thereafter.
    /// **Validates: Requirements 15.2**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool IsWarmTransition_Ema_FalseUntilWarmupThenTrue(PositiveInt periodWrap, PositiveInt seedWrap)
    {
        var period = (periodWrap.Get % 20) + 2; // Period between 2 and 21
        var barCount = period * 3;
        var seed = seedWrap.Get;
        var bars = GenerateBars(barCount, seed);

        var indicator = new EmaIndicator(period);

        for (int i = 0; i < bars.Count; i++)
        {
            indicator.Add(bars[i]);

            var expectedWarm = indicator.Results.Count >= period;
            if (indicator.IsWarm != expectedWarm) return false;
        }

        return true;
    }

    /// <summary>
    /// For any indicator type, IsWarm transitions from false to true exactly once
    /// and never reverts back to false (without a Reset call).
    /// **Validates: Requirements 15.2**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool IsWarmTransition_NeverRevertsWithoutReset(PositiveInt periodWrap, PositiveInt seedWrap)
    {
        var period = (periodWrap.Get % 15) + 2; // Period between 2 and 16
        var barCount = period * 4;
        var seed = seedWrap.Get;
        var bars = GenerateBars(barCount, seed);

        var indicator = new SmaIndicator(period);
        var becameWarm = false;

        for (int i = 0; i < bars.Count; i++)
        {
            indicator.Add(bars[i]);

            if (!becameWarm && indicator.IsWarm)
            {
                becameWarm = true;
            }
            else if (becameWarm && !indicator.IsWarm)
            {
                // IsWarm reverted to false without Reset — violation
                return false;
            }
        }

        // Must have become warm at some point
        return becameWarm;
    }
}
