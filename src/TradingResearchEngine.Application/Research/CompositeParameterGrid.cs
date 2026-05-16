namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Maps composite strategy indicator IDs to numeric parameter ranges for sweep/walk-forward.
/// Each entry targets a specific indicator within a CompositeStrategyConfig.
/// </summary>
public sealed record CompositeParameterGrid(
    IReadOnlyList<CompositeParameterRange> Ranges);

/// <summary>
/// A single sweep dimension targeting a specific indicator parameter.
/// </summary>
/// <param name="IndicatorId">The unique ID of the indicator within the CompositeStrategyConfig.</param>
/// <param name="ParameterName">The parameter name on the IndicatorConfig to override.</param>
/// <param name="Start">Start of the sweep range (inclusive).</param>
/// <param name="End">End of the sweep range (inclusive).</param>
/// <param name="Step">Step increment between values.</param>
public sealed record CompositeParameterRange(
    string IndicatorId,
    string ParameterName,
    decimal Start,
    decimal End,
    decimal Step);
