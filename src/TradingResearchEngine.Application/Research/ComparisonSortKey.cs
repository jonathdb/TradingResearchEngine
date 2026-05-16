namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Metric used to rank filtered comparison survivors.
/// </summary>
public enum ComparisonSortKey
{
    /// <summary>Rank by Sharpe ratio (descending). This is the default.</summary>
    Sharpe,

    /// <summary>Rank by Calmar ratio (descending).</summary>
    Calmar,

    /// <summary>Rank by Sortino ratio (descending).</summary>
    Sortino,

    /// <summary>Rank by profit factor (descending).</summary>
    ProfitFactor,

    /// <summary>Rank by win rate (descending).</summary>
    WinRate,

    /// <summary>Rank by maximum drawdown (ascending — lower is better).</summary>
    MaxDrawdown
}
