using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategy.Composite;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Property-based tests for the IndicatorFactory completeness.
/// **Validates: Requirements 3.1, 3.2**
/// </summary>
public class IndicatorFactoryProperties
{
    // Feature: composite-strategy-engine, Property 4: Indicator factory completeness
    /// <summary>
    /// For any indicator type in {sma, ema, rsi, macd, bollinger, atr, stochastic, donchian}
    /// with valid parameters, the factory returns a non-null instance that becomes warm
    /// after sufficient bars are added.
    /// **Validates: Requirements 3.1, 3.2**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property IndicatorFactory_AnyValidType_ReturnsNonNullInstanceThatBecomesWarm()
    {
        var gen = GenerateValidIndicatorConfig();

        return Prop.ForAll(
            gen.ToArbitrary(),
            config =>
            {
                // Factory should return a non-null instance
                var instance = IndicatorFactory.Create(config);
                if (instance is null)
                    return false.Label("Factory returned null");

                // Instance should not be warm initially
                // Feed enough bars to make it warm
                var barsNeeded = GetWarmupBarsNeeded(config);
                for (var i = 0; i < barsNeeded; i++)
                {
                    var bar = CreateBar(i);
                    instance.Add(bar);
                }

                return instance.IsWarm
                    .Label($"Type={config.Type}, Id={config.Id}, BarsAdded={barsNeeded}, IsWarm={instance.IsWarm}");
            });
    }

    #region Generators

    private static Gen<IndicatorConfig> GenerateValidIndicatorConfig()
    {
        return Gen.OneOf(
            GenerateSmaConfig(),
            GenerateEmaConfig(),
            GenerateRsiConfig(),
            GenerateMacdConfig(),
            GenerateBollingerConfig(),
            GenerateAtrConfig(),
            GenerateStochasticConfig(),
            GenerateDonchianConfig());
    }

    private static Gen<IndicatorConfig> GenerateSmaConfig()
    {
        return from period in Gen.Choose(2, 50)
               from suffix in Gen.Choose(1, 100)
               let id = $"sma{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "sma", parameters);
    }

    private static Gen<IndicatorConfig> GenerateEmaConfig()
    {
        return from period in Gen.Choose(2, 50)
               from suffix in Gen.Choose(1, 100)
               let id = $"ema{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "ema", parameters);
    }

    private static Gen<IndicatorConfig> GenerateRsiConfig()
    {
        return from period in Gen.Choose(2, 50)
               from suffix in Gen.Choose(1, 100)
               let id = $"rsi{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "rsi", parameters);
    }

    private static Gen<IndicatorConfig> GenerateMacdConfig()
    {
        return from fast in Gen.Choose(2, 20)
               from slow in Gen.Choose(21, 50)
               from signal in Gen.Choose(2, 15)
               from suffix in Gen.Choose(1, 100)
               let id = $"macd{suffix}"
               let parameters = new Dictionary<string, object>
               {
                   ["fastPeriod"] = fast,
                   ["slowPeriod"] = slow,
                   ["signalPeriod"] = signal
               }
               select new IndicatorConfig(id, "macd", parameters);
    }

    private static Gen<IndicatorConfig> GenerateBollingerConfig()
    {
        return from period in Gen.Choose(5, 50)
               from stdDevTenths in Gen.Choose(10, 30)
               from suffix in Gen.Choose(1, 100)
               let id = $"bb{suffix}"
               let stdDev = stdDevTenths / 10.0
               let parameters = new Dictionary<string, object>
               {
                   ["period"] = period,
                   ["standardDeviations"] = stdDev
               }
               select new IndicatorConfig(id, "bollinger", parameters);
    }

    private static Gen<IndicatorConfig> GenerateAtrConfig()
    {
        return from period in Gen.Choose(2, 50)
               from suffix in Gen.Choose(1, 100)
               let id = $"atr{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "atr", parameters);
    }

    private static Gen<IndicatorConfig> GenerateStochasticConfig()
    {
        return from lookback in Gen.Choose(5, 30)
               from signal in Gen.Choose(2, 10)
               from smooth in Gen.Choose(2, 10)
               from suffix in Gen.Choose(1, 100)
               let id = $"stoch{suffix}"
               let parameters = new Dictionary<string, object>
               {
                   ["lookbackPeriod"] = lookback,
                   ["signalPeriod"] = signal,
                   ["smoothPeriod"] = smooth
               }
               select new IndicatorConfig(id, "stochastic", parameters);
    }

    private static Gen<IndicatorConfig> GenerateDonchianConfig()
    {
        return from period in Gen.Choose(2, 50)
               from suffix in Gen.Choose(1, 100)
               let id = $"dc{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "donchian", parameters);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Determines how many bars are needed to make an indicator warm based on its config.
    /// Uses a generous multiplier to ensure warmup completes.
    /// </summary>
    private static int GetWarmupBarsNeeded(IndicatorConfig config)
    {
        var type = config.Type.ToLowerInvariant();
        var parameters = config.Parameters ?? new Dictionary<string, object>();

        return type switch
        {
            "sma" => GetIntParam(parameters, "period", 20) * 2,
            "ema" => GetIntParam(parameters, "period", 20) * 2,
            "rsi" => GetIntParam(parameters, "period", 14) * 2 + 10,
            "macd" => (GetIntParam(parameters, "slowPeriod", 26) + GetIntParam(parameters, "signalPeriod", 9)) * 2,
            "bollinger" => GetIntParam(parameters, "period", 20) * 2,
            "atr" => GetIntParam(parameters, "period", 14) * 2 + 5,
            "stochastic" => (GetIntParam(parameters, "lookbackPeriod", 14) +
                             GetIntParam(parameters, "signalPeriod", 3) +
                             GetIntParam(parameters, "smoothPeriod", 3)) * 2,
            "donchian" => GetIntParam(parameters, "period", 20) * 2,
            _ => 100
        };
    }

    private static int GetIntParam(IReadOnlyDictionary<string, object> parameters, string key, int defaultValue)
    {
        if (parameters.TryGetValue(key, out var value))
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                _ => defaultValue
            };
        }
        return defaultValue;
    }

    private static BarRecord CreateBar(int index)
    {
        // Generate realistic-looking price data with some variation
        var basePrice = 100m + index * 0.1m;
        var variation = (index % 7) * 0.5m;
        return new BarRecord(
            Symbol: "TEST",
            Interval: "D1",
            Open: basePrice,
            High: basePrice + variation + 2m,
            Low: basePrice - variation - 1m,
            Close: basePrice + variation,
            Volume: 1000m + index * 10m,
            Timestamp: new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(index));
    }

    #endregion
}
