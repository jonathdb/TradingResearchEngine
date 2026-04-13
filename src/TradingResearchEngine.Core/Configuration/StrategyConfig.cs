namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Strategy type and parameters sub-object for <see cref="ScenarioConfig"/> decomposition.
/// </summary>
public sealed record StrategyConfig(
    /// <summary>The strategy registry key (e.g. "volatility-scaled-trend").</summary>
    string StrategyType,
    /// <summary>Strategy-specific parameter values keyed by parameter name.</summary>
    Dictionary<string, object> StrategyParameters);
