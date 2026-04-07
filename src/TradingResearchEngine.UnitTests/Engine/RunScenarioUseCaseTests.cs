using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public async Task RunAsync_MissingScenarioId_ReturnsValidationError()
    {
        var registry = new StrategyRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var useCase = new RunScenarioUseCase(registry, services,
            NullLoggerFactory.Instance.CreateLogger<RunScenarioUseCase>());

        var config = ValidConfig() with { ScenarioId = "" };
        var result = await useCase.RunAsync(config);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors!, e => e.Contains("ScenarioId"));
    }

    [Fact]
    public async Task RunAsync_MissingStrategyType_ReturnsValidationError()
    {
        var registry = new StrategyRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var useCase = new RunScenarioUseCase(registry, services,
            NullLoggerFactory.Instance.CreateLogger<RunScenarioUseCase>());

        var config = ValidConfig() with { StrategyType = "" };
        var result = await useCase.RunAsync(config);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors!, e => e.Contains("StrategyType"));
    }

    [Fact]
    public async Task RunAsync_UnknownStrategy_ReturnsValidationError()
    {
        var registry = new StrategyRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var useCase = new RunScenarioUseCase(registry, services,
            NullLoggerFactory.Instance.CreateLogger<RunScenarioUseCase>());

        var config = ValidConfig() with { StrategyType = "nonexistent-strategy" };
        var result = await useCase.RunAsync(config);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors!, e => e.Contains("nonexistent-strategy"));
    }

    [Fact]
    public async Task RunAsync_NegativeInitialCash_ReturnsValidationError()
    {
        var registry = new StrategyRegistry();
        var services = new ServiceCollection().BuildServiceProvider();
        var useCase = new RunScenarioUseCase(registry, services,
            NullLoggerFactory.Instance.CreateLogger<RunScenarioUseCase>());

        var config = ValidConfig() with { InitialCash = -1m };
        var result = await useCase.RunAsync(config);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors!, e => e.Contains("InitialCash"));
    }
}
