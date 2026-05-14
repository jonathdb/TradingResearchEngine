namespace TradingResearchEngine.Application.Portfolio;

/// <summary>
/// Portfolio-level health metrics computed from multi-asset backtest results.
/// </summary>
public sealed record PortfolioMetrics(
    /// <summary>Ratio of weighted-average individual volatilities to portfolio volatility. Always >= 1.0 for diversified portfolios.</summary>
    decimal DiversificationRatio,
    /// <summary>Highest off-diagonal value in the correlation matrix.</summary>
    decimal MaxPairwiseCorrelation,
    /// <summary>Annualised portfolio turnover.</summary>
    decimal AnnualisedTurnover,
    /// <summary>Tracking error relative to equal-weight benchmark. Null when not applicable.</summary>
    decimal? TrackingError);
