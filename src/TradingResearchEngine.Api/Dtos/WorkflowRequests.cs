using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Api.Dtos;

/// <summary>Request wrapper for the parameter sweep endpoint.</summary>
public sealed record SweepRequest(ScenarioConfig Config, SweepOptions? Options = null);

/// <summary>Request wrapper for the Monte Carlo endpoint.</summary>
public sealed record MonteCarloRequest(ScenarioConfig Config, MonteCarloOptions? Options = null);

/// <summary>Request wrapper for the walk-forward endpoint.</summary>
public sealed record WalkForwardRequest(ScenarioConfig Config, WalkForwardOptions? Options = null);
