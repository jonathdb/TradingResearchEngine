using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Computes the research checklist for a strategy version by querying
/// runs, studies, and evaluations. The checklist tracks validation progress
/// and produces a Confidence Level score.
/// </summary>
public sealed class ResearchChecklistService
{
    private readonly IRepository<BacktestResult> _resultRepo;
    private readonly IStudyRepository _studyRepo;
    private readonly IStrategyRepository _strategyRepo;

    /// <inheritdoc cref="ResearchChecklistService"/>
    public ResearchChecklistService(
        IRepository<BacktestResult> resultRepo,
        IStudyRepository studyRepo,
        IStrategyRepository strategyRepo)
    {
        _resultRepo = resultRepo;
        _studyRepo = studyRepo;
        _strategyRepo = strategyRepo;
    }

    /// <summary>
    /// Computes the research checklist for the given strategy version.
    /// </summary>
    public async Task<ResearchChecklist> ComputeAsync(
        string strategyVersionId,
        CancellationToken ct = default)
    {
        var allResults = await _resultRepo.ListAsync(ct);
        var versionResults = allResults
            .Where(r => r.StrategyVersionId == strategyVersionId)
            .ToList();

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
        var versions = await GetVersionAsync(strategyVersionId, ct);
        if (versions is not null)
        {
            var strategy = await _strategyRepo.GetAsync(versions.StrategyId, ct);
            finalHeldOutTest = strategy?.Stage == DevelopmentStage.FinalTest;
        }

        // Prop firm evaluation: check if any completed run has been evaluated
        // (simplified: we check if there's a Realism study as proxy, since
        // prop evaluation is computed on-demand and not persisted as a study)
        bool propFirmEvaluation = false; // TODO: wire to prop firm evaluation persistence

        return new ResearchChecklist(
            initialBacktest, monteCarloRobustness, walkForwardValidation,
            regimeSensitivity, realismImpact, parameterSurface,
            finalHeldOutTest, propFirmEvaluation);
    }

    private async Task<StrategyVersion?> GetVersionAsync(string versionId, CancellationToken ct)
    {
        var strategies = await _strategyRepo.ListAsync(ct);
        foreach (var s in strategies)
        {
            var versions = await _strategyRepo.GetVersionsAsync(s.StrategyId, ct);
            var match = versions.FirstOrDefault(v => v.StrategyVersionId == versionId);
            if (match is not null) return match;
        }
        return null;
    }
}

/// <summary>
/// The 8-item research checklist with a computed Confidence Level.
/// </summary>
public sealed record ResearchChecklist(
    bool InitialBacktest,
    bool MonteCarloRobustness,
    bool WalkForwardValidation,
    bool RegimeSensitivity,
    bool RealismImpact,
    bool ParameterSurface,
    bool FinalHeldOutTest,
    bool PropFirmEvaluation)
{
    /// <summary>Number of checks that have passed.</summary>
    public int PassedCount => new[]
    {
        InitialBacktest, MonteCarloRobustness, WalkForwardValidation,
        RegimeSensitivity, RealismImpact, ParameterSurface,
        FinalHeldOutTest, PropFirmEvaluation
    }.Count(x => x);

    /// <summary>Total number of checks.</summary>
    public int TotalChecks => 8;

    /// <summary>Confidence level based on passed checks.</summary>
    public string ConfidenceLevel => PassedCount switch
    {
        >= 7 => "HIGH",
        >= 4 => "MEDIUM",
        _ => "LOW"
    };
}
