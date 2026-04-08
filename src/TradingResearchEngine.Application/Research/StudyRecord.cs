using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// A research workflow execution linked to a strategy version.
/// A Monte Carlo study with 1000 paths is ONE study, not 1000 runs.
/// A walk-forward study with 10 windows is ONE study, not 10 runs.
/// </summary>
public sealed record StudyRecord(
    string StudyId,
    string StrategyVersionId,
    StudyType Type,
    StudyStatus Status,
    DateTimeOffset CreatedAt,
    string? SourceRunId = null,
    string? ErrorSummary = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => StudyId;
}

/// <summary>Type of research workflow.</summary>
public enum StudyType
{
    MonteCarlo,
    WalkForward,
    Sensitivity,
    ParameterSweep,
    Realism,
    ParameterStability
}

/// <summary>Lifecycle status of a study.</summary>
public enum StudyStatus
{
    Running,
    Completed,
    Failed,
    Incomplete,
    Cancelled
}
