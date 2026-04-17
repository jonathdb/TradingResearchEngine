using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>Named constants for trial budget thresholds.</summary>
public static class TrialBudgetDefaults
{
    /// <summary>Maximum trials before amber warning when no walk-forward exists.</summary>
    public const int AmberThreshold = 20;

    /// <summary>Maximum trials before red warning when no walk-forward exists.</summary>
    public const int RedThreshold = 50;

    /// <summary>Number of parameter sweeps without walk-forward that triggers over-optimization warning.</summary>
    public const int OverOptimizationSweepThreshold = 5;
}

/// <summary>V5: Trial budget status indicating overfitting risk from repeated testing.</summary>
public enum TrialBudgetStatus
{
    /// <summary>Low risk: trials ≤ 20 or walk-forward validation exists.</summary>
    Green,

    /// <summary>Moderate risk: 20 &lt; trials ≤ 50 without walk-forward validation.</summary>
    Amber,

    /// <summary>High risk: trials &gt; 50 without walk-forward validation.</summary>
    Red
}

/// <summary>V5: Recommended next action for a strategy version.</summary>
public sealed record NextRecommendedAction(
    string ActionLabel,
    string Description,
    StudyType? SuggestedStudyType,
    bool IsWarning);

/// <summary>
/// Computes the research checklist for a strategy version by querying
/// runs, studies, and evaluations. The checklist tracks validation progress
/// and produces a Confidence Level score.
/// </summary>
public sealed class ResearchChecklistService
{
    private readonly IBacktestResultRepository _resultRepo;
    private readonly IStudyRepository _studyRepo;
    private readonly IStrategyRepository _strategyRepo;
    private readonly IPropFirmEvaluationRepository _evalRepo;

    /// <inheritdoc cref="ResearchChecklistService"/>
    public ResearchChecklistService(
        IBacktestResultRepository resultRepo,
        IStudyRepository studyRepo,
        IStrategyRepository strategyRepo,
        IPropFirmEvaluationRepository evalRepo)
    {
        _resultRepo = resultRepo;
        _studyRepo = studyRepo;
        _strategyRepo = strategyRepo;
        _evalRepo = evalRepo;
    }

    /// <summary>
    /// Computes the research checklist for the given strategy version.
    /// </summary>
    public async Task<ResearchChecklist> ComputeAsync(
        string strategyVersionId,
        CancellationToken ct = default)
    {
        var versionResults = await _resultRepo.ListByVersionAsync(strategyVersionId, ct);

        var studies = await _studyRepo.ListByVersionAsync(strategyVersionId, ct);
        var completedStudies = studies
            .Where(s => s.Status == StudyStatus.Completed)
            .ToList();

        bool initialBacktest = versionResults
            .Any(r => r.Status == BacktestStatus.Completed);

        bool monteCarloRobustness = completedStudies
            .Any(s => s.Type == StudyType.MonteCarlo);

        bool walkForwardValidation = completedStudies
            .Any(s => s.Type is StudyType.WalkForward or StudyType.AnchoredWalkForward);

        bool regimeSensitivity = completedStudies
            .Any(s => s.Type == StudyType.RegimeSegmentation);

        bool realismImpact = completedStudies
            .Any(s => s.Type == StudyType.Realism);

        bool parameterSurface = completedStudies
            .Any(s => s.Type is StudyType.Sensitivity or StudyType.ParameterSweep);

        // Check if a final validation run exists (strategy stage = FinalTest)
        bool finalHeldOutTest = false;
        var version = await _strategyRepo.GetVersionAsync(strategyVersionId, ct);
        if (version is not null)
        {
            var strategy = await _strategyRepo.GetAsync(version.StrategyId, ct);
            finalHeldOutTest = strategy?.Stage == DevelopmentStage.FinalTest;
        }

        // Prop firm evaluation: check if any completed evaluation exists
        bool propFirmEvaluation = await _evalRepo.HasCompletedEvaluationAsync(strategyVersionId, ct);

        // V6: CPCV study completion check (9th checklist item)
        bool cpcvDone = completedStudies
            .Any(s => s.Type is StudyType.CombinatorialPurgedCV or StudyType.Cpcv);

        // V5: Compute trial budget status
        int totalTrialsRun = version?.TotalTrialsRun ?? 0;
        var trialBudget = ComputeTrialBudgetStatus(totalTrialsRun, walkForwardValidation);

        // V5: Compute next recommended action
        int sweepCount = completedStudies.Count(s => s.Type == StudyType.ParameterSweep);
        var nextAction = ComputeNextAction(
            initialBacktest, monteCarloRobustness, walkForwardValidation,
            regimeSensitivity, realismImpact, parameterSurface,
            finalHeldOutTest, sweepCount);

        return new ResearchChecklist(
            initialBacktest, monteCarloRobustness, walkForwardValidation,
            regimeSensitivity, realismImpact, parameterSurface,
            finalHeldOutTest, propFirmEvaluation, cpcvDone, trialBudget, nextAction);
    }

