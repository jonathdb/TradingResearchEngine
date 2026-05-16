using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.AI;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Infrastructure.AI;

namespace TradingResearchEngine.UnitTests.AI;

/// <summary>
/// Unit tests for <see cref="GeminiStrategyAssistant"/>.
/// </summary>
public sealed class GeminiStrategyAssistantTests : IDisposable
{
    private readonly Mock<IGeminiClient> _mockClient;
    private readonly StrategyRegistry _registry;
    private readonly string _tempPromptFile;
    private readonly ILogger<GeminiStrategyAssistant> _logger;

    public GeminiStrategyAssistantTests()
    {
        _mockClient = new Mock<IGeminiClient>();
        _registry = new StrategyRegistry();
        _logger = NullLogger<GeminiStrategyAssistant>.Instance;

        // Create a temp system prompt file
        _tempPromptFile = Path.GetTempFileName();
        File.WriteAllText(_tempPromptFile, "You are a trading strategy assistant.");
    }

    public void Dispose()
    {
        if (File.Exists(_tempPromptFile))
            File.Delete(_tempPromptFile);
    }

    private GeminiStrategyAssistant CreateAssistant(string? apiKey = "test-api-key")
    {
        var options = Options.Create(new GeminiOptions
        {
            ApiKey = apiKey,
            ModelName = "gemini-2.0-flash",
            MaxRetries = 2,
            SystemPromptFilePath = _tempPromptFile
        });

        return new GeminiStrategyAssistant(options, _registry, _logger, _mockClient.Object);
    }

