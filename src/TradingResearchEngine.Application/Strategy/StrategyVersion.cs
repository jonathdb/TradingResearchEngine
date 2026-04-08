using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// A specific parameter configuration of a strategy. Changing parameters
/// creates a new version; previous versions and their runs are preserved.
/// </summary>
public sealed record StrategyVersion(
    string StrategyVersionId,
    string StrategyId,
    int VersionNumber,
    Dictionary<string, object> Parameters,
    ScenarioConfig BaseScenarioConfig,
    DateTimeOffset CreatedAt,
    string? ChangeNote = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => StrategyVersionId;
}
