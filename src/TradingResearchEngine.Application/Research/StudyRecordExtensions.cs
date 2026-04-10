namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Extension methods for creating partial and completed <see cref="StudyRecord"/> instances.
/// </summary>
public static class StudyRecordExtensions
{
    /// <summary>
    /// Creates a cancelled/partial study record from an active study.
    /// </summary>
    public static StudyRecord AsCancelled(
        this StudyRecord study, int completedCount, int totalCount) =>
        study with
        {
            Status = StudyStatus.Cancelled,
            IsPartial = true,
            CompletedCount = completedCount,
            TotalCount = totalCount
        };

    /// <summary>
    /// Creates a completed study record.
    /// </summary>
    public static StudyRecord AsCompleted(
        this StudyRecord study, int completedCount) =>
        study with
        {
            Status = StudyStatus.Completed,
            IsPartial = false,
            CompletedCount = completedCount,
            TotalCount = completedCount
        };

    /// <summary>
    /// Creates a failed study record.
    /// </summary>
    public static StudyRecord AsFailed(
        this StudyRecord study, string errorMessage) =>
        study with
        {
            Status = StudyStatus.Failed,
            ErrorSummary = errorMessage
        };

    /// <summary>
    /// Returns true if the study has enough completed units for verdicts.
    /// Monte Carlo requires at least 200 paths for verdict computation.
    /// </summary>
    public static bool HasEnoughForVerdict(this StudyRecord study) =>
        study.Type == StudyType.MonteCarlo
            ? study.CompletedCount >= 200
            : study.CompletedCount >= 1;
}
