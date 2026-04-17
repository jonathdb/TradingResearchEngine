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
    string? ErrorSummary = null,
    /// <summary>V4: True when the study was cancelled before completion.</summary>
    bool IsPartial = false,
    /// <summary>V4: Number of completed units (paths, windows, combinations) when partial.</summary>
    int CompletedCount = 0,
    /// <summary>V4: Total planned units (paths, windows, combinations).</summary>
    int TotalCount = 0,
    /// <summary>V7: Serialized JSON of the workflow result. Null until the study completes.</summary>
    string? ResultJson = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => StudyId;
}

/// <summary>Type of research workflow.</summary>
public enum StudyType
{
    MonteCarlo,
    WalkForward,
    /// <summary>V4: Walk-forward with fixed training start, expanding window.</summary>
    AnchoredWalkForward,
    /// <summary>V4: Combinatorial Purged Cross-Validation with PBO metric. Deferred to V4.1.</summary>
    CombinatorialPurgedCV,
    /// <summary>V6: Alias for CombinatorialPurgedCV — short name used by BackgroundStudyService.</summary>
    Cpcv = CombinatorialPurgedCV,
    Sensitivity,
    ParameterSweep,
    Realism,
    ParameterStability,
    /// <summary>V4: Post-hoc regime segmentation analysis.</summary>
    RegimeSegmentation,
    /// <summary>V7: Benchmark comparison against a buy-and-hold baseline.</summary>
    BenchmarkComparison,
    /// <summary>V7: Variance testing — stability across sub-period slices.</summary>
    Variance,
    /// <summary>V7: Randomised OOS sampling study.</summary>
    RandomisedOos,
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
