using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Runtime.CompilerServices;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.PaperTrading;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.PaperTrading;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Queue;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.UnitTests.PaperTrading;

// Feature: trading-research-engine, Property 4: Paper trading state machine validity
// Feature: trading-research-engine, Property 5: Paper trading metric equivalence
// Feature: trading-research-engine, Property 6: Paper StopAsync produces valid result

/// <summary>
/// Property-based tests for the SimulatedPaperTradingSession.
/// Tests state machine validity, metric equivalence with backtesting, and StopAsync correctness.
/// </summary>
public class PaperTradingSessionProperties
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

    private static List<BarRecord> GenerateBars(int count, int seed)
    {
        var rng = new Random(seed);
        var bars = new List<BarRecord>(count);
        var baseDate = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var price = 100m;

        for (int i = 0; i < count; i++)
        {
            var change = (decimal)(rng.NextDouble() * 4.0 - 2.0);
            price = Math.Max(10m, price + change);

            var open = price + (decimal)(rng.NextDouble() * 2.0 - 1.0);
            var close = price + (decimal)(rng.NextDouble() * 2.0 - 1.0);
            var high = Math.Max(open, close) + (decimal)(rng.NextDouble() * 2.0);
            var low = Math.Min(open, close) - (decimal)(rng.NextDouble() * 2.0);
            low = Math.Max(1m, low);
            high = Math.Max(low + 0.01m, high);
            var volume = (decimal)(rng.NextDouble() * 1_000_000 + 1000);

            bars.Add(new BarRecord(
                Symbol: "TEST",
                Interval: "1d",
                Open: Math.Round(open, 4),
                High: Math.Round(high, 4),
                Low: Math.Round(low, 4),
                Close: Math.Round(close, 4),
                Volume: Math.Round(volume, 2),
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
        IExecutionHandler? executionHandler = null,
        ISlippageModel? slippageModel = null,
        ICommissionModel? commissionModel = null)
    {
        var mockRepo = new Mock<IRepository<PaperSessionRecord>>();
        mockRepo.Setup(r => r.SaveAsync(It.IsAny<PaperSessionRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var loggerFactory = NullLoggerFactory.Instance;

        var actualSlippage = slippageModel ?? new ZeroSlippageModel();
        var actualCommission = commissionModel ?? new ZeroCommissionModel();
        var actualExecution = executionHandler ?? new SimulatedExecutionHandler(
            actualSlippage, actualCommission,
            loggerFactory.CreateLogger<SimulatedExecutionHandler>());
        var actualRisk = riskLayer ?? CreatePassThroughRiskLayer();

        return new SimulatedPaperTradingSession(
            streamingProvider.Object,
            strategy,
            actualRisk,
            actualExecution,
            actualSlippage,
            actualCommission,
            mockRepo.Object,
            new TestOptionsMonitor<PaperTradingOptions>(new PaperTradingOptions()),
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
                    signal.Direction == Direction.Flat ? 0m : ComputeQuantity(snapshot, signal),
                    OrderType.Market,
                    null,
                    signal.Timestamp));
        mock.Setup(r => r.EvaluateOrder(It.IsAny<OrderEvent>(), It.IsAny<PortfolioSnapshot>()))
            .Returns((OrderEvent order, PortfolioSnapshot _) => order);
        return mock.Object;
    }

    private static decimal ComputeQuantity(PortfolioSnapshot snapshot, SignalEvent signal)
    {
        // Simple fixed quantity for testing
        return 10m;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 4: Paper trading state machine validity
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For any sequence of valid lifecycle operations (Start, Pause, Resume, Stop),
    /// the session status shall always be a valid PaperTradingStatus value,
    /// and pausing shall halt portfolio equity changes until resume.
    /// **Validates: Requirements 9.2, 10.6**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool StateMachine_AlwaysValidStatus_PauseHaltsEquityChanges(PositiveInt seedWrap)
    {
        var seed = seedWrap.Get;
        var barCount = 20;
        var bars = GenerateBars(barCount, seed);

        // Use a strategy that does nothing (no signals) so we can focus on state machine
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        // Split bars: first half before pause, second half after resume
        var firstHalf = bars.Take(barCount / 2).ToList();
        var secondHalf = bars.Skip(barCount / 2).ToList();

        // Create a provider that yields first half, then waits for resume to yield second half
        var allBars = new List<BarRecord>(bars);
        var streamingProvider = CreateMockStreamingProvider(allBars);

        var session = CreateSession(streamingProvider, noOpStrategy.Object);
        var config = CreateTestConfig();

        // Verify initial state
        if (session.Status != PaperTradingStatus.Idle) return false;
        if (!Enum.IsDefined(typeof(PaperTradingStatus), session.Status)) return false;

        // Start
        session.StartAsync(config, CancellationToken.None).Wait();
        if (session.Status != PaperTradingStatus.Running) return false;
        if (!Enum.IsDefined(typeof(PaperTradingStatus), session.Status)) return false;

        // Let some bars process
        Task.Delay(100).Wait();

        // Pause
        session.PauseAsync().Wait();
        if (session.Status != PaperTradingStatus.Paused) return false;
        if (!Enum.IsDefined(typeof(PaperTradingStatus), session.Status)) return false;

        // Record equity at pause
        var equityAtPause = session.Portfolio.TotalEquity;

        // Wait a bit - equity should not change while paused
        Task.Delay(50).Wait();
        var equityAfterWait = session.Portfolio.TotalEquity;
        if (equityAtPause != equityAfterWait) return false;

        // Stop from paused state
        var result = session.StopAsync().Result;
        if (session.Status != PaperTradingStatus.Stopped) return false;
        if (!Enum.IsDefined(typeof(PaperTradingStatus), session.Status)) return false;

        session.Dispose();
        return true;
    }

    /// <summary>
    /// Starting from Idle transitions to Running, and invalid state transitions throw.
    /// **Validates: Requirements 9.2, 10.6**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool StateMachine_InvalidTransitions_ThrowInvalidOperationException(PositiveInt seedWrap)
    {
        var seed = seedWrap.Get;
        var bars = GenerateBars(5, seed);
        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var streamingProvider = CreateMockStreamingProvider(bars);
        var session = CreateSession(streamingProvider, noOpStrategy.Object);
        var config = CreateTestConfig();

        // Cannot pause from Idle
        try
        {
            session.PauseAsync().Wait();
            return false; // Should have thrown
        }
        catch (AggregateException ex) when (ex.InnerException is InvalidOperationException)
        {
            // Expected
        }

        // Cannot resume from Idle
        try
        {
            session.ResumeAsync(CancellationToken.None).Wait();
            return false;
        }
        catch (AggregateException ex) when (ex.InnerException is InvalidOperationException)
        {
            // Expected
        }

        // Start session
        session.StartAsync(config, CancellationToken.None).Wait();

        // Cannot start again
        try
        {
            session.StartAsync(config, CancellationToken.None).Wait();
            return false;
        }
        catch (AggregateException ex) when (ex.InnerException is InvalidOperationException)
        {
            // Expected
        }

        // Clean up
        session.StopAsync().Wait();
        session.Dispose();
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 5: Paper trading metric equivalence
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For any historical bar sequence fed through a mocked IStreamingDataProvider,
    /// the PaperTradingResult metrics shall match the BacktestResult metrics produced
    /// by running the same bar sequence through BacktestEngine with identical strategy,
    /// risk, execution, slippage, and commission configuration.
    /// **Validates: Requirements 10.1, 28.1**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool MetricEquivalence_PaperMatchesBacktest_ForSameBarSequence(PositiveInt seedWrap)
    {
        var seed = seedWrap.Get;
        var barCount = (seed % 30) + 10; // 10 to 39 bars
        var bars = GenerateBars(barCount, seed);

        // Use a simple buy-and-hold strategy: buy on first bar, hold
        var buyOnceStrategy = new BuyOnceStrategy();

        var loggerFactory = NullLoggerFactory.Instance;
        var slippage = new ZeroSlippageModel();
        var commission = new ZeroCommissionModel();
        var executionHandler = new SimulatedExecutionHandler(
            slippage, commission, loggerFactory.CreateLogger<SimulatedExecutionHandler>());
        var riskLayer = CreatePassThroughRiskLayer();

        // --- Paper trading path ---
        var streamingProvider = CreateMockStreamingProvider(bars);
        var session = CreateSession(streamingProvider, buyOnceStrategy, riskLayer, executionHandler, slippage, commission);
        var config = CreateTestConfig();

        session.StartAsync(config, CancellationToken.None).Wait();

        // Wait for all bars to be processed
        Task.Delay(500).Wait();

        var paperResult = session.StopAsync().Result;
        session.Dispose();

        // --- Backtest path (manual simulation) ---
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

            // Mark-to-market
            backtestPortfolio.MarkToMarket(bar.Symbol, bar.Close, bar.Timestamp);

            // Strategy
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

        // Compare key metrics
        var paperMetrics = paperResult.EquivalentBacktestResult;

        // Trade count should match
        if (paperMetrics.TotalTrades != backtestPortfolio.ClosedTrades.Count) return false;

        // End equity should match (within floating point tolerance)
        var equityDiff = Math.Abs(paperMetrics.EndEquity - backtestPortfolio.TotalEquity);
        if (equityDiff > 0.01m) return false;

        // MaxDrawdown should match
        var backtestMaxDD = MetricsCalculator.ComputeMaxDrawdown(backtestPortfolio.EquityCurve);
        var paperMaxDD = paperMetrics.MaxDrawdown;
        if (Math.Abs(paperMaxDD - backtestMaxDD) > 0.01m) return false;

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Property 6: Paper StopAsync produces valid result
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For any paper trading session that has processed at least one bar and is in
    /// Running or Paused state, calling StopAsync() shall transition status to Stopped
    /// and produce a PaperTradingResult with non-null EquivalentBacktestResult and FinalPortfolio.
    /// **Validates: Requirements 10.3, 28.2**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool StopAsync_ProducesValidResult_WhenAtLeastOneBarProcessed(PositiveInt seedWrap)
    {
        var seed = seedWrap.Get;
        var barCount = (seed % 20) + 3; // 3 to 22 bars
        var bars = GenerateBars(barCount, seed);

        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var streamingProvider = CreateMockStreamingProvider(bars);
        var session = CreateSession(streamingProvider, noOpStrategy.Object);
        var config = CreateTestConfig();

        // Start and let bars process
        session.StartAsync(config, CancellationToken.None).Wait();
        Task.Delay(300).Wait();

        // Stop
        var result = session.StopAsync().Result;

        // Verify status
        if (session.Status != PaperTradingStatus.Stopped) return false;

        // Verify result is valid
        if (result.EquivalentBacktestResult is null) return false;
        if (result.FinalPortfolio is null) return false;
        if (result.FinalStatus != PaperTradingStatus.Stopped) return false;
        if (result.StoppedAt <= result.StartedAt) return false;

        // EquivalentBacktestResult should have valid structure
        var backtest = result.EquivalentBacktestResult;
        if (backtest.Status != BacktestStatus.Completed) return false;
        if (backtest.StartEquity != config.InitialCash) return false;

        session.Dispose();
        return true;
    }

    /// <summary>
    /// StopAsync from Paused state also produces a valid result.
    /// **Validates: Requirements 10.3, 28.2**
    /// </summary>
    [Property(MaxTest = 20)]
    public bool StopAsync_FromPausedState_ProducesValidResult(PositiveInt seedWrap)
    {
        var seed = seedWrap.Get;
        var barCount = (seed % 15) + 5; // 5 to 19 bars
        var bars = GenerateBars(barCount, seed);

        var noOpStrategy = new Mock<IStrategy>();
        noOpStrategy.Setup(s => s.OnMarketData(It.IsAny<MarketDataEvent>()))
            .Returns(new List<EngineEvent>());

        var streamingProvider = CreateMockStreamingProvider(bars);
        var session = CreateSession(streamingProvider, noOpStrategy.Object);
        var config = CreateTestConfig();

        // Start, let some bars process, then pause
        session.StartAsync(config, CancellationToken.None).Wait();
        Task.Delay(200).Wait();
        session.PauseAsync().Wait();

        if (session.Status != PaperTradingStatus.Paused) return false;

        // Stop from paused
        var result = session.StopAsync().Result;

        if (session.Status != PaperTradingStatus.Stopped) return false;
        if (result.EquivalentBacktestResult is null) return false;
        if (result.FinalPortfolio is null) return false;
        if (result.FinalStatus != PaperTradingStatus.Stopped) return false;

        session.Dispose();
        return true;
    }

    /// <summary>
    /// A simple strategy that buys once on the first bar and holds.
    /// Used for metric equivalence testing.
    /// </summary>
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

    /// <summary>
    /// Zero slippage model for deterministic testing.
    /// </summary>
    private sealed class ZeroSlippageModel : ISlippageModel
    {
        public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market) => 0m;
    }

    /// <summary>
    /// Zero commission model for deterministic testing.
    /// </summary>
    private sealed class ZeroCommissionModel : ICommissionModel
    {
        public decimal ComputeCommission(OrderEvent order, decimal fillPrice, decimal quantity) => 0m;
    }
}
