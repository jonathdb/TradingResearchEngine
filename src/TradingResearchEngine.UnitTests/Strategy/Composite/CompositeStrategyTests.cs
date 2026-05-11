using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategies.Composite;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Unit tests for CompositeStrategy covering construction, warm-up gating,
/// signal emission, direction modes, and state machine behaviour.
/// Requirements: 1.1, 1.2, 1.3, 1.5, 1.6
/// </summary>
public class CompositeStrategyTests
{
    #region Helpers

    private static CompositeStrategyConfig CreateSmaCrossoverConfig(
        int fastPeriod = 10,
        int slowPeriod = 30,
        DirectionMode directionMode = DirectionMode.Long)
    {
        return new CompositeStrategyConfig(
            "Test SMA Crossover",
            new List<IndicatorConfig>
            {
                new("sma_fast", "sma", new Dictionary<string, object> { ["period"] = fastPeriod }),
                new("sma_slow", "sma", new Dictionary<string, object> { ["period"] = slowPeriod })
            },
            "sma_fast > sma_slow",
            "sma_fast < sma_slow",
            directionMode);
    }

    private static BarEvent CreateBar(decimal close, int dayOffset = 0)
    {
        return new BarEvent(
            "TEST",
            "D1",
            close - 1m,
            close + 1m,
            close - 2m,
            close,
            1000m,
            new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(dayOffset));
    }

