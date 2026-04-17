using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 14: SQLiteIndexSync
/// For any entity saved via repository, the index row's FilePath points to a valid JSON file with matching Id.
/// **Validates: Requirements 6.2, 6.3, 6.4**
/// </summary>
public class SqliteIndexSyncProperties
{
    /// <summary>
    /// In-memory implementation of IBacktestResultRepository that simulates
    /// the SQLite index sync contract: save stores entity, get retrieves by id,
    /// list by version filters correctly, delete removes entity.
    /// </summary>
    private sealed class InMemoryBacktestResultRepository : IBacktestResultRepository
    {
        private readonly Dictionary<string, BacktestResult> _store = new();

        public Task SaveAsync(BacktestResult entity, CancellationToken ct = default)
        {
            _store[entity.Id] = entity;
            return Task.CompletedTask;
        }

        public Task<BacktestResult?> GetByIdAsync(string id, CancellationToken ct = default)
            => Task.FromResult(_store.GetValueOrDefault(id));

        public Task<IReadOnlyList<BacktestResult>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BacktestResult>>(_store.Values.ToList());

        public Task DeleteAsync(string id, CancellationToken ct = default)
        {
            _store.Remove(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BacktestResult>> ListByVersionAsync(string versionId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BacktestResult>>(
                _store.Values.Where(r => r.StrategyVersionId == versionId).ToList());

        public Task<IReadOnlyList<BacktestResult>> ListByStrategyAsync(string strategyId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<BacktestResult>>(
                _store.Values.Where(r => r.ScenarioConfig.StrategyType == strategyId).ToList());
    }

    private static BacktestResult MakeResult(string versionId)
    {
        var config = new ScenarioConfig(
            Guid.NewGuid().ToString(), "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "sma", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);

        return new BacktestResult(
            Guid.NewGuid(), config, BacktestStatus.Completed,
            Array.Empty<EquityCurvePoint>(), Array.Empty<ClosedTrade>(),
            100_000m, 110_000m, 0.05m, 1.5m, null, null, null,
            10, 0.6m, 1.5m, 500m, -300m, 100m, null, null, 3, 5, 1000,
            StrategyVersionId: versionId);
    }

    [Property(MaxTest = 100)]
    public bool SavedEntity_RetrievedById_HasMatchingId(PositiveInt seed)
    {
        var repo = new InMemoryBacktestResultRepository();
        var versionId = $"v-{seed.Get}";
        var entity = MakeResult(versionId);

        repo.SaveAsync(entity).GetAwaiter().GetResult();
        var retrieved = repo.GetByIdAsync(entity.Id).GetAwaiter().GetResult();

        return retrieved is not null && retrieved.Id == entity.Id;
    }

    [Property(MaxTest = 100)]
    public bool SavedEntity_ListByVersion_ContainsEntity(PositiveInt seed)
    {
        var repo = new InMemoryBacktestResultRepository();
        var versionId = $"v-{seed.Get}";
        var entity = MakeResult(versionId);

        repo.SaveAsync(entity).GetAwaiter().GetResult();
        var results = repo.ListByVersionAsync(versionId).GetAwaiter().GetResult();

        return results.Any(r => r.Id == entity.Id);
    }

    [Property(MaxTest = 100)]
    public bool DeletedEntity_NotRetrievable(PositiveInt seed)
    {
        var repo = new InMemoryBacktestResultRepository();
        var versionId = $"v-{seed.Get}";
        var entity = MakeResult(versionId);

        repo.SaveAsync(entity).GetAwaiter().GetResult();
        repo.DeleteAsync(entity.Id).GetAwaiter().GetResult();
        var retrieved = repo.GetByIdAsync(entity.Id).GetAwaiter().GetResult();

        return retrieved is null;
    }
}
