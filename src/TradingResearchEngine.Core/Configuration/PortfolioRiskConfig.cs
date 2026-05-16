namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Portfolio-level risk constraints for multi-symbol backtesting.
/// Controls maximum heat, correlation limits, and rebalancing strategy.
/// </summary>
public sealed record PortfolioRiskConfig(
    /// <summary>Maximum total risk across all open positions as a percentage of portfolio equity.</summary>
    decimal MaxPortfolioHeatPercent = 20m,
    /// <summary>Maximum allowed pairwise correlation before blocking new positions (default 0.85).</summary>
    decimal MaxCorrelationAllowed = 0.85m,
    /// <summary>How to weight symbols in the portfolio during equity curve merging.</summary>
    PortfolioRebalanceMode RebalanceMode = PortfolioRebalanceMode.None,
    /// <summary>
    /// Maximum absolute pairwise correlation permitted between a candidate position and any existing
    /// open position. When set, the <c>CorrelationConstraintEnforcer</c> rejects orders that would
    /// violate this threshold. Null means the constraint is inactive.
    /// </summary>
    decimal? MaxPairwiseCorrelation = null,
    /// <summary>
    /// Number of historical bars used to compute the rolling pairwise correlation.
    /// Only relevant when <see cref="MaxPairwiseCorrelation"/> is set. Default is 60 bars.
    /// </summary>
    int CorrelationLookbackBars = 60);
