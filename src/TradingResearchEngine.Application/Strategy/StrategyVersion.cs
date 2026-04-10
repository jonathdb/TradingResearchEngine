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
    string? ChangeNote = null,
    /// <summary>V4: Total number of trial executions against this version. Incremented per run (+1) or sweep (+N combinations).</summary>
    int TotalTrialsRun = 0,
    /// <summary>V4: Locked date range for the sealed held-out test set. Null if not configured.</summary>
    DateRangeConstraint? SealedTestSet = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => StrategyVersionId;
}
