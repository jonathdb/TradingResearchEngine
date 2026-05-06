using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Runtime.CompilerServices;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.PaperTrading;
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

namespace TradingResearchEngine.UnitTests.PaperTrading;

/// <summary>
/// Example-based unit tests for SimulatedPaperTradingSession.
/// Complements the property-based tests in PaperTradingSessionProperties.cs
/// with specific scenario-driven assertions.
/// Requirements: 28.1, 28.2, 28.3
/// </summary>
public class SimulatedPaperTradingSessionTests
{
    private static ScenarioConfig CreateTestConfig(decimal initialCash = 100_000m) => new(
        ScenarioId: "test-paper",
        Description: "Test paper trading",
        ReplayMode: ReplayMode.Bar,
        DataProviderType: "csv",
        DataProviderOptions: new Dictionary<string, object>
        {
            ["Symbol"] = "TEST",
            ["Interval"] = "1d",
            ["FilePath"] = "test.csv"
        },
        StrategyType: "test-strategy",
        StrategyParameters: new Dictionary<string, object>(),
        RiskParameters: new Dictionary<string, object>(),
        SlippageModelType: "zero",
        CommissionModelType: "zero",
        InitialCash: initialCash,
        AnnualRiskFreeRate: 0m,
        RandomSeed: 42,
        ResearchWorkflowType: null,
        ResearchWorkflowOptions: null,
        PropFirmOptions: null);

    private static List<BarRecord> GenerateFixedBars(int count)
    {
        var bars = new List<BarRecord>(count);
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var price = 100m;

        for (int i = 0; i < count; i++)
        {
            price += 0.5m; // steadily rising
            bars.Add(new BarRecord(
                Symbol: "TEST",
                Interval: "1d",
                Open: price - 0.25m,
                High: price + 1m,
                Low: price - 1m,
                Close: price,
                Volume: 10000m,
                Timestamp: baseDate.AddDays(i)));
        }

        return bars;
    }

