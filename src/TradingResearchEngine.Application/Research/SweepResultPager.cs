using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Provides paged, filtered, and sorted access to parameter sweep results.
/// Avoids loading all results into the DOM simultaneously for large sweeps.
/// </summary>
public sealed class SweepResultPager
{
    private readonly IReadOnlyList<BacktestResult> _allResults;

    /// <summary>
    /// Initializes a new pager over the given sweep results.
    /// </summary>
    /// <param name="results">The full set of sweep results to page over.</param>
    public SweepResultPager(IReadOnlyList<BacktestResult> results)
    {
        _allResults = results ?? throw new ArgumentNullException(nameof(results));
    }

    /// <summary>
    /// Total number of results before filtering.
    /// </summary>
    public int TotalCount => _allResults.Count;

    /// <summary>
    /// Returns a paged subset of sweep results with server-side filtering and sorting applied.
    /// </summary>
    /// <param name="request">The paging, filtering, and sorting parameters.</param>
    /// <returns>A <see cref="PagedResult{T}"/> containing the requested page of results.</returns>
    public PagedResult<BacktestResult> GetPage(SweepPageRequest request)
    {
        var filtered = ApplyFilter(_allResults, request.Filter);
        var sorted = ApplySort(filtered, request.SortBy, request.SortDescending);

        var totalCount = sorted.Count;
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 500);

        var items = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResult<BacktestResult>(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Provides items for Blazor Virtualize component via ItemsProvider delegate.
    /// Applies current filter and sort, then returns the requested range.
    /// </summary>
    /// <param name="filter">Optional filter text to apply.</param>
    /// <param name="sortBy">Metric to sort by.</param>
    /// <param name="sortDescending">Whether to sort descending.</param>
    /// <param name="startIndex">Zero-based start index.</param>
    /// <param name="count">Number of items to return.</param>
    /// <returns>The items in the requested range and total item count.</returns>
    public (IReadOnlyList<BacktestResult> Items, int TotalCount) GetRange(
        string? filter,
        SweepSortMetric sortBy,
        bool sortDescending,
        int startIndex,
        int count)
    {
        var filtered = ApplyFilter(_allResults, filter);
        var sorted = ApplySort(filtered, sortBy, sortDescending);

        var items = sorted
            .Skip(startIndex)
            .Take(count)
            .ToList();

        return (items, sorted.Count);
    }

    private static IReadOnlyList<BacktestResult> ApplyFilter(
        IReadOnlyList<BacktestResult> results, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return results;

        var filterLower = filter.Trim().ToLowerInvariant();

        return results.Where(r =>
        {
            // Filter by scenario ID
            if (r.ScenarioConfig.ScenarioId?.ToLowerInvariant().Contains(filterLower) == true)
                return true;

            // Filter by parameter values
            if (r.ScenarioConfig.StrategyParameters is not null)
            {
                foreach (var kvp in r.ScenarioConfig.StrategyParameters)
                {
                    if (kvp.Key.ToLowerInvariant().Contains(filterLower))
                        return true;
                    if (kvp.Value?.ToString()?.ToLowerInvariant().Contains(filterLower) == true)
                        return true;
                }
            }

            return false;
        }).ToList();
    }

    private static IReadOnlyList<BacktestResult> ApplySort(
        IReadOnlyList<BacktestResult> results, SweepSortMetric sortBy, bool descending)
    {
        var ordered = sortBy switch
        {
            SweepSortMetric.MaxDrawdown => descending
                ? results.OrderByDescending(r => r.MaxDrawdown)
                : results.OrderBy(r => r.MaxDrawdown),
            SweepSortMetric.ProfitFactor => descending
                ? results.OrderByDescending(r => r.ProfitFactor ?? decimal.MinValue)
                : results.OrderBy(r => r.ProfitFactor ?? decimal.MinValue),
            SweepSortMetric.WinRate => descending
                ? results.OrderByDescending(r => r.WinRate ?? decimal.MinValue)
                : results.OrderBy(r => r.WinRate ?? decimal.MinValue),
            SweepSortMetric.CalmarRatio => descending
                ? results.OrderByDescending(r => r.CalmarRatio ?? decimal.MinValue)
                : results.OrderBy(r => r.CalmarRatio ?? decimal.MinValue),
            _ => descending
                ? results.OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue)
                : results.OrderBy(r => r.SharpeRatio ?? decimal.MinValue)
        };

        return ordered.ToList();
    }
}

/// <summary>
/// Request parameters for paged sweep result retrieval.
/// </summary>
public sealed record SweepPageRequest
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Number of items per page (clamped to 1–500).</summary>
    public int PageSize { get; init; } = 50;

    /// <summary>Metric to sort by.</summary>
    public SweepSortMetric SortBy { get; init; } = SweepSortMetric.SharpeRatio;

    /// <summary>Whether to sort descending (true) or ascending (false).</summary>
    public bool SortDescending { get; init; } = true;

    /// <summary>Optional text filter applied to scenario ID and parameter values.</summary>
    public string? Filter { get; init; }
}
