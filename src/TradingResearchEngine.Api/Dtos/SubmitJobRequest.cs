using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Api.Dtos;

/// <summary>
/// Request DTO for submitting an async job via <c>POST /jobs</c>.
/// Accepts either a full <see cref="ScenarioConfig"/> or V5 sub-objects.
/// </summary>
public sealed record SubmitJobRequest(
    ScenarioConfig? Config,
    DataConfig? Data,
    StrategyConfig? Strategy,
    RiskConfig? Risk,
    ExecutionConfig? Execution,
    ResearchConfig? Research,
    JobType JobType,
    string? PresetId = null,
    Dictionary<string, object>? WorkflowOptions = null);
