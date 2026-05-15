namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Configurable metric used by walk-forward and sweep workflows to rank
/// candidate parameter combinations during in-sample optimization.
/// </summary>
public enum OptimizationObjective
{
    /// <summary>Rank by annualised Sharpe ratio (highest wins).</summary>
    Sharpe,

    /// <summary>Rank by Compound Annual Growth Rate (highest wins).</summary>
    CAGR,

    /// <summary>Rank by MAR ratio — CAGR divided by max drawdown (highest wins).</summary>
    MAR
}
