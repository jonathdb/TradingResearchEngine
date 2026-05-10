using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research.Results;

/// <summary>A single cell in the parameter sweep grid with multi-metric values.</summary>
public sealed record SweepCell(
    /// <summary>Parameter values for this cell.</summary>
    IReadOnlyDictionary<string, object> Parameters,
    /// <summary>Sharpe ratio for this parameter combination.</summary>
    decimal? SharpeRatio,
    /// <summary>Maximum drawdown for this parameter combination.</summary>
    decimal? MaxDrawdown,
    /// <summary>Win rate for this parameter combination.</summary>
    decimal? WinRate,
    /// <summary>Profit factor for this parameter combination.</summary>
    decimal? ProfitFactor,
    /// <summary>Total number of trades for this parameter combination.</summary>
    int TotalTrades);

/// <summary>Result of a parameter sweep workflow.</summary>
public sealed record SweepResult(
    IReadOnlyList<BacktestResult> Results,
    IReadOnlyList<BacktestResult> RankedBySharpe,
    IReadOnlyDictionary<string, decimal> ParameterSensitivity,
    /// <summary>Grid cells with multi-metric values for heatmap rendering.</summary>
    IReadOnlyList<SweepCell> Cells = null!)
{
    /// <summary>Grid cells with multi-metric values for heatmap rendering.</summary>
    public IReadOnlyList<SweepCell> Cells { get; init; } = Cells ?? Array.Empty<SweepCell>();
}
