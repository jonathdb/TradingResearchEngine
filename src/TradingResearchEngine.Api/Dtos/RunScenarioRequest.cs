using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Api.Dtos;

/// <summary>
/// Request DTO for running a scenario synchronously.
/// Accepts either a full <see cref="ScenarioConfig"/> or V5 sub-objects.
/// </summary>
public sealed record RunScenarioRequest(
    ScenarioConfig? Config,
    DataConfig? Data,
    StrategyConfig? Strategy,
    RiskConfig? Risk,
    ExecutionConfig? Execution,
    string? PresetId = null);
