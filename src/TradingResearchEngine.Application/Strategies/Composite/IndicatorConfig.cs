using System.Text.Json.Serialization;

namespace TradingResearchEngine.Application.Strategies.Composite;

/// <summary>
/// Declarative specification of an indicator instance within a composite strategy.
/// </summary>
/// <param name="Id">Unique ID used to reference this indicator in conditions (e.g., "sma20").</param>
/// <param name="Type">Indicator type matching a known type: sma, ema, rsi, macd, bollinger, atr, stochastic, donchian.</param>
/// <param name="Parameters">Parameters for the indicator (e.g., {"period": 20}). Supports polymorphic dictionary values.</param>
public sealed record IndicatorConfig(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("type")]
    string Type,
    [property: JsonPropertyName("parameters")]
    IReadOnlyDictionary<string, object>? Parameters);
