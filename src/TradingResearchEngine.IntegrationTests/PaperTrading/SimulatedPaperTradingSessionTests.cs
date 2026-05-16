using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.PaperTrading;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.PaperTrading;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Strategy;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.PaperTrading;

/// <summary>
/// Integration tests verifying that paper trading replay produces metrics equivalent
/// to a standard backtest over the same data with the same strategy configuration.
/// Requirements: 20.1, 20.2, 20.3, 20.4, 20.5
/// </summary>
public class SimulatedPaperTradingSessionTests
{
    private const decimal Tolerance = 1e-6m;

    private static readonly string SpyDataPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "data", "spy-daily.csv");

    private static ScenarioConfig CreateConfig(
        string strategyType = "moving-average-crossover",
        Dictionary<string, object>? strategyParams = null,
        decimal initialCash = 100_000m) => new(
        ScenarioId: "paper-trading-integration-test",
        Description: "Paper trading vs backtest metric equivalence",
        ReplayMode: ReplayMode.Bar,
        DataProviderType: "csv",
        DataProviderOptions: new Dictionary<string, object>
        {
            ["FilePath"] = SpyDataPath,
            ["Symbol"] = "SPY",
            ["Interval"] = "1D"
        },
        StrategyType: strategyType,
        StrategyParameters: strategyParams ?? new Dictionary<string, object>
        {
            ["FastPeriod"] = 10,
            ["SlowPeriod"] = 30
        },
        RiskParameters: new Dictionary<string, object>(),
        SlippageModelType: "Zero",
        CommissionModelType: "Zero",
        InitialCash: initialCash,
        AnnualRiskFreeRate: 0.05m,
        RandomSeed: null,
        ResearchWorkflowType: null,
        ResearchWorkflowOptions: null,
        PropFirmOptions: null,
        FillMode: FillMode.SameBarClose);

    /// <summary>
    /// Runs a standard backtest using BacktestEngine with the given strategy and config.
    /// Uses SameBarClose fill mode to match paper trading session behavior.
    /// </summary>
    private static async Task<BacktestResult> RunStandardBacktestAsync(
        IStrategy strategy, ScenarioConfig config)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var csvLogger = loggerFactory.CreateLogger<CsvDataProvider>();
        var dataProvider = new CsvDataProvider(Path.GetFullPath(SpyDataPath), csvLogger);

        var riskOptions = Options.Create(new RiskOptions());
        var riskLogger = loggerFactory.CreateLogger<DefaultRiskLayer>();
        var riskLayer = new DefaultRiskLayer(riskOptions, riskLogger);

        var slippage = new ZeroSlippageModel();
        var commission = new ZeroCommissionModel();
        var execLogger = loggerFactory.CreateLogger<SimulatedExecutionHandler>();
        var executionHandler = new SimulatedExecutionHandler(slippage, commission, execLogger);

        var engineLogger = loggerFactory.CreateLogger<BacktestEngine>();
        var engine = new BacktestEngine(
            dataProvider, strategy, riskLayer, executionHandler, engineLogger);

        return await engine.RunAsync(config);
    }

    /// <summary>
    /// Runs a paper trading session to completion using the same data and strategy.
    /// The streaming provider wraps the CSV data provider for immediate playback.
    /// </summary>
    private static async Task<PaperTradingResult> RunPaperTradingSessionAsync(
        IStrategy strategy, ScenarioConfig config,
        Action<IObservable<PaperBarEvent>>? barSubscriber = null,
        Action<IObservable<PaperTradeEvent>>? tradeSubscriber = null)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var csvLogger = loggerFactory.CreateLogger<CsvDataProvider>();
        var csvProvider = new CsvDataProvider(Path.GetFullPath(SpyDataPath), csvLogger);

        // Wrap CSV provider in a streaming provider with instant playback (no delay)
        var streamingProvider = new PollingStreamingDataProvider(
            csvProvider,
            pollInterval: TimeSpan.FromMilliseconds(1),
            speedRatio: 1000.0); // Very fast playback for testing

        var riskOptions = Options.Create(new RiskOptions());
        var riskLogger = loggerFactory.CreateLogger<DefaultRiskLayer>();
        var riskLayer = new DefaultRiskLayer(riskOptions, riskLogger);

        var slippage = new ZeroSlippageModel();
        var commission = new ZeroCommissionModel();
        var execLogger = loggerFactory.CreateLogger<SimulatedExecutionHandler>();
        var executionHandler = new SimulatedExecutionHandler(slippage, commission, execLogger);

        var mockRepo = new Mock<IRepository<PaperSessionRecord>>();
        mockRepo.Setup(r => r.SaveAsync(It.IsAny<PaperSessionRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var optionsMonitor = new TestOptionsMonitor(
            new PaperTradingOptions { PollingInterval = TimeSpan.FromMilliseconds(1) });

        var sessionLogger = loggerFactory.CreateLogger<SimulatedPaperTradingSession>();

        var session = new SimulatedPaperTradingSession(
            streamingProvider,
            strategy,
            riskLayer,
            executionHandler,
            slippage,
            commission,
            mockRepo.Object,
            optionsMonitor,
            sessionLogger);

        // Subscribe to streams if requested (for faulting subscriber test)
        barSubscriber?.Invoke(session.BarStream);
        tradeSubscriber?.Invoke(session.TradeStream);

        await session.StartAsync(config, CancellationToken.None);

        // Wait for all bars to be processed (the streaming provider will complete)
        // Poll until the session finishes processing or timeout
        var timeout = TimeSpan.FromSeconds(60);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            await Task.Delay(100);
            // Check if the processing task has completed by trying to stop
            // The session will be in Running state until all bars are consumed
            if (session.Status == PaperTradingStatus.Error)
                break;

            // Try to detect completion: the streaming provider will exhaust bars
            // and the processing task will complete naturally
            try
            {
                // Attempt stop — if processing is done, this succeeds immediately
                var result = await session.StopAsync();
                session.Dispose();
                return result;
            }
            catch (InvalidOperationException)
            {
                // Session might already be stopped or in transition
                continue;
            }
        }

        // Fallback: force stop after timeout
        var finalResult = await session.StopAsync();
        session.Dispose();
        return finalResult;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test: Replay-to-completion metrics match standard backtest
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayToCompletion_MetricsMatchStandardBacktest()
    {
        // Arrange: use MovingAverageCrossover strategy with same parameters
        var config = CreateConfig();
        var backtestStrategy = new MovingAverageCrossoverStrategy(fastPeriod: 10, slowPeriod: 30);
        var paperStrategy = new MovingAverageCrossoverStrategy(fastPeriod: 10, slowPeriod: 30);

        // Act: run both paths
        var backtestResult = await RunStandardBacktestAsync(backtestStrategy, config);
        var paperResult = await RunPaperTradingSessionAsync(paperStrategy, config);

        // Assert: both completed successfully
        Assert.Equal(BacktestStatus.Completed, backtestResult.Status);
        Assert.Equal(PaperTradingStatus.Stopped, paperResult.FinalStatus);

        var paperMetrics = paperResult.EquivalentBacktestResult;

        // Assert: key metrics match within tolerance
        Assert.Equal(backtestResult.TotalTrades, paperMetrics.TotalTrades);

        AssertDecimalClose(backtestResult.StartEquity, paperMetrics.StartEquity, "StartEquity");
        AssertDecimalClose(backtestResult.EndEquity, paperMetrics.EndEquity, "EndEquity");
        AssertDecimalClose(backtestResult.MaxDrawdown, paperMetrics.MaxDrawdown, "MaxDrawdown");

        AssertNullableDecimalClose(backtestResult.SharpeRatio, paperMetrics.SharpeRatio, "SharpeRatio");
        AssertNullableDecimalClose(backtestResult.SortinoRatio, paperMetrics.SortinoRatio, "SortinoRatio");
        AssertNullableDecimalClose(backtestResult.WinRate, paperMetrics.WinRate, "WinRate");
        AssertNullableDecimalClose(backtestResult.ProfitFactor, paperMetrics.ProfitFactor, "ProfitFactor");
        AssertNullableDecimalClose(backtestResult.Expectancy, paperMetrics.Expectancy, "Expectancy");

        // Equity curve lengths should match
        Assert.Equal(backtestResult.EquityCurve.Count, paperMetrics.EquityCurve.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Test: Faulting subscriber does not terminate session (EmitSafely resilience)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FaultingSubscriber_SessionContinues_MetricsStillMatch()
    {
        // Arrange: use MovingAverageCrossover strategy
        var config = CreateConfig();
        var backtestStrategy = new MovingAverageCrossoverStrategy(fastPeriod: 10, slowPeriod: 30);
        var paperStrategy = new MovingAverageCrossoverStrategy(fastPeriod: 10, slowPeriod: 30);

        // Run backtest for reference
        var backtestResult = await RunStandardBacktestAsync(backtestStrategy, config);

        // Run paper trading with a faulting subscriber that throws on every bar
        int barCount = 0;
        var paperResult = await RunPaperTradingSessionAsync(
            paperStrategy,
            config,
            barSubscriber: barStream =>
            {
                barStream.Subscribe(
                    onNext: _ =>
                    {
                        Interlocked.Increment(ref barCount);
                        throw new InvalidOperationException("Simulated subscriber fault");
                    },
                    onError: _ => { },
                    onCompleted: () => { });
            });

        // Assert: session completed despite subscriber faults
        Assert.Equal(PaperTradingStatus.Stopped, paperResult.FinalStatus);
        Assert.True(barCount > 0, "Faulting subscriber should have been called at least once");

        var paperMetrics = paperResult.EquivalentBacktestResult;

        // Assert: metrics still match — the faulting subscriber did not affect execution
        Assert.Equal(backtestResult.TotalTrades, paperMetrics.TotalTrades);
        AssertDecimalClose(backtestResult.EndEquity, paperMetrics.EndEquity, "EndEquity");
        AssertDecimalClose(backtestResult.MaxDrawdown, paperMetrics.MaxDrawdown, "MaxDrawdown");
        AssertNullableDecimalClose(backtestResult.WinRate, paperMetrics.WinRate, "WinRate");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Assertion helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private static void AssertDecimalClose(decimal expected, decimal actual, string label)
    {
        var diff = Math.Abs(expected - actual);
        Assert.True(diff <= Tolerance,
            $"{label}: expected {expected} but got {actual} (diff={diff}, tolerance={Tolerance})");
    }

    private static void AssertNullableDecimalClose(decimal? expected, decimal? actual, string label)
    {
        if (expected is null && actual is null) return;
        if (expected is null || actual is null)
        {
            Assert.Fail($"{label}: one is null (expected={expected}, actual={actual})");
            return;
        }
        AssertDecimalClose(expected.Value, actual.Value, label);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helper: TestOptionsMonitor for paper trading options
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class TestOptionsMonitor(PaperTradingOptions initialValue) : IOptionsMonitor<PaperTradingOptions>
    {
        public PaperTradingOptions CurrentValue { get; } = initialValue;

        public PaperTradingOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<PaperTradingOptions, string?> listener) => null;
    }
}
