using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Application.Strategy.Composite;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Property-based tests for CompositeStrategy warm-up gating behaviour.
/// </summary>
public class CompositeStrategyWarmUpGatingProperties
{
    // Feature: composite-strategy-engine, Property 8: Warm-up gating
    /// <summary>
    /// For any CompositeStrategy configuration with various indicator periods,
    /// no signals are emitted until ALL configured indicators report IsWarm.
    /// **Validates: Requirement 1.6**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property CompositeStrategy_NoSignalsBeforeAllIndicatorsWarm()
    {
        var gen = GenerateConfigWithPeriods();

        return Prop.ForAll(
            gen.ToArbitrary(),
            testCase =>
            {
                var (config, maxPeriod) = testCase;

                var strategy = new CompositeStrategy(config);

                // Feed bars one at a time, tracking signals before warmup
                var signalsBeforeWarm = new List<SignalEvent>();

                // The minimum bars needed before any indicator can be warm is at least maxPeriod.
                // We feed bars up to maxPeriod - 1 and verify no signals are emitted.
                // Use a trending price to maximise the chance of triggering conditions if gating fails.
                for (var i = 0; i < maxPeriod - 1; i++)
                {
                    var price = 100m + i * 2m; // Strongly trending up
                    var bar = new BarEvent(
                        "TEST",
                        "D1",
                        price - 1m,
                        price + 1m,
                        price - 2m,
                        price,
                        1000m,
                        new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(i));

                    var events = strategy.OnMarketData(bar);
                    foreach (var evt in events)
                    {
                        if (evt is SignalEvent sig) signalsBeforeWarm.Add(sig);
                    }
                }

                return (signalsBeforeWarm.Count == 0)
                    .Label($"Expected 0 signals before warmup (maxPeriod={maxPeriod}), got {signalsBeforeWarm.Count}");
            });
    }

    #region Generators

    private static Gen<(CompositeStrategyConfig Config, int MaxPeriod)> GenerateConfigWithPeriods()
    {
        return Gen.OneOf(
            GenerateSingleIndicatorConfig(),
            GenerateDualIndicatorConfig(),
            GenerateTripleIndicatorConfig());
    }

    private static Gen<(CompositeStrategyConfig Config, int MaxPeriod)> GenerateSingleIndicatorConfig()
    {
        return from period in Gen.Choose(5, 30)
               let indicators = new List<IndicatorConfig>
               {
                   new("ind1", "sma", new Dictionary<string, object> { ["period"] = period })
               }
               let config = new CompositeStrategyConfig(
                   "Single Indicator",
                   indicators,
                   "ind1 > 50",
                   "ind1 < 30",
                   DirectionMode.Long)
               select (config, period);
    }

    private static Gen<(CompositeStrategyConfig Config, int MaxPeriod)> GenerateDualIndicatorConfig()
    {
        return from fastPeriod in Gen.Choose(3, 10)
               from slowPeriod in Gen.Choose(12, 30)
               let indicators = new List<IndicatorConfig>
               {
                   new("fast", "sma", new Dictionary<string, object> { ["period"] = fastPeriod }),
                   new("slow", "sma", new Dictionary<string, object> { ["period"] = slowPeriod })
               }
               let config = new CompositeStrategyConfig(
                   "Dual SMA",
                   indicators,
                   "fast > slow",
                   "fast < slow",
                   DirectionMode.Long)
               select (config, slowPeriod);
    }

    private static Gen<(CompositeStrategyConfig Config, int MaxPeriod)> GenerateTripleIndicatorConfig()
    {
        return from smaPeriod in Gen.Choose(5, 15)
               from emaPeriod in Gen.Choose(8, 20)
               from rsiPeriod in Gen.Choose(10, 25)
               // SMA warmup = period, EMA warmup = period, RSI warmup = period + 1
               let maxWarmup = Math.Max(smaPeriod, Math.Max(emaPeriod, rsiPeriod + 1))
               let indicators = new List<IndicatorConfig>
               {
                   new("sma1", "sma", new Dictionary<string, object> { ["period"] = smaPeriod }),
                   new("ema1", "ema", new Dictionary<string, object> { ["period"] = emaPeriod }),
                   new("rsi1", "rsi", new Dictionary<string, object> { ["period"] = rsiPeriod })
               }
               let config = new CompositeStrategyConfig(
                   "Triple Indicator",
                   indicators,
                   "sma1 > ema1 AND rsi1 > 50",
                   "sma1 < ema1 OR rsi1 < 30",
                   DirectionMode.Long)
               select (config, maxWarmup);
    }

    #endregion
}
