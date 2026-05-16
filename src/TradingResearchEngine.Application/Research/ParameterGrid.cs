namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Defines a grid of parameter ranges for walk-forward in-sample optimization.
/// Each range specifies a parameter name, start/end bounds, and step size.
/// </summary>
public sealed record ParameterGrid(
    IReadOnlyList<ParameterRange> Ranges);

/// <summary>
/// A single parameter dimension within a <see cref="ParameterGrid"/>.
/// Defines the sweep range [Start, End] with the given Step increment.
/// </summary>
public sealed record ParameterRange(
    string Name,
    decimal Start,
    decimal End,
    decimal Step);
