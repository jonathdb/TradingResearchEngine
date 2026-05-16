using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Compares multiple BacktestResults side by side, identifying best by Sharpe and best by drawdown.
/// Supports optional multi-criteria filtering and custom sort keys for ranked comparisons.
/// Optionally persists the comparison as a Markdown report via <see cref="IReportExporter"/>.
/// Also supports version-pinned side-by-side comparison of two <see cref="StrategyVersion"/> IDs with metric deltas.
/// </summary>
public sealed class ScenarioComparisonUseCase
{
    private readonly IReportExporter _exporter;
    private readonly IBacktestResultRepository _resultRepo;

    /// <summary>
    /// Initializes a new instance of <see cref="ScenarioComparisonUseCase"/>.
    /// </summary>
    /// <param name="exporter">Report exporter for persisting comparison reports.</param>
    /// <param name="resultRepo">Repository for loading backtest results by strategy version.</param>
    public ScenarioComparisonUseCase(IReportExporter exporter, IBacktestResultRepository resultRepo)
    {
        _exporter = exporter;
        _resultRepo = resultRepo;
    }

    /// <summary>
    /// Builds a <see cref="ComparisonReport"/> from two or more results using default best-of logic.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when fewer than 2 results are supplied.</exception>
    public ComparisonReport Compare(IReadOnlyList<BacktestResult> results)
    {
        return Compare(results, filter: null, sortKey: null);
    }

    /// <summary>
    /// Builds a <see cref="ComparisonReport"/> from two or more results with optional filtering and sorting.
    /// When <paramref name="filter"/> is provided, results that do not meet the filter criteria are excluded
    /// before ranking. When <paramref name="sortKey"/> is provided, filtered survivors are ranked by that metric
    /// instead of the default Sharpe/Drawdown best-of logic.
    /// </summary>
    /// <param name="results">Two or more backtest results to compare.</param>
    /// <param name="filter">Optional filter to narrow candidates before ranking. Null means no filtering.</param>
    /// <param name="sortKey">Optional sort key for ranking filtered survivors. Null uses default best-of logic.</param>
    /// <returns>A comparison report with rows, best-by identifiers, and optional ranked results.</returns>
    /// <exception cref="ArgumentException">Thrown when fewer than 2 results are supplied.</exception>
    public ComparisonReport Compare(
        IReadOnlyList<BacktestResult> results,
        ComparisonFilter? filter,
        ComparisonSortKey? sortKey)
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

