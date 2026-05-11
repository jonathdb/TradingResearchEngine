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

namespace TradingResearchEngine.UnitTests.Execution;

/// <summary>
/// Tests that stop-limit orders correctly persist the StopTriggered state across bars.
/// When a stop-limit order triggers on bar N but the limit is not reached,
/// the order is re-queued with StopTriggered=true and fills on bar N+1 when the limit is hit.
/// </summary>
public class StopLimitTriggeredStateTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A strategy that emits a stop-limit order on the first bar,
    /// then emits a flat (exit) market order on a specified bar index to close the position.
    /// </summary>
    private sealed class StopLimitThenExitStrategy : IStrategy
    {
        private readonly OrderEvent _entryOrder;
        private readonly int _exitOnBar;
        private int _barCount;
        private bool _entryEmitted;

        public StopLimitThenExitStrategy(OrderEvent entryOrder, int exitOnBar)
        {
            _entryOrder = entryOrder;
            _exitOnBar = exitOnBar;
        }

        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            int currentBar = _barCount++;

            if (!_entryEmitted)
            {
                _entryEmitted = true;
                return new EngineEvent[] { _entryOrder };
            }

            if (currentBar == _exitOnBar)
            {
                // Emit a market order to close the position
                var exitOrder = new OrderEvent(
                    Symbol: _entryOrder.Symbol,
                    Direction: Direction.Flat,
                    Quantity: _entryOrder.Quantity,
                    OrderType: OrderType.Market,
                    LimitPrice: null,
                    Timestamp: evt.Timestamp);
                return new EngineEvent[] { exitOrder };
            }

            return Array.Empty<EngineEvent>();
        }
    }

    private static ScenarioConfig CreateConfig() => new(
        ScenarioId: "test-stop-limit",
        Description: "Stop-limit triggered state test",
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

    private static BacktestEngine CreateEngine(
        IDataProvider dataProvider,
        IStrategy strategy,
        IRiskLayer riskLayer,
        IExecutionHandler executionHandler)
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<BacktestEngine>();
        return new BacktestEngine(dataProvider, strategy, riskLayer, executionHandler, logger);
    }

    [Fact]
    public async Task TryFillStopLimit_StopTriggersButLimitMissed_FillsOnNextBar()
    {
        // Arrange:
        // Long stop-limit order: stopPrice=100, limitPrice=95
        // Bar 0 (initial bar — strategy emits order here, order goes to pending queue)
        // Bar 1 (bar N): High=105 (triggers stop at 100), Low=98 (limit 95 NOT reached)
        //   → Order should be re-queued with StopTriggered=true
        // Bar 2 (bar N+1): Low=94 (limit 95 IS reached)
        //   → Order should fill at limitPrice=95
        // Bar 3: strategy emits flat order to close position (fills on bar 4)
        // Bar 4: flat order fills at Open

        var bars = new[]
        {
            new BarRecord("TEST", "1D", 90m, 92m, 88m, 91m, 1000m, T0),                    // Bar 0: strategy emits order
            new BarRecord("TEST", "1D", 98m, 105m, 98m, 102m, 1000m, T0.AddDays(1)),       // Bar 1: stop triggers (High>=100), limit missed (Low=98 > 95)
            new BarRecord("TEST", "1D", 96m, 97m, 94m, 95m, 1000m, T0.AddDays(2)),         // Bar 2: limit reached (Low=94 <= 95), fill at 95
            new BarRecord("TEST", "1D", 96m, 98m, 94m, 97m, 1000m, T0.AddDays(3)),         // Bar 3: strategy emits flat order
            new BarRecord("TEST", "1D", 97m, 99m, 96m, 98m, 1000m, T0.AddDays(4)),         // Bar 4: flat fills at Open=97
        };

        var order = new OrderEvent(
            Symbol: "TEST",
            Direction: Direction.Long,
            Quantity: 10m,
            OrderType: OrderType.StopLimit,
            LimitPrice: 95m,
            Timestamp: T0,
            RiskApproved: false,
            StopPrice: 100m,
            MaxBarsPending: 0,  // GTC
            StopTriggered: false);

        var dataProvider = CreateDataProvider(bars);
        // Exit on bar 3 (0-indexed) to close the position
        var strategy = new StopLimitThenExitStrategy(order, exitOnBar: 3);
        var riskLayer = CreatePassThroughRiskLayer();
        var executionHandler = CreateZeroSlippageExecutionHandler();

        var engine = CreateEngine(dataProvider, strategy, riskLayer, executionHandler);
        var config = CreateConfig();

        // Act
        var result = await engine.RunAsync(config);

        // Assert: The order should have filled and then closed — we should have 1 closed trade
        Assert.Equal(BacktestStatus.Completed, result.Status);
        Assert.Equal(1, result.TotalTrades);

        // The entry fill should be at the limit price of 95
        var trade = result.Trades[0];
        Assert.Equal(95m, trade.EntryPrice);
        Assert.Equal("TEST", trade.Symbol);
        Assert.Equal(Direction.Long, trade.Direction);
    }

    [Fact]
    public async Task TryFillStopLimit_StopTriggeredTrue_PersistsAcrossMultipleBars()
    {
        // Arrange:
        // Long stop-limit order: stopPrice=100, limitPrice=90
        // Bar 0: strategy emits order
        // Bar 1: High=105 (triggers stop), Low=95 (limit 90 NOT reached)
        //   → Re-queued with StopTriggered=true
        // Bar 2: Low=92 (limit 90 NOT reached still)
        //   → Should remain pending with StopTriggered=true (skip stop check)
        // Bar 3: Low=89 (limit 90 IS reached)
        //   → Fill at 90
        // Bar 4: strategy emits flat order
        // Bar 5: flat fills

        var bars = new[]
        {
            new BarRecord("TEST", "1D", 90m, 92m, 88m, 91m, 1000m, T0),                    // Bar 0: strategy emits order
            new BarRecord("TEST", "1D", 98m, 105m, 95m, 102m, 1000m, T0.AddDays(1)),       // Bar 1: stop triggers, limit missed
            new BarRecord("TEST", "1D", 96m, 97m, 92m, 94m, 1000m, T0.AddDays(2)),         // Bar 2: limit still missed (Low=92 > 90)
            new BarRecord("TEST", "1D", 91m, 93m, 89m, 90m, 1000m, T0.AddDays(3)),         // Bar 3: limit reached (Low=89 <= 90)
            new BarRecord("TEST", "1D", 91m, 92m, 89m, 91m, 1000m, T0.AddDays(4)),         // Bar 4: strategy emits flat order
            new BarRecord("TEST", "1D", 91m, 93m, 90m, 92m, 1000m, T0.AddDays(5)),         // Bar 5: flat fills
        };

        var order = new OrderEvent(
            Symbol: "TEST",
            Direction: Direction.Long,
            Quantity: 5m,
            OrderType: OrderType.StopLimit,
            LimitPrice: 90m,
            Timestamp: T0,
            RiskApproved: false,
            StopPrice: 100m,
            MaxBarsPending: 0,  // GTC
            StopTriggered: false);

        var dataProvider = CreateDataProvider(bars);
        var strategy = new StopLimitThenExitStrategy(order, exitOnBar: 4);
        var riskLayer = CreatePassThroughRiskLayer();
        var executionHandler = CreateZeroSlippageExecutionHandler();

        var engine = CreateEngine(dataProvider, strategy, riskLayer, executionHandler);
        var config = CreateConfig();

        // Act
        var result = await engine.RunAsync(config);

        // Assert: Order fills on bar 3 at limit price 90, then closes
        Assert.Equal(BacktestStatus.Completed, result.Status);
        Assert.Equal(1, result.TotalTrades);

        var trade = result.Trades[0];
        Assert.Equal(90m, trade.EntryPrice);
        Assert.Equal(5m, trade.Quantity);
    }

    [Fact]
    public async Task TryFillStopLimit_StopNeverTriggers_OrderNeverFills()
    {
        // Arrange:
        // Long stop-limit order: stopPrice=100, limitPrice=95
        // All bars have High < 100 → stop never triggers → order never fills

        var bars = new[]
        {
            new BarRecord("TEST", "1D", 90m, 92m, 88m, 91m, 1000m, T0),                    // Bar 0: strategy emits order
            new BarRecord("TEST", "1D", 91m, 95m, 89m, 93m, 1000m, T0.AddDays(1)),         // Bar 1: High=95 < 100, no trigger
            new BarRecord("TEST", "1D", 92m, 98m, 90m, 94m, 1000m, T0.AddDays(2)),         // Bar 2: High=98 < 100, no trigger
        };

        var order = new OrderEvent(
            Symbol: "TEST",
            Direction: Direction.Long,
            Quantity: 10m,
            OrderType: OrderType.StopLimit,
            LimitPrice: 95m,
            Timestamp: T0,
            RiskApproved: false,
            StopPrice: 100m,
            MaxBarsPending: 0,
            StopTriggered: false);

        var dataProvider = CreateDataProvider(bars);
        // Exit on bar 99 (never reached) — doesn't matter since order never fills
        var strategy = new StopLimitThenExitStrategy(order, exitOnBar: 99);
        var riskLayer = CreatePassThroughRiskLayer();
        var executionHandler = CreateZeroSlippageExecutionHandler();

        var engine = CreateEngine(dataProvider, strategy, riskLayer, executionHandler);
        var config = CreateConfig();

        // Act
        var result = await engine.RunAsync(config);

        // Assert: No fills should occur — no closed trades, equity unchanged
        Assert.Equal(BacktestStatus.Completed, result.Status);
        Assert.Equal(0, result.TotalTrades);
        Assert.Equal(100_000m, result.EndEquity);
    }

    [Fact]
    public async Task ProcessPendingOrders_TriggeredOrderRequeued_PreservesStopTriggeredState()
    {
        // Arrange:
        // This test verifies that ProcessPendingOrders uses result.TriggeredOrder
        // when re-queuing, preserving StopTriggered=true across bars.
        //
        // Long stop-limit: stopPrice=100, limitPrice=92
        // Bar 0: strategy emits order
        // Bar 1: High=101 (triggers stop), Low=95 (limit 92 NOT reached)
        //   → TriggeredOrder with StopTriggered=true is re-queued
        // Bar 2: High=80 (below original stop of 100 — if StopTriggered were lost,
        //         the stop would NOT re-trigger since High < stopPrice)
        //         Low=91 (limit 92 IS reached)
        //   → Since StopTriggered=true persists, skip stop check, fill at limit
        // Bar 3: strategy emits flat order
        // Bar 4: flat fills

        var bars = new[]
        {
            new BarRecord("TEST", "1D", 90m, 92m, 88m, 91m, 1000m, T0),                    // Bar 0: strategy emits order
            new BarRecord("TEST", "1D", 99m, 101m, 95m, 100m, 1000m, T0.AddDays(1)),       // Bar 1: stop triggers (High=101>=100), limit missed (Low=95>92)
            new BarRecord("TEST", "1D", 78m, 80m, 91m, 79m, 1000m, T0.AddDays(2)),         // Bar 2: High=80 < stopPrice=100, but StopTriggered=true → skip stop, Low=91<=92 → fill
            new BarRecord("TEST", "1D", 80m, 82m, 78m, 81m, 1000m, T0.AddDays(3)),         // Bar 3: strategy emits flat order
            new BarRecord("TEST", "1D", 81m, 83m, 79m, 82m, 1000m, T0.AddDays(4)),         // Bar 4: flat fills
        };

        var order = new OrderEvent(
            Symbol: "TEST",
            Direction: Direction.Long,
            Quantity: 10m,
            OrderType: OrderType.StopLimit,
            LimitPrice: 92m,
            Timestamp: T0,
            RiskApproved: false,
            StopPrice: 100m,
            MaxBarsPending: 0,
            StopTriggered: false);

        var dataProvider = CreateDataProvider(bars);
        var strategy = new StopLimitThenExitStrategy(order, exitOnBar: 3);
        var riskLayer = CreatePassThroughRiskLayer();
        var executionHandler = CreateZeroSlippageExecutionHandler();

        var engine = CreateEngine(dataProvider, strategy, riskLayer, executionHandler);
        var config = CreateConfig();

        // Act
        var result = await engine.RunAsync(config);

        // Assert: The order fills on bar 2 at limit price 92.
        // This proves StopTriggered=true persisted — if it hadn't, the stop would
        // not re-trigger on bar 2 (High=80 < stopPrice=100) and the order would remain unfilled.
        Assert.Equal(BacktestStatus.Completed, result.Status);
        Assert.Equal(1, result.TotalTrades);
        Assert.Equal(92m, result.Trades[0].EntryPrice);
    }
}
