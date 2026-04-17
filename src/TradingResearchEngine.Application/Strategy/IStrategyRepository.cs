namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Persistence interface for strategy identities and versions.
/// Implemented by Infrastructure (JSON files).
/// </summary>
public interface IStrategyRepository
{
    /// <summary>Gets a strategy by ID, or null if not found.</summary>
    Task<StrategyIdentity?> GetAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Lists all strategies.</summary>
    Task<IReadOnlyList<StrategyIdentity>> ListAsync(CancellationToken ct = default);

    /// <summary>Saves or updates a strategy identity.</summary>
    Task SaveAsync(StrategyIdentity strategy, CancellationToken ct = default);

    /// <summary>Deletes a strategy and all its versions.</summary>
    Task DeleteAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Gets all versions for a strategy, ordered by version number.</summary>
    Task<IReadOnlyList<StrategyVersion>> GetVersionsAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Saves a strategy version.</summary>
    Task SaveVersionAsync(StrategyVersion version, CancellationToken ct = default);

    /// <summary>Gets the latest version for a strategy, or null if none exist.</summary>
    Task<StrategyVersion?> GetLatestVersionAsync(string strategyId, CancellationToken ct = default);

    /// <summary>Gets a strategy version by its ID directly, or null if not found.</summary>
    Task<StrategyVersion?> GetVersionAsync(string strategyVersionId, CancellationToken ct = default);
}
