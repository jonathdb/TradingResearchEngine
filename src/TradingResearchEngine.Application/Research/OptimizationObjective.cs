namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Configurable metric used by walk-forward and sweep workflows to rank
/// candidate parameter combinations during in-sample optimization.
/// </summary>
public enum OptimizationObjective
{
    /// <summary>Rank by annualised Sharpe ratio (highest wins).</summary>
    Sharpe,

    /// <summary>Rank by total return percentage: (EndEquity − StartEquity) / StartEquity (highest wins).</summary>
    TotalReturn,

    /// <summary>Rank by MAR ratio — annualised return divided by max drawdown (highest wins).</summary>
    MAR,

    /// <summary>Rank by annualised return normalised by window duration: (EndEquity / StartEquity)^(BarsPerYear / windowBars) − 1 (highest wins).</summary>
    TimeWeightedReturn
}
