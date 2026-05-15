using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Strategies;
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
    private readonly ILogger<ResearchChecklistService> _logger;

    /// <summary>
    /// Navigation paths for each checklist item, mapping step keys to workflow routes.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NavigationPaths = new Dictionary<string, string>
    {
        ["InitialBacktest"] = "/strategies",
        ["MonteCarloRobustness"] = "/research/montecarlo",
        ["WalkForwardValidation"] = "/research/walkforward",
        ["RegimeSensitivity"] = "/research/perturbation",
        ["RealismImpact"] = "/research/perturbation",
        ["ParameterSurface"] = "/research/sweep",
        ["FinalHeldOutTest"] = "/strategies",
        ["PropFirmEvaluation"] = "/propfirm",
        ["CpcvDone"] = "/research/explorer"
    };

    /// <summary>
    /// Confidence explanations for each checklist item when incomplete.
    /// Describes why the item matters and what risk its absence introduces.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ConfidenceExplanations = new Dictionary<string, string>
    {
        ["InitialBacktest"] = "No baseline performance has been established. Without an initial backtest, there is no evidence the strategy produces positive returns on historical data.",
        ["MonteCarloRobustness"] = "Robustness to trade ordering has not been verified. The observed equity curve may be an artifact of the specific trade sequence rather than genuine edge.",
        ["WalkForwardValidation"] = "Out-of-sample validation has not been performed. The strategy may be overfit to in-sample data and fail on unseen market conditions.",
        ["RegimeSensitivity"] = "Performance across different market regimes is unknown. The strategy may only work in specific conditions (trending, ranging, or volatile markets).",
        ["RealismImpact"] = "The impact of execution costs has not been measured. Theoretical performance may degrade significantly under realistic slippage and commission assumptions.",
        ["ParameterSurface"] = "Parameter stability has not been assessed. The strategy may rely on a fragile optimum that breaks with small parameter changes.",
        ["FinalHeldOutTest"] = "The sealed held-out test has not been run. Final out-of-sample confirmation is required before deployment confidence can be established.",
        ["PropFirmEvaluation"] = "Prop firm rule compliance has not been evaluated. The strategy may violate drawdown limits, profit targets, or other challenge constraints.",
        ["CpcvDone"] = "Combinatorial purged cross-validation has not been performed. The probability of backtest overfitting has not been quantified."
    };

    /// <summary>
    /// Identifies which checklist items are critical (gating) for final validation.
    /// Critical items must be complete before final validation can proceed without warnings.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, bool> CriticalItems = new Dictionary<string, bool>
    {
        ["InitialBacktest"] = true,
        ["MonteCarloRobustness"] = true,
        ["WalkForwardValidation"] = true,
        ["RegimeSensitivity"] = false,
        ["RealismImpact"] = false,
        ["ParameterSurface"] = true,
        ["FinalHeldOutTest"] = false, // This IS the final validation step itself
        ["PropFirmEvaluation"] = false,
        ["CpcvDone"] = false
    };

    /// <inheritdoc cref="ResearchChecklistService"/>
    public ResearchChecklistService(
        IBacktestResultRepository resultRepo,
        IStudyRepository studyRepo,
        IStrategyRepository strategyRepo,
        IPropFirmEvaluationRepository evalRepo,
        ILogger<ResearchChecklistService> logger)
    {
        _resultRepo = resultRepo;
        _studyRepo = studyRepo;
        _strategyRepo = strategyRepo;
        _evalRepo = evalRepo;
        _logger = logger;
    }

    /// <summary>
    /// Backward-compatible constructor for existing callers without logger.
    /// </summary>
    public ResearchChecklistService(
        IBacktestResultRepository resultRepo,
        IStudyRepository studyRepo,
        IStrategyRepository strategyRepo,
        IPropFirmEvaluationRepository evalRepo)
        : this(resultRepo, studyRepo, strategyRepo, evalRepo,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ResearchChecklistService>.Instance)
    {
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

    /// <summary>
    /// Returns detailed information for all checklist items, including navigation paths,
    /// confidence explanations, and criticality flags.
    /// </summary>
    /// <param name="checklist">The computed research checklist.</param>
    /// <returns>A list of detailed checklist items with navigation and explanation metadata.</returns>
    public static IReadOnlyList<ChecklistItemDetail> GetItemDetails(ResearchChecklist checklist)
    {
        var items = new (string Key, string Label, bool IsComplete)[]
        {
            ("InitialBacktest", "Initial backtest completed", checklist.InitialBacktest),
            ("MonteCarloRobustness", "Monte Carlo robustness", checklist.MonteCarloRobustness),
            ("WalkForwardValidation", "Walk-forward validation", checklist.WalkForwardValidation),
            ("RegimeSensitivity", "Regime sensitivity checked", checklist.RegimeSensitivity),
            ("RealismImpact", "Execution realism impact measured", checklist.RealismImpact),
            ("ParameterSurface", "Parameter surface mapped", checklist.ParameterSurface),
            ("FinalHeldOutTest", "Final held-out test", checklist.FinalHeldOutTest),
            ("PropFirmEvaluation", "Prop firm evaluation", checklist.PropFirmEvaluation),
            ("CpcvDone", "CPCV overfitting assessment", checklist.CpcvDone)
        };

        return items.Select(item => new ChecklistItemDetail(
            Key: item.Key,
            Label: item.Label,
            IsComplete: item.IsComplete,
            IsCritical: CriticalItems.GetValueOrDefault(item.Key, false),
            NavigationPath: NavigationPaths.GetValueOrDefault(item.Key, "/strategies"),
            ConfidenceExplanation: ConfidenceExplanations.GetValueOrDefault(item.Key, "")
        )).ToList();
    }

    /// <summary>
    /// Returns only the incomplete checklist items with their navigation paths and explanations.
    /// Items are returned in checklist order with prominent metadata for UI rendering.
    /// </summary>
    /// <param name="checklist">The computed research checklist.</param>
    /// <returns>Incomplete items with navigation guidance.</returns>
    public static IReadOnlyList<ChecklistItemDetail> GetIncompleteItems(ResearchChecklist checklist)
    {
        return GetItemDetails(checklist)
            .Where(item => !item.IsComplete)
            .ToList();
    }

    /// <summary>
    /// Checks whether all critical checklist items are complete.
    /// Critical items are gating requirements for final validation.
    /// </summary>
    /// <param name="checklist">The computed research checklist.</param>
    /// <returns><c>true</c> when all critical items are complete; <c>false</c> otherwise.</returns>
    public static bool AreCriticalItemsComplete(ResearchChecklist checklist)
    {
        return GetItemDetails(checklist)
            .Where(item => item.IsCritical)
            .All(item => item.IsComplete);
    }

    /// <summary>
    /// Evaluates checklist readiness for final validation, returning a structured result
    /// with warnings for incomplete critical items and navigation guidance.
    /// </summary>
    /// <param name="strategyVersionId">The strategy version to evaluate.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A readiness result indicating whether final validation can proceed.</returns>
    public async Task<ChecklistReadinessResult> EvaluateReadinessAsync(
        string strategyVersionId,
        CancellationToken ct = default)
    {
        var checklist = await ComputeAsync(strategyVersionId, ct);
        var incompleteItems = GetIncompleteItems(checklist);
        var incompleteCritical = incompleteItems.Where(i => i.IsCritical).ToList();
        var isReady = incompleteCritical.Count == 0;

        var warnings = new List<string>();

        if (!isReady)
        {
            warnings.Add("Critical research steps are incomplete. Final validation results may not be trustworthy.");

            foreach (var item in incompleteCritical)
            {
                warnings.Add($"• {item.Label}: {item.ConfidenceExplanation}");
            }
        }

        if (checklist.ConfidenceLevel == "LOW")
        {
            warnings.Add($"Research confidence is LOW ({checklist.PassedCount} of {checklist.TotalChecks} steps complete). " +
                "Consider completing more research steps before final validation.");
        }

        _logger.LogInformation(
            "Checklist readiness evaluated for '{StrategyVersionId}': Ready={IsReady}, IncompleteCritical={Count}",
            strategyVersionId, isReady, incompleteCritical.Count);

        return new ChecklistReadinessResult(isReady, warnings, incompleteItems);
    }

    /// <summary>
    /// Returns a human-readable explanation of why the current confidence level is what it is.
    /// Provides context beyond just the numeric score.
    /// </summary>
    /// <param name="checklist">The computed research checklist.</param>
    /// <returns>A descriptive explanation of the confidence assessment.</returns>
    public static string GetConfidenceExplanation(ResearchChecklist checklist)
    {
        return checklist.ConfidenceLevel switch
        {
            "HIGH" => $"Research confidence is HIGH ({checklist.PassedCount} of {checklist.TotalChecks} steps complete). " +
                "The strategy has been validated across multiple dimensions including robustness, out-of-sample performance, and parameter stability.",
            "MEDIUM" => $"Research confidence is MEDIUM ({checklist.PassedCount} of {checklist.TotalChecks} steps complete). " +
                "Some validation steps remain incomplete. " + GetTopMissingExplanation(checklist),
            _ => $"Research confidence is LOW ({checklist.PassedCount} of {checklist.TotalChecks} steps complete). " +
                "Significant validation gaps exist. " + GetTopMissingExplanation(checklist)
        };
    }

    /// <summary>
    /// Returns a brief explanation of the most important missing steps.
    /// </summary>
    private static string GetTopMissingExplanation(ResearchChecklist checklist)
    {
        var incomplete = GetIncompleteItems(checklist)
            .Where(i => i.IsCritical)
            .Take(2)
            .ToList();

        if (incomplete.Count == 0)
        {
            incomplete = GetIncompleteItems(checklist).Take(2).ToList();
        }

        if (incomplete.Count == 0)
            return "All steps are complete.";

        var explanations = incomplete.Select(i => i.Label);
        return $"Key missing steps: {string.Join(", ", explanations)}.";
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
