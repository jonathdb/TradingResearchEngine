namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Estimated cost of running a research study, including run count, wall time, and a human-readable summary.
/// </summary>
public sealed record StudyCostEstimate(
    /// <summary>Estimated number of individual backtest runs the study will execute.</summary>
    int EstimatedRunCount,
    /// <summary>Estimated wall-clock duration for the study.</summary>
    TimeSpan EstimatedWallTime,
    /// <summary>Human-readable summary of the estimate (e.g., "~500 runs, ~4 min 10 sec").</summary>
    string Summary,
    /// <summary>True when estimated wall time exceeds 5 minutes.</summary>
    bool IsLongRunning,
    /// <summary>True when no prior study data was available for calibration.</summary>
    bool UsedDefaultCostFactor);

/// <summary>
/// Computes pre-launch cost estimates for research studies based on study type,
/// configuration parameters, and historical run duration data.
/// </summary>
public sealed class StudyCostEstimatorService
{
    private readonly IBacktestResultRepository _resultRepo;

    /// <summary>Conservative default cost factor (milliseconds per run) when no prior data exists.</summary>
    private const double DefaultCostFactorMs = 500.0;

    /// <summary>Threshold above which a study is considered long-running.</summary>
    private static readonly TimeSpan LongRunningThreshold = TimeSpan.FromMinutes(5);

    /// <summary>Initializes a new instance of <see cref="StudyCostEstimatorService"/>.</summary>
    public StudyCostEstimatorService(IBacktestResultRepository resultRepo)
    {
        _resultRepo = resultRepo;
    }

    /// <summary>
    /// Estimates the cost of running a study based on its type and configuration.
    /// Uses the most recent completed backtest run duration as a per-run baseline.
    /// Falls back to a conservative default when no prior data exists.
    /// </summary>
    /// <param name="studyType">The type of study to estimate.</param>
    /// <param name="iterations">Number of iterations/paths/windows for the study.</param>
    /// <param name="parameterCombinations">Number of parameter combinations (for sweeps). Defaults to 1.</param>
    /// <param name="strategyVersionId">Optional strategy version ID to look up recent run durations.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="StudyCostEstimate"/> with the computed estimate.</returns>
    public async Task<StudyCostEstimate> EstimateAsync(
        StudyType studyType,
        int iterations,
        int parameterCombinations = 1,
        string? strategyVersionId = null,
        CancellationToken ct = default)
    {
        int estimatedRunCount = ComputeRunCount(studyType, iterations, parameterCombinations);

        // Try to calibrate from most recent completed run
        double costFactorMs = DefaultCostFactorMs;
        bool usedDefault = true;

        if (strategyVersionId is not null)
        {
            var recentRuns = await _resultRepo.ListByVersionAsync(strategyVersionId, ct);
            var completedRun = recentRuns
                .Where(r => r.Status == Core.Results.BacktestStatus.Completed && r.RunDurationMs > 0)
                .OrderByDescending(r => r.RunId) // Most recent run by ID (GUID v7 or insertion order)
                .FirstOrDefault();

            if (completedRun is not null)
            {
                costFactorMs = completedRun.RunDurationMs;
                usedDefault = false;
            }
        }

        var estimatedMs = estimatedRunCount * costFactorMs;
        var estimatedWallTime = TimeSpan.FromMilliseconds(estimatedMs);
        bool isLongRunning = estimatedWallTime > LongRunningThreshold;

        string summary = usedDefault
            ? $"~{estimatedRunCount} runs, ~{FormatDuration(estimatedWallTime)} (estimate — no prior run data)"
            : $"~{estimatedRunCount} runs, ~{FormatDuration(estimatedWallTime)}";

        return new StudyCostEstimate(
            estimatedRunCount,
            estimatedWallTime,
            summary,
            isLongRunning,
            usedDefault);
    }

    /// <summary>
    /// Computes the estimated number of individual backtest runs for a study type.
    /// </summary>
    private static int ComputeRunCount(StudyType studyType, int iterations, int parameterCombinations)
    {
        return studyType switch
        {
            StudyType.MonteCarlo => iterations,
            StudyType.ParameterSweep => parameterCombinations,
            StudyType.WalkForward or StudyType.AnchoredWalkForward => iterations,
            StudyType.RandomisedOos => iterations * 2, // IS + OOS per iteration
            StudyType.Sensitivity => iterations,
            StudyType.Realism => 3, // 3 realism profiles
            StudyType.ParameterStability => iterations,
            StudyType.CombinatorialPurgedCV => iterations,
            StudyType.Variance => iterations,
            StudyType.BenchmarkComparison => 1,
            StudyType.RegimeSegmentation => iterations,
            _ => iterations
        };
    }

    /// <summary>
    /// Formats a TimeSpan as a human-readable duration string.
    /// </summary>
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 60)
            return $"{duration.TotalSeconds:F0} sec";
        if (duration.TotalMinutes < 60)
            return $"{duration.Minutes} min {duration.Seconds} sec";
        return $"{duration.Hours} hr {duration.Minutes} min";
    }
}
