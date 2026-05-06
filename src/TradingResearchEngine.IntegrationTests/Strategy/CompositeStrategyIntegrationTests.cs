using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Application.Strategy.Composite;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.IntegrationTests.Strategy;

/// <summary>
/// Integration tests for CompositeStrategy verifying signal equivalence with compiled strategies,
/// RSI-based entry/exit behaviour, and deterministic replay.
/// Requirements: 15.1, 15.2, 15.3
/// </summary>
public class CompositeStrategyIntegrationTests
{
    #region Helpers

    private static BarEvent CreateBar(decimal close, int dayOffset)
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
    /// Generates a synthetic price series that produces SMA crossovers.
    /// Starts flat, trends up (fast crosses above slow), then trends down (fast crosses below slow).
    /// </summary>
    private static List<BarEvent> GenerateSmaCrossoverData(int count = 120)
    {
        var bars = new List<BarEvent>();
        for (int i = 0; i < count; i++)
        {
            decimal price;
            if (i < 40)
                price = 100m; // Flat period for warm-up
            else if (i < 80)
                price = 100m + (i - 40) * 2m; // Rising trend
            else
                price = 100m + 80m - (i - 80) * 3m; // Falling trend
            bars.Add(CreateBar(price, i));
        }
        return bars;
    }

    /// <summary>
    /// Generates a synthetic price series that produces RSI extremes.
    /// Alternates between strong uptrends (RSI > 70) and strong downtrends (RSI &lt; 30).
    /// </summary>
    private static List<BarEvent> GenerateRsiData(int count = 100)
    {
        var bars = new List<BarEvent>();
        decimal price = 100m;
        for (int i = 0; i < count; i++)
        {
            if (i < 20)
                price = 100m; // Flat warm-up
            else if (i < 40)
                price += 5m; // Strong uptrend → high RSI
            else if (i < 60)
                price -= 5m; // Strong downtrend → low RSI
            else if (i < 80)
                price += 4m; // Another uptrend
            else
                price -= 4m; // Another downtrend

            bars.Add(CreateBar(price, i));
        }
        return bars;
    }

    private static List<SignalEvent> FeedBarsToStrategy(
        TradingResearchEngine.Core.Strategy.IStrategy strategy,
        List<BarEvent> bars)
    {
        var signals = new List<SignalEvent>();
        foreach (var bar in bars)
        {
            var events = strategy.OnMarketData(bar);
            foreach (var evt in events)
            {
                if (evt is SignalEvent sig)
                    signals.Add(sig);
            }
        }
        return signals;
    }

    #endregion

    #region SMA Crossover Equivalence (Requirement 15.1)

    /// <summary>
    /// Verifies that a composite SMA crossover strategy produces identical signal directions
    /// to the compiled MovingAverageCrossoverStrategy when fed the same bar data.
    /// </summary>
    [Fact]
    public void SmaCrossover_CompositeProducesIdenticalSignals_ToCompiledStrategy()
    {
        // Arrange
        const int fastPeriod = 10;
        const int slowPeriod = 30;

        var compiledStrategy = new MovingAverageCrossoverStrategy(
            fastPeriod: fastPeriod,
            slowPeriod: slowPeriod,
            directionMode: DirectionMode.Long);

        var compositeConfig = new CompositeStrategyConfig(
            "SMA Crossover Composite",
            new List<IndicatorConfig>
            {
                new("sma_fast", "sma", new Dictionary<string, object> { ["period"] = fastPeriod }),
                new("sma_slow", "sma", new Dictionary<string, object> { ["period"] = slowPeriod })
            },
            "sma_fast > sma_slow",
            "sma_fast < sma_slow",
            DirectionMode.Long);

        var compositeStrategy = new CompositeStrategy(compositeConfig);

        var bars = GenerateSmaCrossoverData(count: 120);

        // Act
        var compiledSignals = FeedBarsToStrategy(compiledStrategy, bars);
        var compositeSignals = FeedBarsToStrategy(compositeStrategy, bars);

        // Assert — both should produce at least one signal
        Assert.NotEmpty(compiledSignals);
        Assert.NotEmpty(compositeSignals);

        // Both should produce the same number of signals
        Assert.Equal(compiledSignals.Count, compositeSignals.Count);

        // Each signal should have the same direction and timestamp
        for (int i = 0; i < compiledSignals.Count; i++)
        {
            Assert.Equal(compiledSignals[i].Direction, compositeSignals[i].Direction);
            Assert.Equal(compiledSignals[i].Timestamp, compositeSignals[i].Timestamp);
        }
    }

    #endregion

    #region RSI-Based Entry/Exit (Requirement 15.2)

    /// <summary>
    /// Verifies that a composite RSI-based strategy produces expected entry signals
    /// when RSI crosses below 30 (oversold entry) and exit signals when RSI crosses above 70.
    /// </summary>
    [Fact]
    public void RsiStrategy_CompositeProducesExpectedSignals_OnKnownDataset()
    {
        // Arrange — RSI entry when < 30, exit when > 70
        var compositeConfig = new CompositeStrategyConfig(
            "RSI Composite",
            new List<IndicatorConfig>
            {
                new("rsi14", "rsi", new Dictionary<string, object> { ["period"] = 14 })
            },
            "rsi14 < 30",
            "rsi14 > 70",
            DirectionMode.Long);

        var compositeStrategy = new CompositeStrategy(compositeConfig);
        var bars = GenerateRsiData(count: 100);

        // Act
        var signals = FeedBarsToStrategy(compositeStrategy, bars);

        // Assert — should produce at least one entry and one exit signal
        Assert.NotEmpty(signals);

        // Verify signal directions alternate correctly (Long entry, Flat exit)
        for (int i = 0; i < signals.Count; i++)
        {
            if (i % 2 == 0)
                Assert.Equal(Direction.Long, signals[i].Direction);
            else
                Assert.Equal(Direction.Flat, signals[i].Direction);
        }
    }

    #endregion

    #region Determinism (Requirement 15.3)

    /// <summary>
    /// Verifies that running the same composite strategy configuration with the same data
    /// produces identical results on every execution.
    /// </summary>
    [Fact]
    public void CompositeStrategy_SameConfigAndData_ProducesDeterministicResults()
    {
        // Arrange
        var config = new CompositeStrategyConfig(
            "Determinism Test",
            new List<IndicatorConfig>
            {
                new("sma_fast", "sma", new Dictionary<string, object> { ["period"] = 5 }),
                new("sma_slow", "sma", new Dictionary<string, object> { ["period"] = 20 })
            },
            "crosses_above(sma_fast, sma_slow)",
            "crosses_below(sma_fast, sma_slow)",
            DirectionMode.Long);

        var bars = GenerateSmaCrossoverData(count: 100);

        // Act — run twice with fresh strategy instances
        var strategy1 = new CompositeStrategy(config);
        var signals1 = FeedBarsToStrategy(strategy1, bars);

        var strategy2 = new CompositeStrategy(config);
        var signals2 = FeedBarsToStrategy(strategy2, bars);

        // Assert — identical signal count, directions, and timestamps
        Assert.Equal(signals1.Count, signals2.Count);

        for (int i = 0; i < signals1.Count; i++)
        {
            Assert.Equal(signals1[i].Direction, signals2[i].Direction);
            Assert.Equal(signals1[i].Timestamp, signals2[i].Timestamp);
            Assert.Equal(signals1[i].Symbol, signals2[i].Symbol);
            Assert.Equal(signals1[i].Strength, signals2[i].Strength);
        }
    }

    #endregion
}