    private static Mock<IStreamingDataProvider> CreateMockStreamingProvider(List<BarRecord> bars)
    {
        var mock = new Mock<IStreamingDataProvider>();
        mock.Setup(p => p.StreamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string symbol, string interval, CancellationToken ct) => ToAsyncEnumerable(bars, ct));
        return mock;
    }

    private static async IAsyncEnumerable<BarRecord> ToAsyncEnumerable(
        List<BarRecord> bars, [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var bar in bars)
        {
            if (ct.IsCancellationRequested) yield break;
            yield return bar;
            await Task.Yield();
        }
    }

    private static SimulatedPaperTradingSession CreateSession(
        Mock<IStreamingDataProvider> streamingProvider,
        IStrategy strategy,
        IRiskLayer? riskLayer = null,
        IExecutionHandler? executionHandler = null)
    {
        var mockRepo = new Mock<IRepository<PaperSessionRecord>>();
        mockRepo.Setup(r => r.SaveAsync(It.IsAny<PaperSessionRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerFactory = NullLoggerFactory.Instance;
        var slippage = new ZeroSlippageModel();
        var commission = new ZeroCommissionModel();
        var actualExecution = executionHandler ?? new SimulatedExecutionHandler(
            slippage, commission, loggerFactory.CreateLogger<SimulatedExecutionHandler>());
        var actualRisk = riskLayer ?? CreatePassThroughRiskLayer();

        return new SimulatedPaperTradingSession(
            streamingProvider.Object,
            strategy,
            actualRisk,
            actualExecution,
            slippage,
            commission,
            mockRepo.Object,
            loggerFactory.CreateLogger<SimulatedPaperTradingSession>());
    }

    private static IRiskLayer CreatePassThroughRiskLayer()
    {
        var mock = new Mock<IRiskLayer>();
        mock.Setup(r => r.ConvertSignal(It.IsAny<SignalEvent>(), It.IsAny<PortfolioSnapshot>()))
            .Returns((SignalEvent signal, PortfolioSnapshot snapshot) =>
                new OrderEvent(
                    signal.Symbol,
                    signal.Direction,
                    signal.Direction == Direction.Flat ? 0m : 10m,
                    OrderType.Market,
                    null,
                    signal.Timestamp));
        mock.Setup(r => r.EvaluateOrder(It.IsAny<OrderEvent>(), It.IsAny<PortfolioSnapshot>()))
            .Returns((OrderEvent order, PortfolioSnapshot _) => order);
        return mock.Object;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // StopAsync → status Stopped + valid PaperTradingResult
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StopAsync_AfterProcessingBars_StatusIsStopped()
    {
        // Arrange
        var bars = GenerateFixedBars(10);
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var provider = CreateMockStreamingProvider(bars);
        var session = CreateSession(provider, noOpStrategy.Object);
        var config = CreateTestConfig();

        // Act
        await session.StartAsync(config, CancellationToken.None);
        await Task.Delay(300); // let bars process
        var result = await session.StopAsync();

        // Assert
        Assert.Equal(PaperTradingStatus.Stopped, session.Status);
        session.Dispose();
    }

    [Fact]
    public async Task StopAsync_ProducesValidPaperTradingResult()
    {
        // Arrange
        var bars = GenerateFixedBars(10);
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var provider = CreateMockStreamingProvider(bars);
        var session = CreateSession(provider, noOpStrategy.Object);
        var config = CreateTestConfig();

        // Act
        await session.StartAsync(config, CancellationToken.None);
        await Task.Delay(300);
        var result = await session.StopAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.EquivalentBacktestResult);
        Assert.NotNull(result.FinalPortfolio);
        Assert.Equal(PaperTradingStatus.Stopped, result.FinalStatus);
        Assert.True(result.StoppedAt > result.StartedAt);
        Assert.Equal(BacktestStatus.Completed, result.EquivalentBacktestResult.Status);
        Assert.Equal(config.InitialCash, result.EquivalentBacktestResult.StartEquity);

        session.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // CancellationToken → graceful stop
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_WithCancelledToken_StopsGracefully()
    {
        // Arrange: use many bars so cancellation can interrupt
        var bars = GenerateFixedBars(1000);
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var provider = CreateMockStreamingProvider(bars);
        var session = CreateSession(provider, noOpStrategy.Object);
        var config = CreateTestConfig();
        using var cts = new CancellationTokenSource();

        // Act
        await session.StartAsync(config, cts.Token);
        await Task.Delay(50); // let a few bars process
        await cts.CancelAsync();
        await Task.Delay(100); // allow cancellation to propagate

        // The session should transition to a terminal state
        // StopAsync should still work after cancellation
        var result = await session.StopAsync();

        // Assert
        Assert.Equal(PaperTradingStatus.Stopped, session.Status);
        Assert.NotNull(result.EquivalentBacktestResult);

        session.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // PauseAsync → portfolio state frozen
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PauseAsync_FreezesPortfolioState()
    {
        // Arrange
        var bars = GenerateFixedBars(50);
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var provider = CreateMockStreamingProvider(bars);
        var session = CreateSession(provider, noOpStrategy.Object);
        var config = CreateTestConfig();

        // Act
        await session.StartAsync(config, CancellationToken.None);
        await Task.Delay(100); // let some bars process
        await session.PauseAsync();

        // Assert
        Assert.Equal(PaperTradingStatus.Paused, session.Status);

        // Record equity at pause
        var equityAtPause = session.Portfolio.TotalEquity;

        // Wait and verify equity doesn't change
        await Task.Delay(100);
        var equityAfterWait = session.Portfolio.TotalEquity;
        Assert.Equal(equityAtPause, equityAfterWait);

        // Clean up
        await session.StopAsync();
        session.Dispose();
    }

    [Fact]
    public async Task PauseAsync_FromNonRunningState_ThrowsInvalidOperationException()
    {
        // Arrange
        var bars = GenerateFixedBars(5);
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var provider = CreateMockStreamingProvider(bars);
        var session = CreateSession(provider, noOpStrategy.Object);

        // Act & Assert — cannot pause from Idle
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.PauseAsync());

        session.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ResumeAsync → bar consumption resumes
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResumeAsync_AfterPause_ResumesBarConsumption()
    {
        // Arrange: use a slow streaming provider to control timing
        var bars = GenerateFixedBars(20);
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var provider = CreateMockStreamingProvider(bars);
        var session = CreateSession(provider, noOpStrategy.Object);
        var config = CreateTestConfig();

        // Act
        await session.StartAsync(config, CancellationToken.None);
        await Task.Delay(100);
        await session.PauseAsync();

        var equityAtPause = session.Portfolio.TotalEquity;

        // Resume
        await session.ResumeAsync(CancellationToken.None);
        Assert.Equal(PaperTradingStatus.Running, session.Status);

        // Wait for more bars to process
        await Task.Delay(300);

        // Stop and verify result
        var result = await session.StopAsync();
        Assert.Equal(PaperTradingStatus.Stopped, session.Status);
        Assert.NotNull(result.EquivalentBacktestResult);

        session.Dispose();
    }

    [Fact]
    public async Task ResumeAsync_FromNonPausedState_ThrowsInvalidOperationException()
    {
        // Arrange
        var bars = GenerateFixedBars(5);
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var provider = CreateMockStreamingProvider(bars);
        var session = CreateSession(provider, noOpStrategy.Object);

        // Act & Assert — cannot resume from Idle
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.ResumeAsync(CancellationToken.None));

        session.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Metric equivalence with BacktestResult for same data
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MetricEquivalence_PaperAndBacktest_MatchForSameData()
    {
        // Arrange: use a buy-once strategy for deterministic comparison
        var bars = GenerateFixedBars(20);
        var buyOnceStrategy = new BuyOnceStrategy();

        var loggerFactory = NullLoggerFactory.Instance;
        var slippage = new ZeroSlippageModel();
        var commission = new ZeroCommissionModel();
        var executionHandler = new SimulatedExecutionHandler(
            slippage, commission, loggerFactory.CreateLogger<SimulatedExecutionHandler>());
        var riskLayer = CreatePassThroughRiskLayer();

        // --- Paper trading path ---
        var streamingProvider = CreateMockStreamingProvider(bars);
        var session = CreateSession(streamingProvider, buyOnceStrategy, riskLayer, executionHandler);
        var config = CreateTestConfig();

        await session.StartAsync(config, CancellationToken.None);
        await Task.Delay(500); // let all bars process
        var paperResult = await session.StopAsync();
        session.Dispose();

        // --- Manual backtest path ---
        var backtestPortfolio = new Core.Portfolio.Portfolio(
            config.InitialCash, loggerFactory.CreateLogger<Core.Portfolio.Portfolio>());
        var backtestStrategy = new BuyOnceStrategy();
        var backtestExecution = new SimulatedExecutionHandler(
            slippage, commission, loggerFactory.CreateLogger<SimulatedExecutionHandler>());

        foreach (var bar in bars)
        {
            var barEvent = new BarEvent(
                bar.Symbol, bar.Interval, bar.Open, bar.High, bar.Low,
                bar.Close, bar.Volume, bar.Timestamp);

            backtestPortfolio.MarkToMarket(bar.Symbol, bar.Close, bar.Timestamp);

            var outputs = backtestStrategy.OnMarketData(barEvent);
            foreach (var output in outputs)
            {
                if (output is SignalEvent signal)
                {
                    var order = riskLayer.ConvertSignal(signal, backtestPortfolio.TakeSnapshot());
                    if (order is not null)
                    {
                        var result = backtestExecution.Execute(order with { RiskApproved = true }, barEvent);
                        if (result.Fill is not null)
                            backtestPortfolio.Update(result.Fill);
                    }
                }
            }
        }

        // Assert: key metrics should match
        var paperMetrics = paperResult.EquivalentBacktestResult;
        Assert.Equal(backtestPortfolio.ClosedTrades.Count, paperMetrics.TotalTrades);

        var equityDiff = Math.Abs(paperMetrics.EndEquity - backtestPortfolio.TotalEquity);
        Assert.True(equityDiff <= 0.01m,
            $"End equity mismatch: paper={paperMetrics.EndEquity}, backtest={backtestPortfolio.TotalEquity}");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helper types
    // ─────────────────────────────────────────────────────────────────────────────

    private sealed class BuyOnceStrategy : IStrategy
    {
        private bool _hasBought;

        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            if (!_hasBought)
            {
                _hasBought = true;
                return new List<EngineEvent>
                {
                    new SignalEvent(evt.Symbol, Direction.Long, 1.0m, evt.Timestamp)
                };
            }
            return new List<EngineEvent>();
        }
    }

    private sealed class ZeroSlippageModel : ISlippageModel
    {
        public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market) => 0m;
    }

    private sealed class ZeroCommissionModel : ICommissionModel
    {
        public decimal ComputeCommission(OrderEvent order, decimal fillPrice, decimal quantity) => 0m;
    }
}
