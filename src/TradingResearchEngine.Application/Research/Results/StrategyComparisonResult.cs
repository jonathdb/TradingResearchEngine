namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Comparison of multiple strategies under matched execution assumptions.</summary>
public sealed record StrategyComparisonResult(
    IReadOnlyList<StrategyComparisonRow> Rows,
    IReadOnlyList<string>? MismatchWarnings);

/// <summary>A single strategy's metrics in a comparison.</summary>
public sealed record StrategyComparisonRow(
    string StrategyName,
    decimal CAGR,
    decimal? Sharpe,
    decimal? Sortino,
    decimal MaxDrawdown,
    decimal? ProfitFactor,
    decimal? Expectancy,
    decimal? RecoveryFactor);
