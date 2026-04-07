using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Compares multiple BacktestResults side by side, identifying best by Sharpe and best by drawdown.
/// </summary>
public sealed class ScenarioComparisonUseCase
{
    /// <summary>
    /// Builds a <see cref="ComparisonReport"/> from two or more results.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when fewer than 2 results are supplied.</exception>
    public ComparisonReport Compare(IReadOnlyList<BacktestResult> results)
    {
        if (results.Count < 2)
            throw new ArgumentException("At least 2 BacktestResult instances are required for comparison.", nameof(results));

        var rows = results.Select(r => new ComparisonRow(
            r.ScenarioConfig.ScenarioId,
            r.SharpeRatio,
            r.SortinoRatio,
            r.CalmarRatio,
            r.MaxDrawdown,
            r.WinRate,
            r.ProfitFactor,
            r.Expectancy,
            r.EquityCurveSmoothness,
            r.MaxConsecutiveLosses,
            r.TotalTrades,
            r.EndEquity)).ToList();

        var bestBySharpe = results
            .OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue)
            .First().ScenarioConfig.ScenarioId;

        var bestByDrawdown = results
            .OrderBy(r => r.MaxDrawdown)
            .First().ScenarioConfig.ScenarioId;

        return new ComparisonReport(rows, bestBySharpe, bestByDrawdown);
    }
}
