using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// The sole input required to initialise and execute a simulation run.
/// Deserialised from a JSON file (CLI) or request body (API).
/// Implements <see cref="IHasId"/> for persistence via <c>IRepository&lt;ScenarioConfig&gt;</c>.
/// </summary>
public sealed record ScenarioConfig(
    string ScenarioId,
    string Description,
    ReplayMode ReplayMode,
    string DataProviderType,
    Dictionary<string, object> DataProviderOptions,
    string StrategyType,
    Dictionary<string, object> StrategyParameters,
    Dictionary<string, object> RiskParameters,
    string SlippageModelType,
    string CommissionModelType,
    decimal InitialCash,
    decimal AnnualRiskFreeRate,
    int? RandomSeed,
    string? ResearchWorkflowType,
    Dictionary<string, object>? ResearchWorkflowOptions,
    PropFirmOptions? PropFirmOptions) : IHasId
{
    /// <inheritdoc/>
    public string Id => ScenarioId;
}
