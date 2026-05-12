using System.Text.Json.Serialization;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Strongly-typed identifier for strategy types, replacing raw string usage
/// at the Application boundary. Serialises to/from a plain JSON string for
/// backward compatibility with existing persisted data.
/// </summary>
/// <remarks>
/// Core layer (<see cref="TradingResearchEngine.Core.Configuration.ScenarioConfig"/>)
/// retains <c>string StrategyType</c> to avoid an upward dependency. This wrapper
/// is used in Application-layer contracts: <see cref="StrategyIdentity"/>,
/// <see cref="StrategyRegistry"/>, and repository filter parameters.
/// </remarks>
[JsonConverter(typeof(StrategyTypeIdJsonConverter))]
public readonly record struct StrategyTypeId(string Value)
{
    /// <summary>Returns the underlying string value.</summary>
    public override string ToString() => Value;

    /// <summary>Implicit conversion from <see cref="string"/> to <see cref="StrategyTypeId"/>.</summary>
    public static implicit operator StrategyTypeId(string value) => new(value);

    /// <summary>Implicit conversion from <see cref="StrategyTypeId"/> to <see cref="string"/>.</summary>
    public static implicit operator string(StrategyTypeId id) => id.Value;
}
