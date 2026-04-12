namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Risk parameters, position sizing, and exposure limits sub-object
/// for <see cref="ScenarioConfig"/> decomposition.
/// </summary>
public sealed record RiskConfig(
    /// <summary>Risk-layer-specific parameter values.</summary>
    Dictionary<string, object> RiskParameters,
    /// <summary>Starting cash balance for the simulation.</summary>
    decimal InitialCash = 100_000m,
    /// <summary>Annual risk-free rate for Sharpe/Sortino computation.</summary>
    decimal AnnualRiskFreeRate = 0.05m);
