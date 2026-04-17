namespace TradingResearchEngine.Application.Research.Results;

/// <summary>
/// Result of Combinatorial Purged Cross-Validation (CPCV) study.
/// Contains overfitting probability, performance degradation, and per-combination Sharpe distributions.
/// </summary>
public sealed record CpcvResult(
    /// <summary>Median of the OOS Sharpe distribution across all combinations.</summary>
    decimal MedianOosSharpe,
    /// <summary>Fraction of combinations where OOS Sharpe &lt; IS Sharpe for the same combination.</summary>
    decimal ProbabilityOfOverfitting,
    /// <summary>1 - (MedianOosSharpe / MedianIsSharpe), guarded against division by zero.</summary>
    decimal PerformanceDegradation,
    /// <summary>OOS Sharpe ratio for each C(N,k) combination.</summary>
    IReadOnlyList<decimal> OosSharpeDistribution,
    /// <summary>Total number of C(N,k) combinations evaluated.</summary>
    int TotalCombinations,
    /// <summary>IS Sharpe ratio for each C(N,k) combination (per-combination training set).</summary>
    IReadOnlyList<decimal> IsSharpeDistribution);
