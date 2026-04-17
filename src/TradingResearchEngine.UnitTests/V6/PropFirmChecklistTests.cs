using Moq;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.V6;

public class PropFirmChecklistTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HasCompletedEvaluation_True_UnlocksEighthItem()
    {
        var (service, evalRepo) = CreateService(propFirmEval: true);

        var checklist = await service.ComputeAsync("v1");

        Assert.True(checklist.PropFirmEvaluation);
    }

    [Fact]
    public async Task HasCompletedEvaluation_False_EighthItemFalse()
    {
        var (service, _) = CreateService(propFirmEval: false);

        var checklist = await service.ComputeAsync("v1");

        Assert.False(checklist.PropFirmEvaluation);
    }

    [Fact]
    public async Task ConfidenceReachesHigh_With8Of9Items()
    {
        // All 8 original items complete + CPCV not done = 8/9 = HIGH
        var (service, _) = CreateServiceWithAllStudies(propFirmEval: true, cpcvDone: false);

        var checklist = await service.ComputeAsync("v1");

        Assert.Equal(8, checklist.PassedCount);
        Assert.Equal(9, checklist.TotalChecks);
        Assert.Equal("HIGH", checklist.ConfidenceLevel);
    }

    [Fact]
    public async Task ConfidenceIsMedium_With7Of9Items()
    {
        // 7 items complete = MEDIUM (needs ≥ 8 for HIGH)
        var (service, _) = CreateServiceWithAllStudies(propFirmEval: false, cpcvDone: false);

        var checklist = await service.ComputeAsync("v1");

        Assert.Equal(7, checklist.PassedCount);
        Assert.Equal("MEDIUM", checklist.ConfidenceLevel);
    }

    [Fact]
    public async Task TotalChecks_Is9()
    {
        var (service, _) = CreateService(propFirmEval: false);

        var checklist = await service.ComputeAsync("v1");

        Assert.Equal(9, checklist.TotalChecks);
    }

    [Fact]
    public async Task CpcvDone_WhenCpcvStudyCompleted()
    {
        var (service, _) = CreateServiceWithStudies(
            new StudyRecord("s1", "v1", StudyType.CombinatorialPurgedCV, StudyStatus.Completed, T0));

        var checklist = await service.ComputeAsync("v1");

        Assert.True(checklist.CpcvDone);
    }

    [Fact]
    public async Task CpcvDone_False_WhenNoCpcvStudy()
    {
        var (service, _) = CreateService(propFirmEval: false);

        var checklist = await service.ComputeAsync("v1");

        Assert.False(checklist.CpcvDone);
    }

    // --- Helpers ---

    private static (ResearchChecklistService service, Mock<IPropFirmEvaluationRepository> evalRepo)
        CreateService(bool propFirmEval)
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
            .ReturnsAsync(propFirmEval);

        var service = new ResearchChecklistService(
            resultRepo.Object, studyRepo.Object, strategyRepo.Object, evalRepo.Object);
        return (service, evalRepo);
    }

    private static (ResearchChecklistService service, Mock<IPropFirmEvaluationRepository> evalRepo)
        CreateServiceWithStudies(params StudyRecord[] studies)
    {
        var resultRepo = new Mock<IBacktestResultRepository>();
        resultRepo.Setup(r => r.ListByVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestResult>());

        var studyRepo = new Mock<IStudyRepository>();
        studyRepo.Setup(r => r.ListByVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(studies.ToList());

        var strategyRepo = new Mock<IStrategyRepository>();
        strategyRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StrategyIdentity>());

        var evalRepo = new Mock<IPropFirmEvaluationRepository>();
        evalRepo.Setup(r => r.HasCompletedEvaluationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = new ResearchChecklistService(
            resultRepo.Object, studyRepo.Object, strategyRepo.Object, evalRepo.Object);
        return (service, evalRepo);
    }

    private static (ResearchChecklistService service, Mock<IPropFirmEvaluationRepository> evalRepo)
        CreateServiceWithAllStudies(bool propFirmEval, bool cpcvDone)
    {
        var config = new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);
        var completedRun = new BacktestResult(Guid.NewGuid(), config, BacktestStatus.Completed,
            new List<EquityCurvePoint>(), new List<ClosedTrade>(),
            100_000m, 110_000m, 0.05m,
            1.42m, null, null, null, 23,
            0.61m, 1.87m, 500m, -300m, 142m, null, null, 3, 5, 1200)
            with { StrategyVersionId = "v1" };

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
        if (cpcvDone)
            studies.Add(new("s6", "v1", StudyType.CombinatorialPurgedCV, StudyStatus.Completed, T0));

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
        strategyRepo.Setup(r => r.GetVersionAsync("v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(version);

        var evalRepo = new Mock<IPropFirmEvaluationRepository>();
        evalRepo.Setup(r => r.HasCompletedEvaluationAsync("v1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(propFirmEval);

        var service = new ResearchChecklistService(
            resultRepo.Object, studyRepo.Object, strategyRepo.Object, evalRepo.Object);
        return (service, evalRepo);
    }
}
