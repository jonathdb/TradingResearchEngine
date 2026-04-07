namespace TradingResearchEngine.Core.Results;

/// <summary>Side-by-side comparison of multiple backtest results.</summary>
public sealed record ComparisonReport(
    IReadOnlyList<ComparisonRow> Rows,
    string BestBySharpe,
    string BestByDrawdown);

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
