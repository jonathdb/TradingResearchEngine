using System.Diagnostics;
using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.UnitTests.Indicators;

// Feature: trading-engine-stories, Property 18: Skender Bridge Performance Bound

/// <summary>
/// Property 18: Skender Bridge Performance Bound.
/// 100,000 bars through MACD bridge completes within 2× wall-clock time of hand-written MacdIndicator.
/// Relative benchmark — not fixed time cap — to avoid CI hardware variability causing brittle failures.
/// **Validates: Requirements 23.6**
/// </summary>
public class SkenderBridgePerformanceTests
{
    private const int BarCount = 100_000;
    private const int FastPeriod = 12;
    private const int SlowPeriod = 26;
    private const int SignalPeriod = 9;
    private const double MaxRelativeSlowdown = 2.0;

    /// <summary>
    /// Generates a deterministic list of bars with realistic OHLCV data using a random walk.
    /// </summary>
    private static List<BarRecord> GenerateBars(int count)
    {
        var rng = new Random(42); // Fixed seed for reproducibility
        var bars = new List<BarRecord>(count);
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var price = 100m;

        for (int i = 0; i < count; i++)
        {
            var change = (decimal)(rng.NextDouble() * 4.0 - 2.0);
            price = Math.Max(10m, price + change);

            var open = price + (decimal)(rng.NextDouble() * 2.0 - 1.0);
            var close = price + (decimal)(rng.NextDouble() * 2.0 - 1.0);
            var high = Math.Max(open, close) + (decimal)(rng.NextDouble() * 2.0);
            var low = Math.Min(open, close) - (decimal)(rng.NextDouble() * 2.0);
            low = Math.Max(1m, low);
            high = Math.Max(low + 0.01m, high);
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

    /// <summary>
    /// The SkenderBridgeIndicator (MACD configuration) completes 100,000 bars within 2× the
    /// wall-clock time of the hand-written MacdIndicator processing the same data.
    /// This is a relative benchmark to avoid CI hardware variability causing brittle failures.
    /// **Validates: Requirements 23.6**
    /// </summary>
    [Fact]
    public void MacdBridge_CompletesWithin2x_HandWrittenBaseline()
    {
        // Arrange: generate 100,000 bars
        var bars = GenerateBars(BarCount);

        // Warmup both indicators once to eliminate JIT compilation effects
        var warmupBars = bars.Take(200).ToList();
        var warmupMacd = new MacdIndicator(FastPeriod, SlowPeriod, SignalPeriod);
        foreach (var bar in warmupBars) warmupMacd.Add(bar);

        var warmupBridge = new SkenderBridgeIndicator(
            "macd",
            new Dictionary<string, object>
            {
                ["fastPeriod"] = FastPeriod,
                ["slowPeriod"] = SlowPeriod,
                ["signalPeriod"] = SignalPeriod
            });
        foreach (var bar in warmupBars) warmupBridge.Add(bar);

        // Act: Time the hand-written MacdIndicator (baseline)
        var baselineMacd = new MacdIndicator(FastPeriod, SlowPeriod, SignalPeriod);
        var baselineSw = Stopwatch.StartNew();
        foreach (var bar in bars)
        {
            baselineMacd.Add(bar);
        }
        baselineSw.Stop();
        var baselineElapsed = baselineSw.Elapsed;

        // Act: Time the SkenderBridgeIndicator (MACD configuration)
        var bridge = new SkenderBridgeIndicator(
            "macd",
            new Dictionary<string, object>
            {
                ["fastPeriod"] = FastPeriod,
                ["slowPeriod"] = SlowPeriod,
                ["signalPeriod"] = SignalPeriod
            });
        var bridgeSw = Stopwatch.StartNew();
        foreach (var bar in bars)
        {
            bridge.Add(bar);
        }
        bridgeSw.Stop();
        var bridgeElapsed = bridgeSw.Elapsed;

        // Assert: bridge time <= 2× baseline time
        var ratio = bridgeElapsed.TotalMilliseconds / baselineElapsed.TotalMilliseconds;

        Assert.True(
            ratio <= MaxRelativeSlowdown,
            $"SkenderBridgeIndicator took {bridgeElapsed.TotalMilliseconds:F1}ms " +
            $"({ratio:F2}× baseline), exceeding the 2× threshold. " +
            $"Baseline (MacdIndicator): {baselineElapsed.TotalMilliseconds:F1}ms.");
    }
}
