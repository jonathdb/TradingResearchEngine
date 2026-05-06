using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Portfolio;

/// <summary>
/// Aggregated result of a multi-symbol portfolio backtest.
/// Contains per-symbol results, a merged portfolio-level result,
/// a pairwise correlation matrix, annualised turnover, and the rebalance mode used.
/// </summary>
public sealed record PortfolioBacktestResult(
    /// <summary>Individual backtest results for each symbol in the portfolio.</summary>
    IReadOnlyList<BacktestResult> SymbolResults,
    /// <summary>Portfolio-level backtest result computed from the merged equity curve.</summary>
    BacktestResult PortfolioResult,
    /// <summary>
    /// N×N Pearson correlation matrix of daily return series.
    /// Outer key is symbol A, inner key is symbol B, value is correlation coefficient.
    /// Diagonal entries are always 1.0.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> CorrelationMatrix,
    /// <summary>Annualised portfolio turnover: (total position changes / months) × 12.</summary>
    decimal AnnualisedTurnover,
    /// <summary>The rebalance mode used for equity curve merging.</summary>
    PortfolioRebalanceMode RebalanceMode);
