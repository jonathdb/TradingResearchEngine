namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of parameter stability analysis around a target parameter set.</summary>
public sealed record ParameterStabilityResult(
    decimal LocalMedianSharpe,
    decimal LocalWorstSharpe,
    decimal ProfitableNeighbourProportion,
    decimal FragilityScore);

/// <summary>Options for parameter stability analysis.</summary>
public sealed class ParameterStabilityOptions
{
    /// <summary>Percentage range around target values to consider as neighbours (default 10%).</summary>
    public decimal NeighbourhoodPercent { get; set; } = 10m;
}
