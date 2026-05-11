using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.UnitTests.Strategy;

// Feature: trading-engine-stories, Property 9: Strategy Reset Equivalence

/// <summary>
/// Property 9: Strategy Reset Equivalence.
/// Process N bars, call Reset(), process same N bars → output identical to freshly constructed instance.
/// **Validates: Requirements 8.3**
/// </summary>
public class StrategyResetProperties
{
    /// <summary>
    /// Generates a valid OHLCV bar sequence where each bar has consistent OHLCV relationships:
    /// Low &lt;= Open &lt;= High, Low &lt;= Close &lt;= High, Volume &gt; 0.
    /// Prices walk randomly to produce meaningful indicator state and signal transitions.
    /// </summary>
    private static List<BarEvent> GenerateValidBars(int count, int seed)
    {
        var rng = new Random(seed);
        var bars = new List<BarEvent>(count);
        var baseTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

        decimal price = 100m;

        for (int i = 0; i < count; i++)
        {
            // Random walk with enough volatility to trigger breakouts
            decimal change = (decimal)(rng.NextDouble() * 6.0 - 3.0); // -3 to +3
            price = Math.Max(10m, price + change);

            decimal open = price + (decimal)(rng.NextDouble() * 2.0 - 1.0);
            decimal close = price + (decimal)(rng.NextDouble() * 2.0 - 1.0);
            decimal high = Math.Max(open, close) + (decimal)(rng.NextDouble() * 2.0);
            decimal low = Math.Min(open, close) - (decimal)(rng.NextDouble() * 2.0);
            low = Math.Max(1m, low); // Ensure positive price
            decimal volume = (decimal)(rng.NextDouble() * 10000.0 + 100.0);

            bars.Add(new BarEvent(
                Symbol: "TEST",
                Interval: "1D",
                Open: open,
                High: high,
                Low: low,
                Close: close,
                Volume: volume,
                Timestamp: baseTime.AddDays(i)));
        }

        return bars;
    }

    /// <summary>
    /// Processes a bar sequence through a strategy and collects all output events.
    /// </summary>
    private static List<EngineEvent> ProcessBars(IStrategy strategy, List<BarEvent> bars)
    {
        var allEvents = new List<EngineEvent>();
        foreach (var bar in bars)
        {
            var events = strategy.OnMarketData(bar);
            allEvents.AddRange(events);
        }
        return allEvents;
    }

    /// <summary>
    /// Compares two event lists for structural equality (same count, same event types and values).
    /// </summary>
    private static bool EventsAreEqual(List<EngineEvent> eventsA, List<EngineEvent> eventsB)
    {
        if (eventsA.Count != eventsB.Count)
            return false;

        for (int i = 0; i < eventsA.Count; i++)
        {
            if (!eventsA[i].Equals(eventsB[i]))
                return false;
        }

        return true;
    }

    [Property(MaxTest = 100)]
    public bool ResetInstance_ProducesIdenticalOutput_ToFreshInstance(PositiveInt barCountWrap, PositiveInt seedWrap)
    {
        // Constrain bar count to [25, 200] — enough to warm up Donchian (period 20) and generate signals
        int barCount = (barCountWrap.Get % 176) + 25;
        int seed = seedWrap.Get;

        var bars = GenerateValidBars(barCount, seed);
        int period = 20;

        // Instance A: process bars, reset, process same bars again
        var instanceA = new DonchianBreakoutStrategy(period);
        ProcessBars(instanceA, bars); // First pass — builds up state
        instanceA.Reset();            // Reset to initial state
        var outputAfterReset = ProcessBars(instanceA, bars); // Second pass after reset

        // Instance B: fresh instance, process same bars
        var instanceB = new DonchianBreakoutStrategy(period);
        var outputFresh = ProcessBars(instanceB, bars);

        // Outputs must be identical
        return EventsAreEqual(outputAfterReset, outputFresh);
    }
}
