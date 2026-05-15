using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.Engine;

public class FinalValidationUseCaseTests
{
    private readonly Mock<ITestSetGuard> _testSetGuard = new();
    private readonly Mock<IStrategyRepository> _strategyRepo = new();
    private readonly Mock<ITestSetAuditLog> _auditLog = new();

    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_UserDeclines_ReturnsCancelled()
    {
        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync("version-1", userConfirmed: false);

        Assert.Equal(FinalValidationStatus.Cancelled, result.Status);
        Assert.Contains("declined", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task ExecuteAsync_TestSetAlreadyConsumed_ReturnsAlreadyConsumed()
    {
        _testSetGuard.Setup(g => g.IsConsumedAsync("version-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync("version-1", userConfirmed: true);

        Assert.Equal(FinalValidationStatus.AlreadyConsumed, result.Status);
        Assert.Contains("already consumed", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Result);
    }

    [Fact]
    public async Task ExecuteAsync_VersionNotFound_ReturnsFailed()
    {
        _testSetGuard.Setup(g => g.IsConsumedAsync("version-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _strategyRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StrategyIdentity>());

        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync("version-1", userConfirmed: true);

        Assert.Equal(FinalValidationStatus.Failed, result.Status);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_NoSealedTestSet_ReturnsFailed()
    {
        _testSetGuard.Setup(g => g.IsConsumedAsync("version-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var strategy = new StrategyIdentity("s1", "Test Strategy", new StrategyTypeId("test"), T0);
        var version = new StrategyVersion("version-1", "s1", 1,
            new Dictionary<string, object>(), MakeConfig(), T0, SealedTestSet: null);

        _strategyRepo.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { strategy });
        _strategyRepo.Setup(r => r.GetVersionsAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { version });

        var useCase = CreateUseCase();

        var result = await useCase.ExecuteAsync("version-1", userConfirmed: true);

        Assert.Equal(FinalValidationStatus.Failed, result.Status);
        Assert.Contains("sealed test set", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IsAvailableAsync_NotConsumed_ReturnsTrue()
    {
        _testSetGuard.Setup(g => g.IsConsumedAsync("version-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var useCase = CreateUseCase();

        var available = await useCase.IsAvailableAsync("version-1");

        Assert.True(available);
    }

    [Fact]
    public async Task IsAvailableAsync_Consumed_ReturnsFalse()
    {
        _testSetGuard.Setup(g => g.IsConsumedAsync("version-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var useCase = CreateUseCase();

        var available = await useCase.IsAvailableAsync("version-1");

        Assert.False(available);
    }

    [Fact]
    public async Task GetActionLabelAsync_NotConsumed_ReturnsRunLabel()
    {
        _testSetGuard.Setup(g => g.IsConsumedAsync("version-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var useCase = CreateUseCase();

        var label = await useCase.GetActionLabelAsync("version-1");

        Assert.Equal("Run Final Validation", label);
    }

    [Fact]
    public async Task GetActionLabelAsync_Consumed_ReturnsConsumedLabel()
    {
        _testSetGuard.Setup(g => g.IsConsumedAsync("version-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var useCase = CreateUseCase();

        var label = await useCase.GetActionLabelAsync("version-1");

        Assert.Equal(FinalValidationUseCase.ConsumedActionLabel, label);
    }

    [Fact]
    public void ConsequenceExplanation_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(FinalValidationUseCase.ConsequenceExplanation));
        Assert.Contains("irreversible", FinalValidationUseCase.ConsequenceExplanation,
            StringComparison.OrdinalIgnoreCase);
    }

    // --- Helpers ---

    private FinalValidationUseCase CreateUseCase()
    {
        var guard = new SealedTestSetGuard(_auditLog.Object);

        // RunScenarioUseCase is complex to mock; we use a minimal setup that will
        // return failures for missing strategies (which is fine for our gate tests)
        var registry = new StrategyRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var schemaProvider = new Mock<IStrategySchemaProvider>();
        schemaProvider.Setup(s => s.GetSchema(It.IsAny<string>()))
            .Returns(Array.Empty<StrategyParameterSchema>());
        var validator = new PreflightValidator(schemaProvider.Object);
        var engineFactory = new Mock<IBacktestEngineFactory>();
        var runScenario = new RunScenarioUseCase(
            registry, services,
            NullLoggerFactory.Instance.CreateLogger<RunScenarioUseCase>(),
            validator,
            engineFactory.Object);

        return new FinalValidationUseCase(
            runScenario,
            _strategyRepo.Object,
            guard,
            _testSetGuard.Object,
            CreateChecklistService(),
            NullLoggerFactory.Instance.CreateLogger<FinalValidationUseCase>());
    }

    private ResearchChecklistService CreateChecklistService()
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
        strategyRepo.Setup(r => r.GetVersionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StrategyVersion?)null);

        var evalRepo = new Mock<IPropFirmEvaluationRepository>();
        evalRepo.Setup(r => r.HasCompletedEvaluationAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        return new ResearchChecklistService(
            resultRepo.Object, studyRepo.Object, strategyRepo.Object, evalRepo.Object);
    }

    private static ScenarioConfig MakeConfig() => new(
        "test-scenario", "Test", ReplayMode.Bar,
        "csv", new Dictionary<string, object>(),
        "test-strategy", new Dictionary<string, object>(),
        new Dictionary<string, object>(),
        "ZeroSlippageModel", "ZeroCommissionModel",
        100_000m, 0.02m, null, null, null, null);
}
