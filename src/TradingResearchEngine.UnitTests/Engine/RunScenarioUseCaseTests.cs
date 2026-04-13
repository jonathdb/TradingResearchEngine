using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;

namespace TradingResearchEngine.UnitTests.Engine;

public class RunScenarioUseCaseTests
{
    private static ScenarioConfig ValidConfig() => new(
        "test-scenario", "Test", ReplayMode.Bar,
        "csv", new Dictionary<string, object>(),
        "test-strategy", new Dictionary<string, object>(),
        new Dictionary<string, object>(),
        "ZeroSlippageModel", "ZeroCommissionModel",
        100_000m, 0.02m, null, null, null, null);

    private static RunScenarioUseCase CreateUseCase(StrategyRegistry? registry = null)
    {
        registry ??= new StrategyRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var schemaProvider = new Mock<IStrategySchemaProvider>();
        schemaProvider.Setup(s => s.GetSchema(It.IsAny<string>()))
            .Returns(Array.Empty<StrategyParameterSchema>());
        var validator = new PreflightValidator(schemaProvider.Object);
        return new RunScenarioUseCase(registry, services,
            NullLoggerFactory.Instance.CreateLogger<RunScenarioUseCase>(),
            validator);
    }

    [Fact]
    public async Task RunAsync_MissingScenarioId_ReturnsValidationError()
    {
        var useCase = CreateUseCase();

        var config = ValidConfig() with { ScenarioId = "" };
        var result = await useCase.RunAsync(config);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors!, e => e.Contains("ScenarioId"));
    }

    [Fact]
    public async Task RunAsync_MissingStrategyType_ReturnsValidationError()
    {
        var useCase = CreateUseCase();

        var config = ValidConfig() with { StrategyType = "" };
        var result = await useCase.RunAsync(config);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors!, e => e.Contains("StrategyType"));
    }

    [Fact]
    public async Task RunAsync_UnknownStrategy_ReturnsValidationError()
    {
        var useCase = CreateUseCase();

        var config = ValidConfig() with { StrategyType = "nonexistent-strategy" };
        var result = await useCase.RunAsync(config);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors!, e => e.Contains("nonexistent-strategy"));
    }

    [Fact]
    public async Task RunAsync_NegativeInitialCash_ReturnsValidationError()
    {
        var useCase = CreateUseCase();

        var config = ValidConfig() with { InitialCash = -1m };
        var result = await useCase.RunAsync(config);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors!, e => e.Contains("InitialCash"));
    }
}
