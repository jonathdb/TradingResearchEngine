using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Compares multiple BacktestResults side by side, identifying best by Sharpe and best by drawdown.
/// Optionally persists the comparison as a Markdown report via <see cref="IReportExporter"/>.
/// </summary>
public sealed class ScenarioComparisonUseCase
{
    private readonly IReportExporter _exporter;

    /// <summary>
    /// Initializes a new instance of <see cref="ScenarioComparisonUseCase"/>.
    /// </summary>
    /// <param name="exporter">Report exporter for persisting comparison reports.</param>
    public ScenarioComparisonUseCase(IReportExporter exporter)
    {
        _exporter = exporter;
    }

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

    /// <summary>
    /// Builds a <see cref="ComparisonReport"/> from two or more results and persists it as Markdown.
    /// </summary>
    /// <param name="results">Two or more backtest results to compare.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The comparison report and the file path where the Markdown was persisted.</returns>
    /// <exception cref="ArgumentException">Thrown when fewer than 2 results are supplied.</exception>
    public async Task<(ComparisonReport Report, string ExportPath)> CompareAndExportAsync(
        IReadOnlyList<BacktestResult> results,
        CancellationToken ct = default)
    {
        var report = Compare(results);
        var path = await _exporter.ExportComparisonMarkdownAsync(report, ct);
        return (report, path);
    }
}