        // Default best-of logic (always computed)
        var bestBySharpe = results
            .OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue)
            .First().ScenarioConfig.ScenarioId;

        var bestByDrawdown = results
            .OrderBy(r => r.MaxDrawdown)
            .First().ScenarioConfig.ScenarioId;

        // Apply filter if provided
        IReadOnlyList<string>? rankedScenarioIds = null;
        if (filter is not null || sortKey is not null)
        {
            var survivors = ApplyFilter(results, filter);
            var effectiveSortKey = sortKey ?? ComparisonSortKey.Sharpe;
            var ranked = ApplySort(survivors, effectiveSortKey);
            rankedScenarioIds = ranked
                .Select(r => r.ScenarioConfig.ScenarioId)
                .ToList();
        }

        return new ComparisonReport(rows, bestBySharpe, bestByDrawdown, rankedScenarioIds);
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

    /// <summary>
    /// Builds a <see cref="ComparisonReport"/> with filtering and sorting, then persists it as Markdown.
    /// </summary>
    /// <param name="results">Two or more backtest results to compare.</param>
    /// <param name="filter">Optional filter to narrow candidates before ranking.</param>
    /// <param name="sortKey">Optional sort key for ranking filtered survivors.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The comparison report and the file path where the Markdown was persisted.</returns>
    /// <exception cref="ArgumentException">Thrown when fewer than 2 results are supplied.</exception>
    public async Task<(ComparisonReport Report, string ExportPath)> CompareAndExportAsync(
        IReadOnlyList<BacktestResult> results,
        ComparisonFilter? filter,
        ComparisonSortKey? sortKey,
        CancellationToken ct = default)
    {
        var report = Compare(results, filter, sortKey);
        var path = await _exporter.ExportComparisonMarkdownAsync(report, ct);
        return (report, path);
    }

    /// <summary>
    /// Compares two strategy versions side by side by loading the latest <see cref="BacktestResult"/>
    /// for each version and computing metric deltas. Pinned to specific strategy versions,
    /// distinct from arbitrary BacktestResult comparison.
    /// </summary>
    /// <param name="versionIdA">The first strategy version ID.</param>
    /// <param name="versionIdB">The second strategy version ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="VersionComparisonResult"/> containing both results and their metric deltas.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either version has no completed backtest results.
    /// </exception>
    public async Task<VersionComparisonResult> CompareVersionsAsync(
        string versionIdA,
        string versionIdB,
        CancellationToken ct = default)
    {
        var resultsA = await _resultRepo.ListByVersionAsync(versionIdA, ct);
        var resultsB = await _resultRepo.ListByVersionAsync(versionIdB, ct);

        var latestA = resultsA
            .Where(r => r.Status == BacktestStatus.Completed)
            .OrderByDescending(r => r.CompletedAt)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No completed backtest results found for strategy version '{versionIdA}'.");

        var latestB = resultsB
            .Where(r => r.Status == BacktestStatus.Completed)
            .OrderByDescending(r => r.CompletedAt)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No completed backtest results found for strategy version '{versionIdB}'.");

        var sharpeDelta = (latestA.SharpeRatio, latestB.SharpeRatio) switch
        {
            (not null, not null) => latestB.SharpeRatio - latestA.SharpeRatio,
            _ => (decimal?)null
        };

        var winRateDelta = (latestA.WinRate, latestB.WinRate) switch
        {
            (not null, not null) => latestB.WinRate - latestA.WinRate,
            _ => (decimal?)null
        };

        return new VersionComparisonResult(
            VersionIdA: versionIdA,
            VersionIdB: versionIdB,
            ResultA: latestA,
            ResultB: latestB,
            SharpeDelta: sharpeDelta,
            MaxDrawdownDelta: latestB.MaxDrawdown - latestA.MaxDrawdown,
            WinRateDelta: winRateDelta,
            TotalTradesDelta: latestB.TotalTrades - latestA.TotalTrades,
            EndEquityDelta: latestB.EndEquity - latestA.EndEquity);
    }

    private static IReadOnlyList<BacktestResult> ApplyFilter(
        IReadOnlyList<BacktestResult> results,
        ComparisonFilter? filter)
    {
        if (filter is null)
            return results;

        IEnumerable<BacktestResult> filtered = results;

        if (filter.MinWinRate is not null)
            filtered = filtered.Where(r => r.WinRate is not null && r.WinRate >= filter.MinWinRate);

        if (filter.MinTrades is not null)
            filtered = filtered.Where(r => r.TotalTrades >= filter.MinTrades);

        if (filter.MaxDrawdown is not null)
            filtered = filtered.Where(r => r.MaxDrawdown <= filter.MaxDrawdown);

        return filtered.ToList();
    }

    private static IReadOnlyList<BacktestResult> ApplySort(
        IReadOnlyList<BacktestResult> results,
        ComparisonSortKey sortKey)
    {
        return sortKey switch
        {
            ComparisonSortKey.Sharpe => results
                .OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue)
                .ToList(),
            ComparisonSortKey.Calmar => results
                .OrderByDescending(r => r.CalmarRatio ?? decimal.MinValue)
                .ToList(),
            ComparisonSortKey.Sortino => results
                .OrderByDescending(r => r.SortinoRatio ?? decimal.MinValue)
                .ToList(),
            ComparisonSortKey.ProfitFactor => results
                .OrderByDescending(r => r.ProfitFactor ?? decimal.MinValue)
                .ToList(),
            ComparisonSortKey.WinRate => results
                .OrderByDescending(r => r.WinRate ?? decimal.MinValue)
                .ToList(),
            ComparisonSortKey.MaxDrawdown => results
                .OrderBy(r => r.MaxDrawdown)
                .ToList(),
            _ => results.ToList()
        };
    }
}
