namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Research workflow and trace settings sub-object for <see cref="ScenarioConfig"/> decomposition.
/// </summary>
public sealed record ResearchConfig(
    /// <summary>Research workflow type key (e.g. "montecarlo", "walkforward"). Null for single runs.</summary>
    string? ResearchWorkflowType = null,
    /// <summary>Workflow-specific options.</summary>
    Dictionary<string, object>? ResearchWorkflowOptions = null,
    /// <summary>Explicit seed for deterministic stochastic workflows.</summary>
    int? RandomSeed = null,
    /// <summary>Trace and debugging options.</summary>
    TraceOptions? TraceOptions = null);
