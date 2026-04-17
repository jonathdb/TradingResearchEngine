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
}