    /// <summary>
    /// Computes the trial budget status based on total trials and walk-forward existence.
    /// Green when trials ≤ 20 or walk-forward exists, Amber when 20 &lt; trials ≤ 50 without WF,
    /// Red when &gt; 50 without WF.
    /// </summary>
    public static TrialBudgetStatus ComputeTrialBudgetStatus(int totalTrialsRun, bool hasWalkForward)
    {
        if (hasWalkForward || totalTrialsRun <= TrialBudgetDefaults.AmberThreshold)
            return TrialBudgetStatus.Green;

        if (totalTrialsRun <= TrialBudgetDefaults.RedThreshold)
            return TrialBudgetStatus.Amber;

        return TrialBudgetStatus.Red;
    }

    /// <summary>
    /// Computes the next recommended action based on completed studies and checklist state.
    /// </summary>
    private static NextRecommendedAction? ComputeNextAction(
        bool initialBacktest,
        bool monteCarlo,
        bool walkForward,
        bool regimeSensitivity,
        bool realismImpact,
        bool parameterSurface,
        bool finalHeldOutTest,
        int sweepCount)
    {
        // Over-optimization warning: > 5 parameter sweeps without walk-forward
        if (sweepCount > TrialBudgetDefaults.OverOptimizationSweepThreshold && !walkForward)
        {
            return new NextRecommendedAction(
                "Run Walk-Forward Study",
                "Consider running a walk-forward study to check for overfitting before further parameter tuning.",
                StudyType.WalkForward,
                IsWarning: true);
        }

        if (!initialBacktest)
        {
            return new NextRecommendedAction(
                "Run Initial Backtest",
                "Run your first backtest to establish a baseline.",
                null,
                IsWarning: false);
        }

        if (!monteCarlo)
        {
            return new NextRecommendedAction(
                "Run Monte Carlo Robustness",
                "Run a Monte Carlo simulation to assess strategy robustness under randomized conditions.",
                StudyType.MonteCarlo,
                IsWarning: false);
        }

        if (!walkForward)
        {
            return new NextRecommendedAction(
                "Run Walk-Forward Validation",
                "Run a walk-forward study to validate out-of-sample performance.",
                StudyType.WalkForward,
                IsWarning: false);
        }

        if (!parameterSurface)
        {
            return new NextRecommendedAction(
                "Run Parameter Sensitivity",
                "Run a sensitivity analysis to understand parameter stability.",
                StudyType.Sensitivity,
                IsWarning: false);
        }

        if (!regimeSensitivity)
        {
            return new NextRecommendedAction(
                "Run Regime Segmentation",
                "Analyze performance across different market regimes.",
                StudyType.RegimeSegmentation,
                IsWarning: false);
        }

        if (!realismImpact)
        {
            return new NextRecommendedAction(
                "Run Realism Impact Study",
                "Test strategy performance under different execution realism profiles.",
                StudyType.Realism,
                IsWarning: false);
        }

        if (!finalHeldOutTest)
        {
            return new NextRecommendedAction(
                "Run Final Held-Out Test",
                "Run the sealed test set for final validation.",
                null,
                IsWarning: false);
        }

        // All checks passed
        return null;
    }

}

/// <summary>
/// The 9-item research checklist with a computed Confidence Level.
/// V5: Extended with TrialBudget and NextAction fields.
/// V6: Added CpcvDone as 9th item; updated thresholds: HIGH ≥ 8, MEDIUM ≥ 5, LOW &lt; 5.
/// </summary>
public sealed record ResearchChecklist(
    bool InitialBacktest,
    bool MonteCarloRobustness,
    bool WalkForwardValidation,
    bool RegimeSensitivity,
    bool RealismImpact,
    bool ParameterSurface,
    bool FinalHeldOutTest,
    bool PropFirmEvaluation,
    /// <summary>V6: Whether a CPCV study has been completed.</summary>
    bool CpcvDone,
    /// <summary>V5: Trial budget status.</summary>
    TrialBudgetStatus TrialBudget = TrialBudgetStatus.Green,
    /// <summary>V5: Computed next recommended action.</summary>
    NextRecommendedAction? NextAction = null)
{
    /// <summary>Number of checks that have passed.</summary>
    public int PassedCount => new[]
    {
        InitialBacktest, MonteCarloRobustness, WalkForwardValidation,
        RegimeSensitivity, RealismImpact, ParameterSurface,
        FinalHeldOutTest, PropFirmEvaluation, CpcvDone
    }.Count(x => x);

    /// <summary>Total number of checks.</summary>
    public int TotalChecks => 9;

    /// <summary>Confidence level based on passed checks. V6: HIGH ≥ 8, MEDIUM ≥ 5, LOW &lt; 5.</summary>
    public string ConfidenceLevel => PassedCount switch
    {
        >= 8 => "HIGH",
        >= 5 => "MEDIUM",
        _ => "LOW"
    };
}
