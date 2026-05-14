using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Custom JSON converter for <see cref="StrategyTypeId"/> that reads/writes
/// a plain string token. This ensures backward compatibility — existing JSON
/// files with <c>"StrategyType": "moving-average-crossover"</c> deserialise
/// directly into the value object without a wrapper object.
/// </summary>
public sealed class StrategyTypeIdJsonConverter : JsonConverter<StrategyTypeId>
{
    /// <inheritdoc/>
    public override StrategyTypeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return new StrategyTypeId(value ?? string.Empty);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, StrategyTypeId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Value);
    }
}
