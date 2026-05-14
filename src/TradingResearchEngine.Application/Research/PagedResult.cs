namespace TradingResearchEngine.Application.Research;

/// <summary>
/// A paginated query result containing a page of items and total count metadata.
/// Used by <c>IBacktestResultRepository.ListPagedAsync</c> and <c>IStudyRepository.ListPagedAsync</c>
/// to avoid loading entire datasets into memory.
/// </summary>
/// <typeparam name="T">The type of items in the page.</typeparam>
/// <param name="Items">The items on the current page.</param>
/// <param name="TotalCount">Total number of items matching the query (across all pages).</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The maximum number of items per page.</param>
/// <remarks>
/// Safe operating limits for in-memory JSON repository pagination:
/// - Up to 5000 records: &lt; 100ms per query (linear scan + skip/take)
/// - 5000–10000 records: 100–300ms (acceptable for research workloads)
/// - Beyond 10000 records: consider SQLite index for O(log n) lookups
/// </remarks>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    /// <summary>
    /// Total number of pages. Returns 0 when <see cref="PageSize"/> is 0 to avoid division by zero.
    /// </summary>
    public int TotalPages => PageSize > 0
        ? (int)Math.Ceiling((double)TotalCount / PageSize)
        : 0;
}
