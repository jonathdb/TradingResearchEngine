using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategies.Composite;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Property-based tests for CompositeStrategy signal equivalence with compiled strategies.
/// </summary>
public class CompositeStrategySignalEquivalenceProperties
{
    // Feature: composite-strategy-engine, Property 5: CompositeStrategy signal equivalence
    /// <summary>
    /// For a CompositeStrategyConfig that encodes the same logic as the compiled
    /// MovingAverageCrossoverStrategy (SMA crossover with matching periods),
    /// the CompositeStrategy produces identical signal sequences on the same bar data
    /// after both strategies are warm.
    /// **Validates: Requirements 10.3, 15.1**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property CompositeStrategy_MatchingSmaLogic_ProducesIdenticalSignals()
    {
        var gen = GenerateTestParameters();

        return Prop.ForAll(
            gen.ToArbitrary(),
            parameters =>
            {
                var (fastPeriod, slowPeriod, prices) = parameters;

                // Configure composite strategy to match the compiled MovingAverageCrossoverStrategy logic.
                // The compiled strategy uses:
                //   Entry: fastValue > slowValue (when not in Long position)
                //   Exit: fastValue <= slowValue (when in Long position)
                // We match this with: entry = "sma_fast > sma_slow", exit = "sma_fast <= sma_slow"
                var compositeConfig = new CompositeStrategyConfig(
                    "SMA Crossover Equivalent",
                    new List<IndicatorConfig>
                    {
                        new("sma_fast", "sma", new Dictionary<string, object> { ["period"] = fastPeriod }),
                        new("sma_slow", "sma", new Dictionary<string, object> { ["period"] = slowPeriod })
                    },
                    "sma_fast > sma_slow",
                    "sma_fast <= sma_slow",
                    DirectionMode.Long);

                var compositeStrategy = new CompositeStrategy(compositeConfig);
                var compiledStrategy = new MovingAverageCrossoverStrategy(fastPeriod, slowPeriod, DirectionMode.Long);

                var compositeSignals = new List<SignalEvent>();
                var compiledSignals = new List<SignalEvent>();

                // Feed the same bars to both strategies
                for (var i = 0; i < prices.Length; i++)
                {
                    var bar = new BarEvent(
                        "TEST",
                        "D1",
                        prices[i] - 1m,
                        prices[i] + 1m,
                        prices[i] - 2m,
                        prices[i],
                        1000m,
                        new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i));

                    var compositeEvents = compositeStrategy.OnMarketData(bar);
                    var compiledEvents = compiledStrategy.OnMarketData(bar);

                    foreach (var evt in compositeEvents)
                    {
                        if (evt is SignalEvent sig) compositeSignals.Add(sig);
                    }

                    foreach (var evt in compiledEvents)
                    {
                        if (evt is SignalEvent sig) compiledSignals.Add(sig);
                    }
                }

                // Compare signal sequences after both are warm
                // Both should produce the same number of signals
                if (compositeSignals.Count != compiledSignals.Count)
                    return false.Label(
                        $"Signal count mismatch: composite={compositeSignals.Count}, compiled={compiledSignals.Count}");

                for (var i = 0; i < compositeSignals.Count; i++)
                {
                    if (compositeSignals[i].Direction != compiledSignals[i].Direction)
                        return false.Label(
                            $"Signal direction mismatch at index {i}: composite={compositeSignals[i].Direction}, compiled={compiledSignals[i].Direction}");

                    if (compositeSignals[i].Timestamp != compiledSignals[i].Timestamp)
                        return false.Label(
                            $"Signal timestamp mismatch at index {i}");
                }

                return true.Label($"Matched {compositeSignals.Count} signals with fast={fastPeriod}, slow={slowPeriod}");
            });
    }

    #region Generators

    private static Gen<(int FastPeriod, int SlowPeriod, decimal[] Prices)> GenerateTestParameters()
    {
        return from fastPeriod in Gen.Choose(3, 8)
               from slowPeriod in Gen.Choose(10, 15)
               from priceCount in Gen.Choose(slowPeriod * 3, slowPeriod * 5)
               from prices in GeneratePriceSequence(priceCount)
               select (fastPeriod, slowPeriod, prices);
    }

    private static Gen<decimal[]> GeneratePriceSequence(int count)
    {
        // Generate a price sequence with trends that will trigger crossovers
        return from basePrice in Gen.Choose(50, 200)
               from steps in Gen.Choose(-30, 30).ArrayOf(count)
               select BuildPriceArray(basePrice, steps);
    }

    private static decimal[] BuildPriceArray(int basePrice, int[] steps)
    {
        var prices = new decimal[steps.Length];
        var current = (decimal)basePrice;

        for (var i = 0; i < steps.Length; i++)
        {
            current += steps[i] * 0.1m;
            // Keep price positive
            if (current < 10m) current = 10m;
            prices[i] = current;
        }

        return prices;
    }

    #endregion
}