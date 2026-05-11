using System.Text.Json;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategies.Composite;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Property-based tests for CompositeStrategy and CompositeStrategyConfig.
/// </summary>
public class CompositeStrategyProperties
{
    // Feature: composite-strategy-engine, Property 1: CompositeStrategyConfig JSON round-trip
    /// <summary>
    /// For any valid CompositeStrategyConfig instance, serialising to JSON and deserialising back
    /// produces a semantically equivalent object with all fields preserved.
    /// **Validates: Requirements 2.3, 2.4**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property CompositeStrategyConfig_JsonRoundTrip_PreservesAllFields()
    {
        var gen = GenerateValidCompositeStrategyConfig();

        return Prop.ForAll(
            gen.ToArbitrary(),
            config =>
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                };

                // Serialise to JSON
                var json = JsonSerializer.Serialize(config, options);

                // Deserialise back
                var deserialized = JsonSerializer.Deserialize<CompositeStrategyConfig>(json, options);

                if (deserialized is null)
                    return false.Label("Deserialized config is null");

                // Assert semantic equivalence
                var nameMatch = config.Name == deserialized.Name;
                var directionMatch = config.DirectionMode == deserialized.DirectionMode;
                var entryMatch = config.EntryCondition == deserialized.EntryCondition;
                var exitMatch = config.ExitCondition == deserialized.ExitCondition;
                var indicatorCountMatch = config.Indicators.Count == deserialized.Indicators.Count;

                if (!indicatorCountMatch)
                    return false.Label($"Indicator count mismatch: {config.Indicators.Count} vs {deserialized.Indicators.Count}");

                var indicatorsMatch = true;
                for (var i = 0; i < config.Indicators.Count; i++)
                {
                    var orig = config.Indicators[i];
                    var deser = deserialized.Indicators[i];

                    if (orig.Id != deser.Id || orig.Type != deser.Type)
                    {
                        indicatorsMatch = false;
                        break;
                    }

                    // Compare parameters by re-serialising them
                    var origParams = JsonSerializer.Serialize(orig.Parameters, options);
                    var deserParams = JsonSerializer.Serialize(deser.Parameters, options);
                    if (origParams != deserParams)
                    {
                        indicatorsMatch = false;
                        break;
                    }
                }

                return (nameMatch && directionMatch && entryMatch && exitMatch && indicatorsMatch)
                    .Label($"Name={nameMatch}, Direction={directionMatch}, Entry={entryMatch}, Exit={exitMatch}, Indicators={indicatorsMatch}");
            });
    }

    #region Generators

    private static Gen<CompositeStrategyConfig> GenerateValidCompositeStrategyConfig()
    {
        return from indicatorCount in Gen.Choose(1, 4)
               from indicators in GenerateIndicatorConfigs(indicatorCount)
               from directionMode in Gen.Elements(DirectionMode.Long, DirectionMode.Short, DirectionMode.Both)
               from name in GenerateStrategyName()
               let indicatorIds = indicators.Select(i => i.Id).ToList()
               from entry in GenerateSimpleCondition(indicatorIds)
               from exit in GenerateSimpleCondition(indicatorIds)
               select new CompositeStrategyConfig(name, indicators, entry, exit, directionMode);
    }

    private static Gen<IReadOnlyList<IndicatorConfig>> GenerateIndicatorConfigs(int count)
    {
        return from configs in GenerateSingleIndicatorConfig().ArrayOf(count)
               let uniqueConfigs = MakeIdsUnique(configs.ToList())
               select (IReadOnlyList<IndicatorConfig>)uniqueConfigs;
    }

    private static List<IndicatorConfig> MakeIdsUnique(List<IndicatorConfig> configs)
    {
        var result = new List<IndicatorConfig>();
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < configs.Count; i++)
        {
            var config = configs[i];
            var id = config.Id;
            var suffix = 1;
            while (!usedIds.Add(id))
            {
                id = $"{config.Id}_{suffix++}";
            }
            result.Add(new IndicatorConfig(id, config.Type, config.Parameters));
        }

        return result;
    }

    private static Gen<IndicatorConfig> GenerateSingleIndicatorConfig()
    {
        return Gen.OneOf(
            GenerateSmaIndicator(),
            GenerateEmaIndicator(),
            GenerateRsiIndicator(),
            GenerateAtrIndicator());
    }

    private static Gen<IndicatorConfig> GenerateSmaIndicator()
    {
        return from period in Gen.Choose(5, 50)
               from suffix in Gen.Choose(1, 999)
               let id = $"sma{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "sma", parameters);
    }

    private static Gen<IndicatorConfig> GenerateEmaIndicator()
    {
        return from period in Gen.Choose(5, 50)
               from suffix in Gen.Choose(1, 999)
               let id = $"ema{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "ema", parameters);
    }

    private static Gen<IndicatorConfig> GenerateRsiIndicator()
    {
        return from period in Gen.Choose(5, 30)
               from suffix in Gen.Choose(1, 999)
               let id = $"rsi{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "rsi", parameters);
    }

    private static Gen<IndicatorConfig> GenerateAtrIndicator()
    {
        return from period in Gen.Choose(5, 30)
               from suffix in Gen.Choose(1, 999)
               let id = $"atr{suffix}"
               let parameters = new Dictionary<string, object> { ["period"] = period }
               select new IndicatorConfig(id, "atr", parameters);
    }

    private static Gen<string> GenerateStrategyName()
    {
        return Gen.Elements(
            "Test Strategy Alpha",
            "SMA Crossover",
            "RSI Mean Reversion",
            "Trend Following",
            "Momentum Strategy");
    }

    private static Gen<string> GenerateSimpleCondition(List<string> indicatorIds)
    {
        if (indicatorIds.Count == 0)
            return Gen.Constant("close > 0");

        if (indicatorIds.Count == 1)
        {
            var id = indicatorIds[0];
            return Gen.Elements(
                $"{id} > 50",
                $"{id} < 70",
                $"close > {id}",
                $"{id} > close");
        }

        // Two or more indicators — generate comparisons between them
        return from i in Gen.Choose(0, indicatorIds.Count - 1)
               from j in Gen.Choose(0, indicatorIds.Count - 1).Where(x => x != i)
               from op in Gen.Elements(">", "<")
               select $"{indicatorIds[i]} {op} {indicatorIds[j]}";
    }

    #endregion
}
