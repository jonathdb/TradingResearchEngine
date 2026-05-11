namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Extension methods for <see cref="ScenarioConfig"/> providing deep-copy semantics
/// for use in parallel workflows where dictionary mutation must be isolated per worker.
/// </summary>
public static class ScenarioConfigExtensions
{
    /// <summary>
    /// Creates an independent copy of the <see cref="ScenarioConfig"/> with all dictionary
    /// properties cloned. Mutations to the clone's dictionaries do not affect the original.
    /// </summary>
    public static ScenarioConfig DeepClone(this ScenarioConfig config) => config with
    {
        DataProviderOptions = new Dictionary<string, object>(config.DataProviderOptions),
        StrategyParameters = new Dictionary<string, object>(config.StrategyParameters),
        RiskParameters = new Dictionary<string, object>(config.RiskParameters),
        ResearchWorkflowOptions = config.ResearchWorkflowOptions is not null
            ? new Dictionary<string, object>(config.ResearchWorkflowOptions)
            : null,
        ExecutionOptions = config.ExecutionOptions is null ? null : config.ExecutionOptions with { },
    };
}
