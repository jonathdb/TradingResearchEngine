using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Extended persistence interface for backtest results with indexed queries.
/// V6: Adds version- and strategy-scoped lookups backed by SQLite index.
/// </summary>
public interface IBacktestResultRepository : IRepository<BacktestResult>
{
    /// <summary>Lists results for a specific strategy version via SQLite index.</summary>
    Task<IReadOnlyList<BacktestResult>> ListByVersionAsync(
        string versionId, CancellationToken ct = default);

    /// <summary>Lists results for a specific strategy via SQLite index.</summary>
    Task<IReadOnlyList<BacktestResult>> ListByStrategyAsync(
        string strategyId, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the most recent backtest runs ordered by run date descending,
    /// limited to the specified count. Uses the SQLite index for O(log n) lookup
    /// rather than deserialising the entire JSON collection.
    /// </summary>
    /// <param name="limit">The maximum number of recent runs to return.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>An ordered list of the most recent backtest results, newest first.</returns>
    Task<IReadOnlyList<BacktestResult>> GetRecentRunsAsync(int limit, CancellationToken ct = default);

    /// <summary>
    /// Retrieves the last (most recent) backtest result for each distinct strategy type.
    /// Uses a SQLite <c>GROUP BY</c> query to return at most one result per strategy.
    /// </summary>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>
    /// A dictionary keyed by strategy type with the most recent <see cref="BacktestResult"/> as the value.
    /// </returns>
    Task<IReadOnlyDictionary<string, BacktestResult>> GetLastRunPerStrategyAsync(CancellationToken ct = default);

    /// <summary>
    /// Retrieves all backtest run summaries for a specific strategy, identified by strategy ID.
    /// Queries the SQLite index to avoid full-collection deserialisation.
    /// </summary>
    /// <param name="strategyId">The unique identifier of the strategy to filter by.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A list of backtest results associated with the specified strategy.</returns>
    Task<IReadOnlyList<BacktestResult>> GetRunSummariesByStrategyAsync(string strategyId, CancellationToken ct = default);
}