    private static string BuildValidDraftJson(string strategyType = "moving-average-crossover")
    {
        var dto = new
        {
            strategyName = "Test Strategy",
            hypothesis = "Mean reversion works in ranging markets",
            strategyType = strategyType,
            parameters = new Dictionary<string, object>
            {
                ["fastPeriod"] = 10,
                ["slowPeriod"] = 20
            },
            suggestedRisk = new
            {
                riskParameters = new Dictionary<string, object>
                {
                    ["maxRiskPercent"] = 2.0
                },
                initialCash = 100000m,
                annualRiskFreeRate = 0.05m
            },
            rationale = "Moving averages capture trend changes effectively.",
            caveats = new[] { "May underperform in trending markets" }
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    [Fact]
    public async Task GenerateStrategyAsync_ValidJson_ReturnsCorrectAIStrategyDraft()
    {
        // Arrange
        var json = BuildValidDraftJson("moving-average-crossover");
        _mockClient
            .Setup(c => c.GenerateJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        // Register a known strategy type so validation passes
        _registry.RegisterAssembly(typeof(Core.Strategy.IStrategy).Assembly);

        var assistant = CreateAssistant();

        // Act
        var result = await assistant.GenerateStrategyAsync("Create a moving average strategy", CancellationToken.None);

        // Assert
        Assert.Equal("Test Strategy", result.StrategyName);
        Assert.Equal("Mean reversion works in ranging markets", result.Hypothesis);
        Assert.Equal("moving-average-crossover", result.StrategyType);
        Assert.Equal("Moving averages capture trend changes effectively.", result.Rationale);
        Assert.Contains("May underperform in trending markets", result.Caveats);
        Assert.Equal(SourceType.AIGenerated, result.SourceType);
        Assert.NotNull(result.Parameters);
        Assert.NotNull(result.SuggestedRisk);
    }

    [Fact]
    public async Task GenerateStrategyAsync_UnknownStrategyType_RetriesExactlyOnce()
    {
        // Arrange
        var unknownJson = BuildValidDraftJson("unknown-strategy-type");
        var validJson = BuildValidDraftJson("moving-average-crossover");

        var callCount = 0;
        _mockClient
            .Setup(c => c.GenerateJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return callCount == 1 ? unknownJson : validJson;
            });

        // Register known strategy types
        _registry.RegisterAssembly(typeof(Core.Strategy.IStrategy).Assembly);

        var assistant = CreateAssistant();

        // Act
        var result = await assistant.GenerateStrategyAsync("Create a strategy", CancellationToken.None);

        // Assert
        Assert.Equal(2, callCount); // Initial call + exactly one retry
        Assert.Equal("moving-average-crossover", result.StrategyType);
        Assert.Equal(SourceType.AIGenerated, result.SourceType);

        // Verify the retry prompt contains known names
        _mockClient.Verify(
            c => c.GenerateJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(msg => msg.Contains("unknown-strategy-type") && msg.Contains("known strategy types")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateStrategyAsync_UnknownStrategyType_BothRetryFails_AddsCaveat()
    {
        // Arrange
        var unknownJson = BuildValidDraftJson("totally-unknown-type");

        _mockClient
            .Setup(c => c.GenerateJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(unknownJson);

        // Register known strategy types
        _registry.RegisterAssembly(typeof(Core.Strategy.IStrategy).Assembly);

        var assistant = CreateAssistant();

        // Act
        var result = await assistant.GenerateStrategyAsync("Create a strategy", CancellationToken.None);

        // Assert
        Assert.Contains(result.Caveats, c => c.Contains("Unrecognised strategy type"));
        Assert.Contains(result.Caveats, c => c.Contains("totally-unknown-type"));
        Assert.Equal(SourceType.AIGenerated, result.SourceType);
    }

    [Fact]
    public async Task GenerateStrategyAsync_CancellationToken_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var assistant = CreateAssistant();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => assistant.GenerateStrategyAsync("Create a strategy", cts.Token));
    }

    [Fact]
    public async Task GenerateStrategyAsync_EmptyApiKey_ReturnsGracefulFailure()
    {
        // Arrange
        var assistant = CreateAssistant(apiKey: "");

        // Act
        var result = await assistant.GenerateStrategyAsync("test prompt", CancellationToken.None);

        // Assert
        Assert.Equal("Unavailable", result.StrategyName);
        Assert.Contains(result.Caveats, c => c.Contains("not configured"));
    }

    [Fact]
    public async Task GenerateStrategyAsync_NullApiKey_ReturnsGracefulFailure()
    {
        // Arrange
        var assistant = CreateAssistant(apiKey: null);

        // Act
        var result = await assistant.GenerateStrategyAsync("test prompt", CancellationToken.None);

        // Assert
        Assert.Equal("Unavailable", result.StrategyName);
        Assert.Contains(result.Caveats, c => c.Contains("not configured"));
    }

    [Fact]
    public async Task RefineStrategyAsync_IncludesMetricsInContext()
    {
        // Arrange
        var json = BuildValidDraftJson("moving-average-crossover");
        _mockClient
            .Setup(c => c.GenerateJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        _registry.RegisterAssembly(typeof(Core.Strategy.IStrategy).Assembly);

        var assistant = CreateAssistant();

        var currentDraft = new AIStrategyDraft(
            "Test", "Hypothesis", "moving-average-crossover",
            new Dictionary<string, object>(),
            new RiskConfig(new Dictionary<string, object>()),
            "Rationale", new List<string>(),
            SourceType: SourceType.AIGenerated);

        var backtestResult = CreateMinimalBacktestResult();

        // Act
        var result = await assistant.RefineStrategyAsync(currentDraft, backtestResult, "Improve win rate", CancellationToken.None);

        // Assert
        Assert.Equal(SourceType.AIGenerated, result.SourceType);

        // Verify the user message includes key metrics
        _mockClient.Verify(
            c => c.GenerateJsonAsync(
                It.IsAny<string>(),
                It.Is<string>(msg =>
                    msg.Contains("Sharpe") &&
                    msg.Contains("Max Drawdown") &&
                    msg.Contains("Win Rate") &&
                    msg.Contains("Trade Count") &&
                    msg.Contains("K-Ratio") &&
                    msg.Contains("Deflated Sharpe")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefineStrategyAsync_CancellationToken_ThrowsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var assistant = CreateAssistant();

        var currentDraft = new AIStrategyDraft(
            "Test", "Hypothesis", "moving-average-crossover",
            new Dictionary<string, object>(),
            new RiskConfig(new Dictionary<string, object>()),
            "Rationale", new List<string>(),
            SourceType: SourceType.AIGenerated);

        var backtestResult = CreateMinimalBacktestResult();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => assistant.RefineStrategyAsync(currentDraft, backtestResult, "Improve", cts.Token));
    }

    private static BacktestResult CreateMinimalBacktestResult()
    {
        var config = new ScenarioConfig("test", "Test", Core.Engine.ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "moving-average-crossover", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.05m, null, null, null, null);

        return new BacktestResult(
            Guid.NewGuid(), config, BacktestStatus.Completed,
            new List<Core.Portfolio.EquityCurvePoint>(),
            new List<Core.Portfolio.ClosedTrade>(),
            100_000m, 110_000m, 0.05m,
            1.5m, 2.0m, 1.2m, null, null, null, null, 2.0m, 50,
            0.6m, 1.8m, 500m, -300m, 100m,
            TimeSpan.FromDays(5), 0.95m, 3, 7, 1000,
            DeflatedSharpeRatio: 1.2m);
    }
}
