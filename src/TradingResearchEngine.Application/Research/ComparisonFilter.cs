namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Filters comparison candidates by minimum thresholds before ranking.
/// All criteria are optional; only non-null values are enforced.
/// </summary>
/// <param name="MinWinRate">Minimum win rate threshold (e.g., 0.5 for 50%). Results below this are excluded.</param>
/// <param name="MinTrades">Minimum total trade count. Results with fewer trades are excluded.</param>
/// <param name="MaxDrawdown">Maximum drawdown threshold (e.g., 0.20 for 20%). Results exceeding this are excluded.</param>
public sealed record ComparisonFilter(
    decimal? MinWinRate = null,
    int? MinTrades = null,
    decimal? MaxDrawdown = null);
