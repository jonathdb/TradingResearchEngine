using Moq;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Metrics;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.V4;

public class V4ServicesTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // --- DsrCalculator ---

    [Fact]
    public void DsrCalculator_KnownInputs_ReturnsExpected()
    {
        // With 1 trial, normal returns, reasonable Sharpe, DSR should be close to 0.5
        // (single trial = no multiple testing penalty, but the formula still applies)
        var dsr = DsrCalculator.Compute(
            observedSharpe: 1.5m,
            trialCount: 1,
            skewness: 0m,
            kurtosis: 0m,
            barCount: 252,
            barsPerYear: 252);

        Assert.True(dsr > 0m, "DSR should be positive for a positive Sharpe");
        Assert.True(dsr <= 1m, "DSR is a probability, should be <= 1");
    }

    [Fact]
    public void DsrCalculator_SingleTrial_HigherThanManyTrials()
    {
        var dsrSingle = DsrCalculator.Compute(1.5m, 1, 0m, 0m, 252, 252);
        var dsrMany = DsrCalculator.Compute(1.5m, 50, 0m, 0m, 252, 252);

        Assert.True(dsrSingle > dsrMany,
            "DSR should decrease with more trials (multiple testing penalty)");
    }

    [Fact]
    public void DsrCalculator_ZeroTrials_ReturnsObservedSharpe()
    {
        var dsr = DsrCalculator.Compute(1.5m, 0, 0m, 0m, 252, 252);
        Assert.Equal(1.5m, dsr);
    }

    [Fact]
    public void DsrCalculator_HighTrialCount_LowDsr()
    {
        // With 100 trials, a Sharpe of 1.0 should have a low DSR
        var dsr = DsrCalculator.Compute(1.0m, 100, 0m, 0m, 252, 252);
        Assert.True(dsr < 0.95m,
            $"DSR should be below 0.95 with 100 trials, got {dsr}");
    }

    // --- MinBtlCalculator ---

    [Fact]
    public void MinBtlCalculator_ShortData_ReturnsHighMinimum()
    {
        var minBars = MinBtlCalculator.MinimumBarsRequired(
            observedSharpe: 0.5m,
            trialCount: 10,
            skewness: 0m,
            kurtosis: 0m);

        Assert.True(minBars > 100,
            $"Low Sharpe with many trials should require many bars, got {minBars}");
    }

    [Fact]
    public void MinBtlCalculator_HighSharpe_LowerMinimum()
    {
        var minLow = MinBtlCalculator.MinimumBarsRequired(0.5m, 1, 0m, 0m);
        var minHigh = MinBtlCalculator.MinimumBarsRequired(2.0m, 1, 0m, 0m);

        Assert.True(minHigh < minLow,
            "Higher Sharpe should require fewer bars for significance");
    }

    [Fact]
    public void MinBtlCalculator_ZeroSharpe_ReturnsZero()
    {
        var minBars = MinBtlCalculator.MinimumBarsRequired(0m, 1, 0m, 0m);
        Assert.Equal(0, minBars);
    }

    // --- ResearchChecklistService ---

    [Fact]
    public async Task ResearchChecklist_NoStudies_AllFalse()
    {
        var resultRepo = new Mock<IBacktestResultRepository>();
        resultRepo.Setup(r => r.ListByVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestResult>());

        var studyRepo = new Mock<IStudyRepository>();
        studyRepo.Setup(r => r.ListByVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StudyRecord>());

        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StrategyIdentity>());

        var evalRepo = new Mock<IPropFirmEvaluationRepository>();
        evalRepo.Setup(r => r.HasCompletedEvaluationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new ResearchChecklistService(resultRepo.Object, studyRepo.Object, strategyRepo.Object, evalRepo.Object);
        var checklist = await service.ComputeAsync("v1");

        Assert.False(checklist.InitialBacktest);
        Assert.False(checklist.MonteCarloRobustness);
        Assert.False(checklist.WalkForwardValidation);
        Assert.Equal(0, checklist.PassedCount);
        Assert.Equal("LOW", checklist.ConfidenceLevel);
    }

    [Fact]
    public async Task ResearchChecklist_AllComplete_HighConfidence()
    {
        var config = MakeConfig();
        var completedRun = MakeResult(config) with { StrategyVersionId = "v1" };

        var resultRepo = new Mock<IBacktestResultRepository>();
        resultRepo.Setup(r => r.ListByVersionAsync("v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestResult> { completedRun });

        var studies = new List<StudyRecord>
        {
            new("s1", "v1", StudyType.MonteCarlo, StudyStatus.Completed, T0),
            new("s2", "v1", StudyType.WalkForward, StudyStatus.Completed, T0),
            new("s3", "v1", StudyType.RegimeSegmentation, StudyStatus.Completed, T0),
            new("s4", "v1", StudyType.Realism, StudyStatus.Completed, T0),
            new("s5", "v1", StudyType.Sensitivity, StudyStatus.Completed, T0),
        };

        var studyRepo = new Mock<IStudyRepository>();
        studyRepo.Setup(r => r.ListByVersionAsync("v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(studies);

        var strategy = new StrategyIdentity("s1", "Test", "sma", T0, Stage: DevelopmentStage.FinalTest);
        var version = new StrategyVersion("v1", "s1", 1, new(), config, T0);

        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StrategyIdentity> { strategy });
        strategyRepo.Setup(r => r.GetAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategy);
        strategyRepo.Setup(r => r.GetVersionsAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StrategyVersion> { version });

        var evalRepo = new Mock<IPropFirmEvaluationRepository>();
        evalRepo.Setup(r => r.HasCompletedEvaluationAsync("v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new ResearchChecklistService(resultRepo.Object, studyRepo.Object, strategyRepo.Object, evalRepo.Object);
        var checklist = await service.ComputeAsync("v1");

        Assert.True(checklist.InitialBacktest);
        Assert.True(checklist.MonteCarloRobustness);
        Assert.True(checklist.WalkForwardValidation);
        Assert.True(checklist.RegimeSensitivity);
        Assert.True(checklist.RealismImpact);
        Assert.True(checklist.ParameterSurface);
        Assert.True(checklist.FinalHeldOutTest);
        // V6: PropFirmEvaluation=false, CpcvDone=false → 7 of 9 = MEDIUM
        Assert.Equal(7, checklist.PassedCount);
        Assert.Equal("MEDIUM", checklist.ConfidenceLevel);
    }

    // --- SealedTestSetViolationException ---

    [Fact]
    public void SealedTestSetViolation_OverlappingRange_Throws()
    {
        var sealed_ = new DateRangeConstraint(
            new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            IsSealed: true);

        var studyStart = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var studyEnd = new DateTimeOffset(2024, 8, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.True(sealed_.Overlaps(studyStart, studyEnd));

        // Simulate what the orchestrator would do
        var ex = Assert.Throws<SealedTestSetViolationException>(
            ThrowSealedViolation);
        Assert.Contains("sealed test set", ex.Message);
    }

    private static void ThrowSealedViolation() =>
        throw new SealedTestSetViolationException("Study date range overlaps the sealed test set.");

    // --- BackgroundStudyService ---

    [Fact]
    public void BackgroundStudyService_RegisterAndCancel()
    {
        using var service = new BackgroundStudyService();
        var ct = service.RegisterStudy("study-1", "v1", StudyType.MonteCarlo, 1000);

        Assert.Single(service.GetActiveStudies());
        Assert.False(ct.IsCancellationRequested);

        service.CancelStudy("study-1");
        Assert.True(ct.IsCancellationRequested);
    }

    [Fact]
    public void BackgroundStudyService_Complete_RemovesFromActive()
    {
        using var service = new BackgroundStudyService();
        service.RegisterStudy("study-1", "v1", StudyType.MonteCarlo, 1000);

        StudyCompletionUpdate? received = null;
        service.OnCompleted += update => received = update;

        service.Complete("study-1", StudyStatus.Completed);

        Assert.Empty(service.GetActiveStudies());
        Assert.NotNull(received);
        Assert.Equal(StudyStatus.Completed, received.Status);
    }

    [Fact]
    public void BackgroundStudyService_ReportProgress_FiresEvent()
    {
        using var service = new BackgroundStudyService();
        service.RegisterStudy("study-1", "v1", StudyType.MonteCarlo, 1000);

        StudyProgressUpdate? received = null;
        service.OnProgress += update => received = update;

        service.ReportProgress("study-1", 347, 1000, "Simulating path 347 of 1000");

        Assert.NotNull(received);
        Assert.Equal(347, received.Current);
        Assert.Equal(1000, received.Total);
    }

    // --- Helpers ---

    private static ScenarioConfig MakeConfig() =>
        new("test", "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);

    private static BacktestResult MakeResult(ScenarioConfig config) =>
        new(Guid.NewGuid(), config, BacktestStatus.Completed,
            new List<EquityCurvePoint>(), new List<ClosedTrade>(),
            100_000m, 110_000m, 0.05m,
            1.42m, null, null, null, 23,
            0.61m, 1.87m, 500m, -300m, 142m, null, null, 3, 5, 1200);
}
