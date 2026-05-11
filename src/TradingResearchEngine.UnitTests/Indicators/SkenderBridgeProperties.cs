using FsCheck;
using FsCheck.Xunit;
using Skender.Stock.Indicators;
using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.UnitTests.Indicators;

// Feature: trading-engine-stories, Property 17: Skender Bridge Output Equivalence

/// <summary>
/// Property 17: Skender Bridge Output Equivalence.
/// For any valid bar sequence of length N ≥ warmup period, the SkenderBridgeIndicator output
/// for the last bar SHALL equal the output produced by calling the corresponding Skender
/// extension method directly on the same quote data.
/// **Validates: Requirements 23.1**
/// </summary>
public class SkenderBridgeProperties
{
    private const int SmaPeriod = 20;
    private const int MinBars = 30; // Must be >= warmup period for SMA(20)
    private const decimal Tolerance = 1e-10m;

    /// <summary>
    /// Generates a list of BarRecord values with valid OHLCV data using a random walk.
    /// Prices are constrained to realistic ranges to avoid computation issues.
    /// </summary>
    private static List<BarRecord> GenerateBars(int count, int seed)
    {
        var rng = new Random(seed);
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
    /// Converts BarRecords to Skender Quotes for direct extension method calls.
    /// </summary>
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

    /// <summary>
    /// For any valid bar sequence of length N ≥ warmup period (SMA with period 20),
    /// the SkenderBridgeIndicator last output equals the direct Skender GetSma() last output.
    /// **Validates: Requirements 23.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool BridgeOutput_EqualsDirect_Sma(PositiveInt seedWrap, PositiveInt extraBarsWrap)
    {
        var seed = seedWrap.Get;
        var barCount = MinBars + (extraBarsWrap.Get % 70); // 30 to 99 bars
        var bars = GenerateBars(barCount, seed);

        // Process through SkenderBridgeIndicator
        var bridge = new SkenderBridgeIndicator(
            "sma",
            new Dictionary<string, object> { ["period"] = SmaPeriod });

        foreach (var bar in bars)
        {
            bridge.Add(bar);
        }

        // Process through direct Skender extension method
        var quotes = BarsToQuotes(bars);
        var directResults = quotes.GetSma(SmaPeriod).ToList();

        // Compare last result
        var bridgeLastValue = bridge.Results[^1];
        var directLastValue = directResults[^1].Sma;

        if (directLastValue is null)
            return bridgeLastValue == 0m; // Bridge returns 0 for null

        var diff = Math.Abs(bridgeLastValue - (decimal)directLastValue.Value);
        return diff <= Tolerance;
    }

    /// <summary>
    /// For any valid bar sequence of length N ≥ warmup period (EMA with period 20),
    /// the SkenderBridgeIndicator last output equals the direct Skender GetEma() last output.
    /// **Validates: Requirements 23.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool BridgeOutput_EqualsDirect_Ema(PositiveInt seedWrap, PositiveInt extraBarsWrap)
    {
        var seed = seedWrap.Get;
        var barCount = MinBars + (extraBarsWrap.Get % 70); // 30 to 99 bars
        var bars = GenerateBars(barCount, seed);

        // Process through SkenderBridgeIndicator
        var bridge = new SkenderBridgeIndicator(
            "ema",
            new Dictionary<string, object> { ["period"] = SmaPeriod });

        foreach (var bar in bars)
        {
            bridge.Add(bar);
        }

        // Process through direct Skender extension method
        var quotes = BarsToQuotes(bars);
        var directResults = quotes.GetEma(SmaPeriod).ToList();

        // Compare last result
        var bridgeLastValue = bridge.Results[^1];
        var directLastValue = directResults[^1].Ema;

        if (directLastValue is null)
            return bridgeLastValue == 0m; // Bridge returns 0 for null

        var diff = Math.Abs(bridgeLastValue - (decimal)directLastValue.Value);
        return diff <= Tolerance;
    }

    /// <summary>
    /// For any valid bar sequence of length N ≥ warmup period,
    /// the SkenderBridgeIndicator output for ALL bars past warmup equals the direct
    /// Skender extension method output — not just the last bar.
    /// This strengthens the equivalence guarantee across the entire warm period.
    /// **Validates: Requirements 23.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool BridgeOutput_EqualsDirectForAllWarmBars_Sma(PositiveInt seedWrap, PositiveInt extraBarsWrap)
    {
        var seed = seedWrap.Get;
        var barCount = MinBars + (extraBarsWrap.Get % 70); // 30 to 99 bars
        var bars = GenerateBars(barCount, seed);

        // Process through SkenderBridgeIndicator
        var bridge = new SkenderBridgeIndicator(
            "sma",
            new Dictionary<string, object> { ["period"] = SmaPeriod });

        foreach (var bar in bars)
        {
            bridge.Add(bar);
        }

        // Process through direct Skender extension method
        var quotes = BarsToQuotes(bars);
        var directResults = quotes.GetSma(SmaPeriod).ToList();

        // Compare all results from warmup onward
        for (int i = SmaPeriod - 1; i < barCount; i++)
        {
            var bridgeValue = bridge.Results[i];
            var directValue = directResults[i].Sma;

            if (directValue is null)
            {
                if (bridgeValue != 0m) return false;
            }
            else
            {
                var diff = Math.Abs(bridgeValue - (decimal)directValue.Value);
                if (diff > Tolerance) return false;
            }
        }

        return true;
    }
}
