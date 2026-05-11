using System.Text.Json.Serialization;

namespace TradingResearchEngine.Application.Strategies.Composite;

/// <summary>
/// Immutable configuration record for a composite strategy.
/// Serialisable to/from JSON using System.Text.Json without data loss.
/// </summary>
/// <param name="Name">Human-readable name for this composite strategy.</param>
/// <param name="Indicators">Ordered list of indicator definitions.</param>
/// <param name="EntryCondition">Entry condition expression string.</param>
/// <param name="ExitCondition">Exit condition expression string.</param>
/// <param name="DirectionMode">Direction mode: Long, Short, or Both. Default Long.</param>
public sealed record CompositeStrategyConfig(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("indicators")]
    IReadOnlyList<IndicatorConfig> Indicators,
    [property: JsonPropertyName("entryCondition")]
    string EntryCondition,
    [property: JsonPropertyName("exitCondition")]
    string ExitCondition,
    [property: JsonPropertyName("directionMode")]
    [property: JsonConverter(typeof(JsonStringEnumConverter<DirectionMode>))]
    DirectionMode DirectionMode = DirectionMode.Long);
