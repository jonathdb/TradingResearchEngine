using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TradingResearchEngine.Application.Portfolio;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;

namespace TradingResearchEngine.IntegrationTests.Api;

/// <summary>
/// Integration tests for V8 API endpoints using WebApplicationFactory.
/// Tests export, portfolio run, and portfolio sweep endpoints.
/// Requirements: 7.1, 7.2, 23.1, 23.2
/// </summary>
public class V8EndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public V8EndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static readonly string SpyDataPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "data", "spy-daily.csv"));

    // ─────────────────────────────────────────────────────────────────────────────
    // POST /strategies/{versionId}/export
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportStrategy_WithValidVersionId_Returns200WithCode()
    {
        // Arrange: first create a strategy version via the repository
        using var scope = _factory.Services.CreateScope();
        var strategyRepo = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();

        var strategyId = "test-strategy-" + Guid.NewGuid().ToString("N")[..8];
        var versionId = "test-version-" + Guid.NewGuid().ToString("N")[..8];

        var identity = new StrategyIdentity(
            StrategyId: strategyId,
            StrategyName: "Test MA Crossover",
            StrategyType: "moving-average-crossover",
            CreatedAt: DateTimeOffset.UtcNow,
            Description: "Test strategy for export");
        await strategyRepo.SaveAsync(identity);

        var version = new StrategyVersion(
            StrategyVersionId: versionId,
            StrategyId: strategyId,
            VersionNumber: 1,
            Parameters: new Dictionary<string, object>
            {
                ["FastPeriod"] = 10,
                ["SlowPeriod"] = 30
            },
            BaseScenarioConfig: new ScenarioConfig(
                ScenarioId: "test",
                Description: "Test",
                ReplayMode: ReplayMode.Bar,
                DataProviderType: "csv",
                DataProviderOptions: new Dictionary<string, object>(),
                StrategyType: "moving-average-crossover",
                StrategyParameters: new Dictionary<string, object>
                {
                    ["FastPeriod"] = 10,
                    ["SlowPeriod"] = 30
                },
                RiskParameters: new Dictionary<string, object>(),
                SlippageModelType: "zero",
                CommissionModelType: "zero",
                InitialCash: 100_000m,
                AnnualRiskFreeRate: 0.05m,
                RandomSeed: null,
                ResearchWorkflowType: null,
                ResearchWorkflowOptions: null,
                PropFirmOptions: null),
            CreatedAt: DateTimeOffset.UtcNow);
        await strategyRepo.SaveVersionAsync(version);

        // Act
        var response = await _client.PostAsync(
            $"/strategies/{versionId}/export?format=MQL4", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
        Assert.Contains("OnInit()", content);
        Assert.Contains("OnTick()", content);
    }

    [Fact]
    public async Task ExportStrategy_WithInvalidVersionId_Returns400()
    {
        // Act
        var response = await _client.PostAsync(
            "/strategies/nonexistent-version-id/export?format=MQL4", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("versionId", content);
    }

    [Fact]
    public async Task ExportStrategy_WithMissingFormat_Returns400()
    {
        // Act
        var response = await _client.PostAsync(
            "/strategies/any-version/export", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("format", content);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // POST /portfolios/run
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PortfolioRun_WithValidConfig_Returns200WithResult()
    {
        // Arrange
        var config = new PortfolioBacktestConfig(
            Symbols: new List<DataConfig>
            {
                new DataConfig(
                    DataProviderType: "csv",
                    DataProviderOptions: new Dictionary<string, object>
                    {
                        ["FilePath"] = SpyDataPath,
                        ["Symbol"] = "SPY",
                        ["Interval"] = "1D"
                    },
                    Timeframe: "Daily",
                    BarsPerYear: 252)
            },
            Strategies: new List<StrategyConfig>
            {
                new StrategyConfig("moving-average-crossover", new Dictionary<string, object>
                {
                    ["FastPeriod"] = 10,
                    ["SlowPeriod"] = 30
                })
            },
            PortfolioRisk: new PortfolioRiskConfig(),
            Execution: new ExecutionConfig(
                SlippageModelType: "Zero",
                CommissionModelType: "Zero"),
            InitialCash: 100_000m,
            Seed: 42,
            Timeframe: "Daily");

        // Act
        var response = await _client.PostAsJsonAsync("/portfolios/run", config);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);

        // Verify it's valid JSON with expected structure
        var doc = JsonDocument.Parse(content);
        Assert.True(doc.RootElement.TryGetProperty("symbolResults", out _) ||
                    doc.RootElement.TryGetProperty("SymbolResults", out _),
            "Response should contain SymbolResults");
    }

    [Fact]
    public async Task PortfolioRun_WithEmptySymbols_Returns400()
    {
        // Arrange
        var config = new PortfolioBacktestConfig(
            Symbols: new List<DataConfig>(),
            Strategies: new List<StrategyConfig>
            {
                new StrategyConfig("moving-average-crossover", new Dictionary<string, object>())
            },
            PortfolioRisk: new PortfolioRiskConfig(),
            Execution: new ExecutionConfig(),
            InitialCash: 100_000m);

        // Act
        var response = await _client.PostAsJsonAsync("/portfolios/run", config);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // POST /portfolios/sweep
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PortfolioSweep_WithValidConfig_Returns200WithList()
    {
        // Arrange
        var baseConfig = new PortfolioBacktestConfig(
            Symbols: new List<DataConfig>
            {
                new DataConfig(
                    DataProviderType: "csv",
                    DataProviderOptions: new Dictionary<string, object>
                    {
                        ["FilePath"] = SpyDataPath,
                        ["Symbol"] = "SPY",
                        ["Interval"] = "1D"
                    },
                    Timeframe: "Daily",
                    BarsPerYear: 252)
            },
            Strategies: new List<StrategyConfig>
            {
                new StrategyConfig("moving-average-crossover", new Dictionary<string, object>
                {
                    ["FastPeriod"] = 10,
                    ["SlowPeriod"] = 30
                })
            },
            PortfolioRisk: new PortfolioRiskConfig(),
            Execution: new ExecutionConfig(
                SlippageModelType: "Zero",
                CommissionModelType: "Zero"),
            InitialCash: 100_000m,
            Seed: 42,
            Timeframe: "Daily");

        var request = new
        {
            Config = baseConfig,
            Variations = new[]
            {
                new { InitialCash = (decimal?)50_000m, PortfolioRisk = (PortfolioRiskConfig?)null },
                new { InitialCash = (decimal?)200_000m, PortfolioRisk = (PortfolioRiskConfig?)null }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/portfolios/sweep", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);

        // Verify it's a JSON array
        var doc = JsonDocument.Parse(content);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(2, doc.RootElement.GetArrayLength());
    }
}
