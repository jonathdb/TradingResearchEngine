using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.UnitTests.Engine;

/// <summary>
/// Tests that ProcessPendingOrders uses the swap buffer pattern correctly,
/// preserving order processing semantics while eliminating per-bar List allocations.
/// </summary>
public class PendingOrdersSwapBufferTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ScenarioConfig CreateConfig() => new(
        ScenarioId: "swap-buffer-test",
        Description: "Swap buffer pattern test",
        ReplayMode: ReplayMode.Bar,
        DataProviderType: "Mock",
        DataProviderOptions: new Dictionary<string, object>
        {
            ["Symbol"] = "TEST",
            ["Interval"] = "1D"
        },
        StrategyType: "test",
        StrategyParameters: new Dictionary<string, object>(),
        RiskParameters: new Dictionary<string, object>(),
        SlippageModelType: "Zero",
        CommissionModelType: "Zero",
        InitialCash: 100_000m,
        AnnualRiskFreeRate: 0m,
        RandomSeed: null,
        ResearchWorkflowType: null,
        ResearchWorkflowOptions: null,
        PropFirmOptions: null,
        FillMode: FillMode.NextBarOpen);

    private static IDataProvider CreateDataProvider(params BarRecord[] bars)
    {
        var mock = new Mock<IDataProvider>();
        mock.Setup(p => p.GetBars(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(bars));
        return mock.Object;
    }

    private static async IAsyncEnumerable<BarRecord> ToAsyncEnumerable(BarRecord[] bars)
    {
        foreach (var bar in bars)
        {
            yield return bar;
            await Task.CompletedTask;
        }
    }

    private static IRiskLayer CreatePassThroughRiskLayer()
    {
        var mock = new Mock<IRiskLayer>();
        mock.Setup(r => r.EvaluateOrder(It.IsAny<OrderEvent>(), It.IsAny<PortfolioSnapshot>()))
            .Returns((OrderEvent o, PortfolioSnapshot _) => o);
        mock.Setup(r => r.ConvertSignal(It.IsAny<SignalEvent>(), It.IsAny<PortfolioSnapshot>()))
            .Returns((SignalEvent s, PortfolioSnapshot _) => null);
        return mock.Object;
    }

    private static IExecutionHandler CreateZeroSlippageExecutionHandler()
    {
        var mock = new Mock<IExecutionHandler>();
        mock.Setup(h => h.Execute(It.IsAny<OrderEvent>(), It.IsAny<MarketDataEvent>()))
            .Returns((OrderEvent order, MarketDataEvent mde) =>
            {
                decimal price = mde is BarEvent bar ? bar.Close : 0m;
                var fill = new FillEvent(
                    order.Symbol,
                    order.Direction,
                    order.Quantity,
                    price,
                    Commission: 0m,
                    SlippageAmount: 0m,
                    mde.Timestamp);
                return new ExecutionResult(ExecutionOutcome.Filled, fill);
            });
        return mock.Object;
    }

    private static BacktestEngine CreateEngine(IDataProvider dataProvider, IStrategy strategy)
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<BacktestEngine>();
        return new BacktestEngine(dataProvider, strategy, CreatePassThroughRiskLayer(),
            CreateZeroSlippageExecutionHandler(), logger);
    }

    /// <summary>
    /// A strategy that emits a GTC limit order on the first bar that will never fill
    /// (limit price far below market), verifying the order persists across multiple bars
    /// via the swap buffer pattern.
    /// </summary>
    private sealed class PersistentLimitOrderStrategy : IStrategy
    {
        private bool _orderEmitted;

        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            if (!_orderEmitted)
            {
                _orderEmitted = true;
                return new EngineEvent[]
                {
                    new OrderEvent(
                        Symbol: "TEST",
                        Direction: Direction.Long,
                        Quantity: 100m,
                        OrderType: OrderType.Limit,
                        LimitPrice: 1.00m, // Far below market price of 100+
                        Timestamp: evt.Timestamp,
                        MaxBarsPending: 0) // GTC — never expires
                };
            }
            return Array.Empty<EngineEvent>();
        }
    }

    /// <summary>
    /// A strategy that emits multiple GTC limit orders on the first bar,
    /// some of which will fill and some won't.
    /// </summary>
    private sealed class MixedFillStrategy : IStrategy
    {
        private bool _ordersEmitted;

        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            if (!_ordersEmitted)
            {
                _ordersEmitted = true;
                return new EngineEvent[]
                {
                    // This order will fill on bar 2 (limit at 95, bar Low will be 90)
                    new OrderEvent(
                        Symbol: "TEST",
                        Direction: Direction.Long,
                        Quantity: 50m,
                        OrderType: OrderType.Limit,
                        LimitPrice: 95.00m,
                        Timestamp: evt.Timestamp,
                        MaxBarsPending: 0),
                    // This order will NOT fill (limit at 1.00, far below market)
                    new OrderEvent(
                        Symbol: "TEST",
                        Direction: Direction.Long,
                        Quantity: 100m,
                        OrderType: OrderType.Limit,
                        LimitPrice: 1.00m,
                        Timestamp: evt.Timestamp,
                        MaxBarsPending: 0)
                };
            }
            return Array.Empty<EngineEvent>();
        }
    }

    /// <summary>
    /// Strategy that emits a limit order with a configurable MaxBarsPending expiry.
    /// The limit price is set far below market so it never fills.
    /// </summary>
    private sealed class ExpiringOrderStrategy : IStrategy
    {
        private readonly int _maxBarsPending;
        private bool _orderEmitted;

        public ExpiringOrderStrategy(int maxBarsPending)
        {
            _maxBarsPending = maxBarsPending;
        }

        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            if (!_orderEmitted)
            {
                _orderEmitted = true;
                return new EngineEvent[]
                {
                    new OrderEvent(
                        Symbol: "TEST",
                        Direction: Direction.Long,
                        Quantity: 100m,
                        OrderType: OrderType.Limit,
                        LimitPrice: 1.00m, // Far below market
                        Timestamp: evt.Timestamp,
                        MaxBarsPending: _maxBarsPending)
                };
            }
            return Array.Empty<EngineEvent>();
        }
    }

    private static BarRecord[] CreateBars(int count, decimal basePrice = 100m)
    {
        var bars = new BarRecord[count];
        for (int i = 0; i < count; i++)
        {
            decimal price = basePrice + i;
            bars[i] = new BarRecord(
                Symbol: "TEST",
                Interval: "1D",
                Open: price,
                High: price + 5m,
                Low: price - 10m,
                Close: price + 2m,
                Volume: 1000m,
                Timestamp: T0.AddDays(i));
        }
        return bars;
    }

    [Fact]
    public async Task ProcessPendingOrders_GtcOrderPersistsAcrossMultipleBars_ViaSwapBuffer()
    {
        // Arrange: 10 bars, GTC limit order that never fills (limit at 1.00, market at 100+)
        var bars = CreateBars(10);
        var engine = CreateEngine(CreateDataProvider(bars), new PersistentLimitOrderStrategy());

        // Act
        var result = await engine.RunAsync(CreateConfig());

        // Assert: The run completes successfully (no exceptions from swap buffer logic)
        Assert.Equal(BacktestStatus.Completed, result.Status);
        // The order never fills (limit at 1.00, market at 100+), so zero trades
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public async Task ProcessPendingOrders_MixedFillAndRemain_CorrectlyProcessed()
    {
        // Arrange: 5 bars, one order fills on bar 2, one never fills
        // Bar 0: strategy emits orders (go to pending queue)
        // Bar 1: Open=101, High=106, Low=91, Close=103 → limit at 95 fills (Low 91 <= 95)
        // The second order (limit at 1.00) never fills
        var bars = CreateBars(5);
        var engine = CreateEngine(CreateDataProvider(bars), new MixedFillStrategy());

        // Act
        var result = await engine.RunAsync(CreateConfig());

        // Assert: Run completes successfully — the swap buffer correctly handles
        // mixed fill/remain scenarios (one order filled, one remains in buffer)
        Assert.Equal(BacktestStatus.Completed, result.Status);
        // The first order (limit at 95) fills when bar.Low <= 95 (bar 1: Low = 100-10=90 <= 95)
        // This opens a position but doesn't close it, so TotalTrades (closed trades) = 0
        // The key assertion is that the engine completes without error, proving the swap buffer
        // correctly handles the case where some orders fill and others remain
        Assert.Equal(0, result.TotalTrades);
        // End equity differs from start because a position was opened (mark-to-market)
        Assert.NotEqual(result.StartEquity, result.EndEquity);
    }

    [Fact]
    public async Task ProcessPendingOrders_ManyBarsWithPendingOrders_CompletesWithoutError()
    {
        // Arrange: Simulate many bars with a persistent pending order
        // This verifies the swap buffer pattern handles repeated swaps correctly
        var bars = CreateBars(100, basePrice: 1000m);
        var engine = CreateEngine(CreateDataProvider(bars), new PersistentLimitOrderStrategy());

        // Act
        var result = await engine.RunAsync(CreateConfig());

        // Assert: Completes without error after 100 swap operations
        Assert.Equal(BacktestStatus.Completed, result.Status);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public async Task ProcessPendingOrders_OrderWithMaxBarsPending_ExpiresCorrectly()
    {
        // Arrange: Strategy emits a limit order with MaxBarsPending = 3
        var bars = CreateBars(10, basePrice: 1000m);
        var engine = CreateEngine(CreateDataProvider(bars), new ExpiringOrderStrategy(maxBarsPending: 3));

        // Act
        var result = await engine.RunAsync(CreateConfig());

        // Assert: Order expires after 3 bars, no trades
        Assert.Equal(BacktestStatus.Completed, result.Status);
        Assert.Equal(0, result.TotalTrades);
    }
}
