namespace TradingResearchEngine.Core.Results;

/// <summary>Side-by-side comparison of multiple backtest results.</summary>
/// <param name="Rows">All comparison rows (unfiltered).</param>
/// <param name="BestBySharpe">Scenario ID with the highest Sharpe ratio.</param>
/// <param name="BestByDrawdown">Scenario ID with the lowest maximum drawdown.</param>
/// <param name="RankedScenarioIds">
/// Optional ordered list of scenario IDs after applying filter and sort criteria.
/// Null when no filter or sort key is specified (default best-of logic only).
/// </param>
public sealed record ComparisonReport(
    IReadOnlyList<ComparisonRow> Rows,
    string BestBySharpe,
    string BestByDrawdown,
    IReadOnlyList<string>? RankedScenarioIds = null);

/// <summary>A single row in a <see cref="ComparisonReport"/>.</summary>
public sealed record ComparisonRow(
    string ScenarioId,
    decimal? SharpeRatio,
    decimal? SortinoRatio,
    decimal? CalmarRatio,
    decimal MaxDrawdown,
    decimal? WinRate,
    decimal? ProfitFactor,
    decimal? Expectancy,
    decimal? EquityCurveSmoothness,
    int MaxConsecutiveLosses,
    int TotalTrades,
    decimal EndEquity);
