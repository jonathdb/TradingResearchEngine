namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Portfolio rebalancing strategy that determines how symbol weights
/// are computed when merging per-symbol equity curves.
/// </summary>
public enum PortfolioRebalanceMode
{
    /// <summary>No rebalancing — simple sum of equity curves.</summary>
    None,

    /// <summary>Equal capital allocation per symbol (1/N weighting).</summary>
    EqualWeight,

    /// <summary>Inverse-volatility weighting — symbols with lower volatility receive higher allocation.</summary>
    VolatilityParity
}
