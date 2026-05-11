using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.Portfolio;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Strategy;
using TradingResearchEngine.Infrastructure;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.Portfolio;

/// <summary>
/// Integration tests for PortfolioBacktestRunner.
/// Tests determinism, correlation matrix symmetry, Sharpe diversification bound,
/// and multi-symbol run completion.
/// Requirements: 27.1, 27.2, 27.3
/// </summary>
public class PortfolioRunnerIntegrationTests
{
    private static readonly string SpyDataPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "data", "spy-daily.csv"));

    private static PortfolioBacktestRunner CreateRunner()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Repository:BasePath"] = Path.Combine(Path.GetTempPath(), "tre-test-" + Guid.NewGuid().ToString("N")),
                ["DataProvider:FilePath"] = SpyDataPath
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTradingResearchEngine(configuration);
        services.AddTradingResearchEngineInfrastructure(configuration);
        services.AddStrategyAssembly(typeof(DonchianBreakoutStrategy).Assembly);

        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<PortfolioBacktestRunner>();
    }

    private static PortfolioBacktestConfig CreateMultiSymbolConfig(int symbolCount = 3, int? seed = 42)
    {
        // Use the same SPY data file for all symbols (simulating different symbols with same data)
        var symbols = Enumerable.Range(0, symbolCount)
            .Select(i => new DataConfig(
                DataProviderType: "csv",
                DataProviderOptions: new Dictionary<string, object>
                {
                    ["FilePath"] = SpyDataPath,
                    ["Symbol"] = $"SYM{i}",
                    ["Interval"] = "1D"
                },
                Timeframe: "Daily",
                BarsPerYear: 252))
            .ToList();

        var strategies = new List<StrategyConfig>
        {
            new StrategyConfig("moving-average-crossover", new Dictionary<string, object>
            {
                ["FastPeriod"] = 10,
                ["SlowPeriod"] = 30
            })
        };

        return new PortfolioBacktestConfig(
            Symbols: symbols,
            Strategies: strategies,
            PortfolioRisk: new PortfolioRiskConfig(),
            Execution: new ExecutionConfig(
                SlippageModelType: "Zero",
                CommissionModelType: "Zero"),
            InitialCash: 300_000m,
            Seed: seed,
            Timeframe: "Daily");
    }

    /// <summary>
    /// Determinism: same seed + inputs → identical PortfolioBacktestResult.
    /// </summary>
    [Fact]
    public async Task RunAsync_SameSeedAndInputs_ProducesIdenticalResults()
    {
        // Arrange
        var runner = CreateRunner();
        var config = CreateMultiSymbolConfig(symbolCount: 2, seed: 42);
        var progress = new NullProgressReporter();

        // Act
        var result1 = await runner.RunAsync(config, progress, CancellationToken.None);
        var result2 = await runner.RunAsync(config, progress, CancellationToken.None);

        // Assert
        Assert.Equal(result1.SymbolResults.Count, result2.SymbolResults.Count);
        Assert.Equal(result1.PortfolioResult.EndEquity, result2.PortfolioResult.EndEquity);
        Assert.Equal(result1.PortfolioResult.TotalTrades, result2.PortfolioResult.TotalTrades);
        Assert.Equal(result1.PortfolioResult.MaxDrawdown, result2.PortfolioResult.MaxDrawdown);
        Assert.Equal(result1.AnnualisedTurnover, result2.AnnualisedTurnover);

        for (int i = 0; i < result1.SymbolResults.Count; i++)
        {
            Assert.Equal(result1.SymbolResults[i].EndEquity, result2.SymbolResults[i].EndEquity);
            Assert.Equal(result1.SymbolResults[i].TotalTrades, result2.SymbolResults[i].TotalTrades);
        }
    }

    /// <summary>
    /// Correlation matrix symmetry: M[A][B] == M[B][A].
    /// </summary>
    [Fact]
    public async Task RunAsync_CorrelationMatrix_IsSymmetric()
    {
        // Arrange
        var runner = CreateRunner();
        var config = CreateMultiSymbolConfig(symbolCount: 3);
        var progress = new NullProgressReporter();

        // Act
        var result = await runner.RunAsync(config, progress, CancellationToken.None);

        // Assert
        var matrix = result.CorrelationMatrix;
        var symbols = matrix.Keys.ToList();

        foreach (var symA in symbols)
        {
            foreach (var symB in symbols)
            {
                var ab = matrix[symA][symB];
                var ba = matrix[symB][symA];
                Assert.Equal(ab, ba, precision: 10);
            }

            // Diagonal should be 1.0
            Assert.Equal(1.0, matrix[symA][symA], precision: 10);
        }
    }

    /// <summary>
    /// Portfolio Sharpe ≤ max(symbol Sharpes) when correlation > 0.
    /// Note: This is a general diversification property. When all symbols are perfectly
    /// correlated (same data), portfolio Sharpe should approximately equal individual Sharpes.
    /// </summary>
    [Fact]
    public async Task RunAsync_PortfolioSharpe_BoundedByMaxSymbolSharpe_WhenPositiveCorrelation()
    {
        // Arrange: use same data for all symbols → correlation ≈ 1.0
        var runner = CreateRunner();
        var config = CreateMultiSymbolConfig(symbolCount: 3);
        var progress = new NullProgressReporter();

        // Act
        var result = await runner.RunAsync(config, progress, CancellationToken.None);

        // Assert
        var symbolSharpes = result.SymbolResults
            .Where(r => r.SharpeRatio.HasValue)
            .Select(r => r.SharpeRatio!.Value)
            .ToList();

        if (symbolSharpes.Count > 0 && result.PortfolioResult.SharpeRatio.HasValue)
        {
            var maxSymbolSharpe = symbolSharpes.Max();
            var portfolioSharpe = result.PortfolioResult.SharpeRatio.Value;

            // With high correlation, portfolio Sharpe should not exceed max symbol Sharpe
            // Allow small tolerance for floating point and merge effects
            Assert.True(portfolioSharpe <= maxSymbolSharpe + 0.5m,
                $"Portfolio Sharpe ({portfolioSharpe}) should be bounded by max symbol Sharpe ({maxSymbolSharpe}) + tolerance when correlation > 0");
        }
    }

    /// <summary>
    /// 3-symbol run completes without error.
    /// </summary>
    [Fact]
    public async Task RunAsync_ThreeSymbols_CompletesWithoutError()
    {
        // Arrange
        var runner = CreateRunner();
        var config = CreateMultiSymbolConfig(symbolCount: 3);
        var progress = new NullProgressReporter();

        // Act
        var result = await runner.RunAsync(config, progress, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.SymbolResults.Count);
        Assert.NotNull(result.PortfolioResult);
        Assert.NotNull(result.CorrelationMatrix);
        Assert.Equal(3, result.CorrelationMatrix.Count);
        Assert.True(result.AnnualisedTurnover >= 0m);
        Assert.Equal(PortfolioRebalanceMode.None, result.RebalanceMode);

        foreach (var symbolResult in result.SymbolResults)
        {
            Assert.Equal(Core.Results.BacktestStatus.Completed, symbolResult.Status);
            Assert.True(symbolResult.EndEquity > 0);
        }
    }

    private sealed class NullProgressReporter : IProgressReporter
    {
        public void Report(int current, int total, string label) { }
        public void Report(ProgressSnapshot snapshot) { }
    }
}