using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Infrastructure;
using TradingResearchEngine.Infrastructure.Persistence;

namespace TradingResearchEngine.IntegrationTests.Research;

/// <summary>
/// End-to-end integration test for the walk-forward → OOS → persist cycle.
/// Loads real CSV data, runs WalkForwardWorkflow with a simple moving-average strategy,
/// verifies OOS windows are populated, persists the result, and retrieves it.
/// Review: Opp 11
/// </summary>
public class WalkForwardIntegrationTests : IDisposable
{
    private static readonly string SpyDataPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "data", "spy-daily.csv"));

    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;

    public WalkForwardIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-wf-test-{Guid.NewGuid():N}");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Repository:BaseDirectory"] = _tempDir,
                ["DataProvider:FilePath"] = SpyDataPath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTradingResearchEngine(configuration);
        services.AddTradingResearchEngineInfrastructure(configuration);
        services.AddStrategyAssembly(typeof(MovingAverageCrossoverStrategy).Assembly);

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task WalkForward_WithCsvData_ProducesOosWindows_AndPersistsResult()
    {
        // Arrange: configure a walk-forward run with short windows to fit within ~500 bars of SPY data
        // SPY data spans 2023-01-03 to ~2025-01-02 (500 trading days)
        var workflow = _serviceProvider.GetRequiredService<WalkForwardWorkflow>();

        var config = new ScenarioConfig(
            ScenarioId: Guid.NewGuid().ToString(),
            Description: "WalkForward Integration Test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>
            {
                ["FilePath"] = SpyDataPath,
                ["Symbol"] = "SPY",
                ["Interval"] = "1D",
                ["From"] = new DateTimeOffset(2023, 1, 3, 0, 0, 0, TimeSpan.Zero),
                ["To"] = new DateTimeOffset(2024, 6, 30, 0, 0, 0, TimeSpan.Zero)
            },
            StrategyType: "moving-average-crossover",
            StrategyParameters: new Dictionary<string, object>
            {
                ["FastPeriod"] = 10,
                ["SlowPeriod"] = 30
            },
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: "Zero",
            CommissionModelType: "Zero",
            InitialCash: 100_000m,
            AnnualRiskFreeRate: 0.02m,
            RandomSeed: 42,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null);

        var options = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(120),
            OutOfSampleLength = TimeSpan.FromDays(60),
            StepSize = TimeSpan.FromDays(60)
        };

        // Act: run walk-forward workflow
        var result = await workflow.RunAsync(config, options, CancellationToken.None);

        // Assert: OOS windows are populated
        Assert.NotNull(result);
        Assert.NotEmpty(result.Windows);
        Assert.True(result.Windows.Count >= 2,
            $"Expected at least 2 walk-forward windows, got {result.Windows.Count}");

        foreach (var window in result.Windows)
        {
            // Each window has both IS and OOS results
            Assert.NotNull(window.InSampleResult);
            Assert.NotNull(window.OutOfSampleResult);

            // OOS result should have completed status
            Assert.Equal(BacktestStatus.Completed, window.OutOfSampleResult.Status);

            // OOS result should have an equity curve
            Assert.NotEmpty(window.OutOfSampleResult.EquityCurve);

            // Selected parameters should be populated
            Assert.NotNull(window.SelectedParameters);
            Assert.NotEmpty(window.SelectedParameters);
        }

        // Verify analytics are computed
        Assert.NotNull(result.Analytics);
        Assert.True(result.Analytics.OosProfitabilityRate >= 0m && result.Analytics.OosProfitabilityRate <= 1m);
        Assert.NotEmpty(result.Analytics.ConcatenatedOosEquityCurve);

        // Verify concatenated OOS equity curve
        Assert.NotEmpty(result.ConcatenatedOosEquityCurve);

        // Act: persist the first OOS result and retrieve it
        var repo = _serviceProvider.GetRequiredService<IRepository<BacktestResult>>();
        var oosResult = result.Windows[0].OutOfSampleResult;

        await repo.SaveAsync(oosResult);
        var retrieved = await repo.GetByIdAsync(oosResult.Id);

        // Assert: persisted result is retrievable and matches
        Assert.NotNull(retrieved);
        Assert.Equal(oosResult.Id, retrieved!.Id);
        Assert.Equal(oosResult.EndEquity, retrieved.EndEquity);
        Assert.Equal(oosResult.TotalTrades, retrieved.TotalTrades);
        Assert.Equal(oosResult.Status, retrieved.Status);
    }
}
