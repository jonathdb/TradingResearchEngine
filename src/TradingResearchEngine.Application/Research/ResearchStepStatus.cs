namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Visual state of a research checklist step, enabling three distinct
/// rendering modes in the UI: not started, in progress, and completed.
/// </summary>
public enum ResearchStepStatus
{
    /// <summary>The research step has not been started.</summary>
    NotStarted,

    /// <summary>The research step is currently in progress.</summary>
    InProgress,

    /// <summary>The research step has been completed.</summary>
    Completed
}