    /// <summary>
    /// Feeds bars to the strategy and returns all emitted signals.
    /// Generates a rising sequence followed by a falling sequence to trigger crossovers.
    /// </summary>
    private static List<SignalEvent> FeedBarsAndCollectSignals(
        CompositeStrategy strategy,
        int warmupBars,
        decimal[] prices)
    {
        var signals = new List<SignalEvent>();
        var dayOffset = 0;

        // Feed warmup bars with stable price
        for (var i = 0; i < warmupBars; i++)
        {
            var events = strategy.OnMarketData(CreateBar(100m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        // Feed the actual price sequence
        foreach (var price in prices)
        {
            var events = strategy.OnMarketData(CreateBar(price, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        return signals;
    }

    #endregion

    #region Construction

    [Fact]
    public void Constructor_ValidConfig_Succeeds()
    {
        var config = CreateSmaCrossoverConfig();

        var strategy = new CompositeStrategy(config);

        Assert.NotNull(strategy);
    }

    [Fact]
    public void Constructor_UnknownIndicatorType_ThrowsInvalidOperationException()
    {
        var config = new CompositeStrategyConfig(
            "Bad Config",
            new List<IndicatorConfig>
            {
                new("ind1", "unknown_type", new Dictionary<string, object> { ["period"] = 10 })
            },
            "ind1 > 50",
            "ind1 < 30");

        var ex = Assert.Throws<InvalidOperationException>(() => new CompositeStrategy(config));
        Assert.Contains("Invalid CompositeStrategyConfig", ex.Message);
    }

    [Fact]
    public void Constructor_BadExpression_ThrowsInvalidOperationException()
    {
        var config = new CompositeStrategyConfig(
            "Bad Expression",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 })
            },
            "!!! invalid expression !!!",
            "sma10 < 50");

        var ex = Assert.Throws<InvalidOperationException>(() => new CompositeStrategy(config));
        Assert.Contains("Invalid CompositeStrategyConfig", ex.Message);
    }

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CompositeStrategy(null!));
    }

    #endregion

    #region Warm-Up Gating

    [Fact]
    public void OnMarketData_BeforeAllIndicatorsWarm_EmitsNoSignal()
    {
        var config = CreateSmaCrossoverConfig(fastPeriod: 5, slowPeriod: 10);
        var strategy = new CompositeStrategy(config);

        // Feed fewer bars than the slow period requires for warmup
        var signals = new List<SignalEvent>();
        for (var i = 0; i < 5; i++)
        {
            var events = strategy.OnMarketData(CreateBar(100m + i * 10m, i));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        Assert.Empty(signals);
    }

    #endregion

    #region Long Mode Signals

    [Fact]
    public void OnMarketData_EntryConditionMet_EmitsLongSignal()
    {
        var config = CreateSmaCrossoverConfig(fastPeriod: 3, slowPeriod: 5);
        var strategy = new CompositeStrategy(config);

        // Feed enough bars to warm up with a flat price, then a rising price to trigger entry
        var signals = new List<SignalEvent>();
        var dayOffset = 0;

        // Warm up with stable price (both SMAs converge around 100)
        for (var i = 0; i < 10; i++)
        {
            var events = strategy.OnMarketData(CreateBar(100m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        // Now push price up sharply so fast SMA > slow SMA
        for (var i = 0; i < 5; i++)
        {
            var events = strategy.OnMarketData(CreateBar(120m + i * 5m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        Assert.Contains(signals, s => s.Direction == Direction.Long);
    }

    [Fact]
    public void OnMarketData_ExitConditionMet_EmitsFlatSignal()
    {
        var config = CreateSmaCrossoverConfig(fastPeriod: 3, slowPeriod: 5);
        var strategy = new CompositeStrategy(config);

        var signals = new List<SignalEvent>();
        var dayOffset = 0;

        // Warm up with stable price
        for (var i = 0; i < 10; i++)
        {
            strategy.OnMarketData(CreateBar(100m, dayOffset++));
        }

        // Push price up to trigger Long entry
        for (var i = 0; i < 5; i++)
        {
            var events = strategy.OnMarketData(CreateBar(130m + i * 5m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        // Verify we got a Long signal
        Assert.Contains(signals, s => s.Direction == Direction.Long);

        // Now push price down to trigger exit
        signals.Clear();
        for (var i = 0; i < 5; i++)
        {
            var events = strategy.OnMarketData(CreateBar(80m - i * 5m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        Assert.Contains(signals, s => s.Direction == Direction.Flat);
    }

    #endregion

    #region Short Mode Signals

    [Fact]
    public void OnMarketData_ShortMode_EntryEmitsShortSignal()
    {
        var config = CreateSmaCrossoverConfig(fastPeriod: 3, slowPeriod: 5, directionMode: DirectionMode.Short);
        var strategy = new CompositeStrategy(config);

        var signals = new List<SignalEvent>();
        var dayOffset = 0;

        // Warm up with stable price
        for (var i = 0; i < 10; i++)
        {
            strategy.OnMarketData(CreateBar(100m, dayOffset++));
        }

        // Entry condition for Short mode is same expression: sma_fast > sma_slow
        // Push price up to trigger entry
        for (var i = 0; i < 5; i++)
        {
            var events = strategy.OnMarketData(CreateBar(130m + i * 5m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        Assert.Contains(signals, s => s.Direction == Direction.Short);
    }

    [Fact]
    public void OnMarketData_ShortMode_ExitEmitsFlatSignal()
    {
        var config = CreateSmaCrossoverConfig(fastPeriod: 3, slowPeriod: 5, directionMode: DirectionMode.Short);
        var strategy = new CompositeStrategy(config);

        var dayOffset = 0;

        // Warm up
        for (var i = 0; i < 10; i++)
        {
            strategy.OnMarketData(CreateBar(100m, dayOffset++));
        }

        // Trigger Short entry
        for (var i = 0; i < 5; i++)
        {
            strategy.OnMarketData(CreateBar(130m + i * 5m, dayOffset++));
        }

        // Now trigger exit: sma_fast < sma_slow
        var signals = new List<SignalEvent>();
        for (var i = 0; i < 5; i++)
        {
            var events = strategy.OnMarketData(CreateBar(80m - i * 5m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        Assert.Contains(signals, s => s.Direction == Direction.Flat);
    }

    #endregion

    #region State Machine

    [Fact]
    public void OnMarketData_AlreadyInPosition_NoDuplicateEntrySignal()
    {
        var config = CreateSmaCrossoverConfig(fastPeriod: 3, slowPeriod: 5);
        var strategy = new CompositeStrategy(config);

        var signals = new List<SignalEvent>();
        var dayOffset = 0;

        // Warm up
        for (var i = 0; i < 10; i++)
        {
            strategy.OnMarketData(CreateBar(100m, dayOffset++));
        }

        // Push price up to trigger entry and keep it up
        for (var i = 0; i < 10; i++)
        {
            var events = strategy.OnMarketData(CreateBar(130m + i * 2m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        // Should only have one Long signal (no duplicates)
        var longSignals = signals.Where(s => s.Direction == Direction.Long).ToList();
        Assert.Single(longSignals);
    }

    [Fact]
    public void OnMarketData_NotInPosition_NoExitSignal()
    {
        var config = CreateSmaCrossoverConfig(fastPeriod: 3, slowPeriod: 5);
        var strategy = new CompositeStrategy(config);

        var signals = new List<SignalEvent>();
        var dayOffset = 0;

        // Warm up with stable price
        for (var i = 0; i < 10; i++)
        {
            strategy.OnMarketData(CreateBar(100m, dayOffset++));
        }

        // Push price down immediately (exit condition met but never entered)
        for (var i = 0; i < 5; i++)
        {
            var events = strategy.OnMarketData(CreateBar(70m - i * 5m, dayOffset++));
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig) signals.Add(sig);
            }
        }

        // Should have no Flat signals since we never entered a position
        var flatSignals = signals.Where(s => s.Direction == Direction.Flat).ToList();
        Assert.Empty(flatSignals);
    }

    #endregion
}
