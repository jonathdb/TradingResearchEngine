using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>
/// Enriched walk-forward analytics providing OOS profitability rate,
/// concatenated out-of-sample equity curve, and parameter drift metrics.
/// </summary>
public sealed record WalkForwardAnalytics(
    /// <summary>Fraction of OOS windows that are profitable (EndEquity > StartEquity).</summary>
    decimal OosProfitabilityRate,
    /// <summary>Chronologically stitched equity curve combining all OOS window results.</summary>
    IReadOnlyList<EquityCurvePoint> ConcatenatedOosEquityCurve,
    /// <summary>Score quantifying how much optimal parameters change across successive windows. Higher = less stable.</summary>
    decimal ParameterDriftScore,
    /// <summary>Per-window snapshot of selected parameters and their objective values.</summary>
    IReadOnlyList<ParameterWindowSnapshot> ParameterHistory);

/// <summary>
/// Snapshot of the selected parameters and optimization metric for a single walk-forward window.
/// </summary>
public sealed record ParameterWindowSnapshot(
    /// <summary>Zero-based index of the walk-forward window.</summary>
    int WindowIndex,
    /// <summary>The parameter combination selected during in-sample optimization.</summary>
    Dictionary<string, object> Parameters,
    /// <summary>The value of the optimization objective achieved by the selected parameters.</summary>
    decimal ObjectiveValue);
