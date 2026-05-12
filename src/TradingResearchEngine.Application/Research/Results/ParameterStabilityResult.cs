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

/// <summary>
/// Result of parameter stability analysis showing stability scores per cell.
/// </summary>
public sealed record ParameterGridStabilityResult(
    IReadOnlyList<ParameterStabilityCell> Cells,
    IReadOnlyList<StabilityZone> StabilityZones,
    double StabilityThreshold);

/// <summary>A single cell in the parameter grid with its stability score.</summary>
public sealed record ParameterStabilityCell(
    Dictionary<string, object> ParameterValues,
    decimal MetricValue,
    double StabilityScore);

/// <summary>A contiguous region of stable parameter values.</summary>
public sealed record StabilityZone(
    IReadOnlyList<ParameterStabilityCell> Cells,
    Dictionary<string, object> CenterParameterValues,
    decimal CenterMetricValue);
