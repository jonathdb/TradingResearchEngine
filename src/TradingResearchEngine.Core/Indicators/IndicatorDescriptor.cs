namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Describes a technical indicator's metadata for UI discovery and visual composer integration.
/// Contains the indicator's name, description, parameter definitions, and output type.
/// </summary>
/// <param name="Name">The short identifier for the indicator (e.g. "SMA", "EMA").</param>
/// <param name="Description">A human-readable description of what the indicator computes.</param>
/// <param name="Parameters">The list of configurable parameters with their constraints.</param>
/// <param name="OutputType">The name of the output type produced by the indicator (e.g. "decimal", "BollingerBandsOutput").</param>
public sealed record IndicatorDescriptor(
    string Name,
    string Description,
    IReadOnlyList<IndicatorParameterDescriptor> Parameters,
    string OutputType);

/// <summary>
/// Describes a single parameter of a technical indicator, including its type,
/// valid range, and default value.
/// </summary>
/// <param name="Name">The parameter name as used in the indicator constructor.</param>
/// <param name="Type">The CLR type name of the parameter (e.g. "int", "decimal").</param>
/// <param name="Min">The minimum allowed value for the parameter, or null if unbounded.</param>
/// <param name="Max">The maximum allowed value for the parameter, or null if unbounded.</param>
/// <param name="DefaultValue">The default value used when the parameter is not explicitly specified.</param>
public sealed record IndicatorParameterDescriptor(
    string Name,
    string Type,
    object? Min,
    object? Max,
    object DefaultValue);
