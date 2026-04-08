using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Queue;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Sessions;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Core.Engine;

/// <summary>
/// Event-driven backtest engine. Outer heartbeat loop drives the simulation;
/// inner dispatch loop routes typed events through the pipeline.
/// V2: corrected per-bar processing order eliminates look-ahead bias.
/// </summary>
public sealed class BacktestEngine : IBacktestEngine
{
    private readonly IDataProvider _dataProvider;
    private readonly IStrategy _strategy;
    private readonly IRiskLayer _riskLayer;
    private readonly IExecutionHandler _executionHandler;
    private readonly ISessionCalendar? _sessionCalendar;
    private readonly ILogger<BacktestEngine> _logger;

    /// <summary>Initialises the engine with all required pipeline components.</summary>
    public BacktestEngine(
        IDataProvider dataProvider,
        IStrategy strategy,
        IRiskLayer riskLayer,
        IExecutionHandler executionHandler,
        ILogger<BacktestEngine> logger,
        ISessionCalendar? sessionCalendar = null)
    {
        _dataProvider = dataProvider;
        _strategy = strategy;
        _riskLayer = riskLayer;
        _executionHandler = executionHandler;
        _logger = logger;
        _sessionCalendar = sessionCalendar;
    }

    /// <inheritdoc/>
    public async Task<BacktestResult> RunAsync(ScenarioConfig config, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        var portfolio = new Portfolio.Portfolio(config.InitialCash,
            loggerFactory.CreateLogger<Portfolio.Portfolio>());
        var queue = new EventQueue();
        var dataHandler = new DataHandler(_dataProvider, config, loggerFactory.CreateLogger<DataHandler>());
        var state = new RunState(config.EffectiveFillMode);

        try
        {
            while (dataHandler.HasMore && !ct.IsCancellationRequested)
            {
                await dataHandler.EmitNextAsync(queue, ct);

                while (queue.TryDequeue(out var evt) && evt is not null)
                {
                    if (evt is MarketDataEvent mde)
                    {
                        ProcessBar(mde, queue, portfolio, state);
                    }
                    else
                    {
                        DispatchNonMarketEvent(evt, queue, portfolio, state);
                    }
                    if (state.Status == BacktestStatus.Failed) break;
                }
                if (state.Status == BacktestStatus.Failed) break;
            }

            if (ct.IsCancellationRequested) state.Status = BacktestStatus.Cancelled;

            // Drain remaining events on clean exit
            if (state.Status == BacktestStatus.Completed)
            {
                while (queue.TryDequeue(out var evt) && evt is not null)
                    DispatchNonMarketEvent(evt, queue, portfolio, state);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "StrategyException: unhandled error during engine run.");
            state.Status = BacktestStatus.Failed;
        }

        sw.Stop();
        return BuildResult(config, portfolio, state.Status, sw.ElapsedMilliseconds, dataHandler.MalformedRecordCount);
    }

    /// <summary>
    /// V2 corrected per-bar processing order:
    /// 1. Fill pending orders from previous bar using new bar's Open
    /// 2. Mark-to-market with new bar's Close
    /// 3. Pass new bar to strategy
    /// 4. New signals → risk layer → approved orders enter pending queue
    /// </summary>
    private void ProcessBar(MarketDataEvent mde, IEventQueue queue, Portfolio.Portfolio portfolio, RunState state)
    {
        state.LastMarketEvent = mde;

        // Step 1: Fill pending orders from previous bar
        if (state.FillMode == FillMode.NextBarOpen)
            ProcessPendingOrders(mde, portfolio, state);

        // Step 2: Mark-to-market with current bar data
        if (mde is BarEvent bar)
            portfolio.MarkToMarket(bar.Symbol, bar.Close, bar.Timestamp);
        else if (mde is TickEvent tick)
            portfolio.MarkToMarket(tick.Symbol, tick.LastTrade.Price, tick.Timestamp);

        // Session filter: skip strategy invocation for bars outside allowed sessions
        if (_sessionCalendar is not null && !_sessionCalendar.IsTradable(mde.Timestamp))
            return;

        // Step 3: Pass to strategy
        try
        {
            var outputs = _strategy.OnMarketData(mde);
            foreach (var output in outputs)
                queue.Enqueue(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "StrategyException at {Timestamp} for {Symbol}.", mde.Timestamp, mde.Symbol);
            state.Status = BacktestStatus.Failed;
            return;
        }

        // Step 4: Drain signal/order events produced by strategy — route through risk layer
        // In NextBarOpen mode, approved orders go to pending queue (filled next bar)
        // In SameBarClose mode, approved orders execute immediately (V1 compat)
        DrainStrategyOutputs(queue, portfolio, state);
    }

    /// <summary>Drains all non-market events from the queue after strategy invocation.</summary>
    private void DrainStrategyOutputs(IEventQueue queue, Portfolio.Portfolio portfolio, RunState state)
    {
        while (queue.TryDequeue(out var evt) && evt is not null)
        {
            DispatchNonMarketEvent(evt, queue, portfolio, state);
            if (state.Status == BacktestStatus.Failed) break;
        }
    }

    /// <summary>Dispatches a non-MarketData event through the pipeline.</summary>
    private void DispatchNonMarketEvent(EngineEvent evt, IEventQueue queue, Portfolio.Portfolio portfolio, RunState state)
    {
        switch (evt)
        {
            case SignalEvent signal:
                var order = _riskLayer.ConvertSignal(signal, portfolio.TakeSnapshot());
                if (order is not null)
                    RouteApprovedOrder(order with { RiskApproved = true }, queue, portfolio, state);
                break;

            case OrderEvent { RiskApproved: false } rawOrder:
                var approved = _riskLayer.EvaluateOrder(rawOrder, portfolio.TakeSnapshot());
                if (approved is not null)
                    RouteApprovedOrder(approved with { RiskApproved = true }, queue, portfolio, state);
                break;

            case OrderEvent { RiskApproved: true } approvedOrder:
                // This path is only hit in SameBarClose mode when orders are enqueued directly
                if (state.LastMarketEvent is not null)
                {
                    var result = _executionHandler.Execute(approvedOrder, state.LastMarketEvent);
                    if (result.Fill is not null)
                        portfolio.Update(result.Fill);
                }
                break;

            case FillEvent fill:
                portfolio.Update(fill);
                break;

            default:
                _logger.LogWarning("UnrecognisedEvent: event type {Type} discarded.", evt.GetType().Name);
                break;
        }
    }

    /// <summary>
    /// Routes an approved order based on FillMode.
    /// NextBarOpen: add to pending queue for next bar.
    /// SameBarClose: enqueue for immediate execution (V1 compat).
    /// </summary>
    private void RouteApprovedOrder(OrderEvent order, IEventQueue queue, Portfolio.Portfolio portfolio, RunState state)
    {
        if (state.FillMode == FillMode.NextBarOpen)
        {
            state.PendingOrders.Add(order);
        }
        else
        {
            // SameBarClose: execute immediately
            if (state.LastMarketEvent is not null)
            {
                var result = _executionHandler.Execute(order, state.LastMarketEvent);
                if (result.Fill is not null)
                    portfolio.Update(result.Fill);
            }
        }
    }

    /// <summary>
    /// Fills pending orders from the previous bar using the new bar's data.
    /// Market orders fill at the new bar's Open price.
    /// Limit/Stop orders use intra-bar fill logic.
    /// Expired orders are dropped.
    /// </summary>
    private void ProcessPendingOrders(MarketDataEvent mde, Portfolio.Portfolio portfolio, RunState state)
    {
        if (state.PendingOrders.Count == 0) return;

        var remaining = new List<OrderEvent>();

        foreach (var order in state.PendingOrders)
        {
            var result = TryFillOrder(order, mde);

            if (result is not null && result.Fill is not null &&
                result.Outcome is ExecutionOutcome.Filled or ExecutionOutcome.PartiallyFilled)
            {
                portfolio.Update(result.Fill);

                // Partial fill: re-enqueue residual order
                if (result.Outcome == ExecutionOutcome.PartiallyFilled && result.RemainingQuantity > 0m)
                {
                    remaining.Add(order with { Quantity = result.RemainingQuantity });
                }
            }
            else if (result is not null && result.Outcome == ExecutionOutcome.Rejected)
            {
                _logger.LogWarning("OrderRejected: {Symbol} {OrderType} — {Reason}",
                    order.Symbol, order.OrderType, result.RejectionReason ?? "unknown");
            }
            else
            {
                // Unfilled — check expiry (MaxBarsPending: 0 = GTC, >0 = decrement and expire at 0)
                if (order.MaxBarsPending > 0)
                {
                    int barsLeft = order.MaxBarsPending - 1;
                    if (barsLeft <= 0)
                    {
                        _logger.LogInformation("OrderExpired: {Symbol} {OrderType} order expired after MaxBarsPending.", order.Symbol, order.OrderType);
                        continue;
                    }
                    remaining.Add(order with { MaxBarsPending = barsLeft });
                }
                else
                {
                    remaining.Add(order); // GTC — keep
                }
            }
        }

        state.PendingOrders.Clear();
        state.PendingOrders.AddRange(remaining);
    }

    /// <summary>Attempts to fill an order against the current market data. Returns null if no fill attempt possible.</summary>
    private ExecutionResult? TryFillOrder(OrderEvent order, MarketDataEvent mde)
    {
        if (mde is BarEvent bar)
        {
            return order.OrderType switch
            {
                OrderType.Market => FillMarketAtOpen(order, bar),
                OrderType.Limit => TryFillLimit(order, bar),
                OrderType.StopMarket => TryFillStopMarket(order, bar),
                OrderType.StopLimit => TryFillStopLimit(order, bar),
                _ => null
            };
        }

        // Tick events: fill immediately via execution handler
        return _executionHandler.Execute(order, mde);
    }

    /// <summary>Fills a market order at the bar's Open price + slippage.</summary>
    private ExecutionResult? FillMarketAtOpen(OrderEvent order, BarEvent bar)
    {
        var openBar = bar with { Close = bar.Open };
        return _executionHandler.Execute(order, openBar);
    }

    /// <summary>Limit buy: fill if bar.Low &lt;= LimitPrice. Limit sell: fill if bar.High &gt;= LimitPrice.</summary>
    private ExecutionResult? TryFillLimit(OrderEvent order, BarEvent bar)
    {
        if (!order.LimitPrice.HasValue) return null;
        decimal limitPrice = order.LimitPrice.Value;

        if (order.Direction == Direction.Long && bar.Low <= limitPrice)
            return CreateFillAtPrice(order, limitPrice, bar.Timestamp);
        if (order.Direction == Direction.Flat && bar.High >= limitPrice)
            return CreateFillAtPrice(order, limitPrice, bar.Timestamp);
        return null;
    }

    /// <summary>Stop-market buy: fill if bar.High &gt;= StopPrice. Stop-market sell: fill if bar.Low &lt;= StopPrice.</summary>
    private ExecutionResult? TryFillStopMarket(OrderEvent order, BarEvent bar)
    {
        if (!order.StopPrice.HasValue) return null;
        decimal stopPrice = order.StopPrice.Value;

        if (order.Direction == Direction.Long && bar.High >= stopPrice)
        {
            var syntheticBar = bar with { Close = stopPrice };
            return _executionHandler.Execute(order, syntheticBar);
        }
        if (order.Direction == Direction.Flat && bar.Low <= stopPrice)
        {
            var syntheticBar = bar with { Close = stopPrice };
            return _executionHandler.Execute(order, syntheticBar);
        }
        return null;
    }

    /// <summary>
    /// Stop-limit: trigger if stop condition met, then fill if limit condition met.
    /// If triggered but not filled, converts to pending limit order.
    /// </summary>
    private ExecutionResult? TryFillStopLimit(OrderEvent order, BarEvent bar)
    {
        if (!order.StopPrice.HasValue || !order.LimitPrice.HasValue) return null;

        bool triggered = order.StopTriggered;
        decimal stopPrice = order.StopPrice.Value;
        decimal limitPrice = order.LimitPrice.Value;

        if (!triggered)
        {
            if (order.Direction == Direction.Long && bar.High >= stopPrice)
                triggered = true;
            else if (order.Direction == Direction.Flat && bar.Low <= stopPrice)
                triggered = true;
        }

        if (!triggered) return null;

        if (order.Direction == Direction.Long && bar.Low <= limitPrice)
            return CreateFillAtPrice(order, limitPrice, bar.Timestamp);
        if (order.Direction == Direction.Flat && bar.High >= limitPrice)
            return CreateFillAtPrice(order, limitPrice, bar.Timestamp);

        // Triggered but not filled — return Unfilled so caller keeps it in queue
        return new ExecutionResult(ExecutionOutcome.Unfilled, null);
    }

    /// <summary>Creates a fill at a specific price using the execution handler for commission calculation.</summary>
    private ExecutionResult? CreateFillAtPrice(OrderEvent order, decimal fillPrice, DateTimeOffset timestamp)
    {
        var syntheticBar = new BarEvent(order.Symbol, "1D", fillPrice, fillPrice, fillPrice, fillPrice, 0m, timestamp);
        var result = _executionHandler.Execute(order, syntheticBar);

        if (result.Fill is null) return null;

        var adjustedFill = result.Fill with { FillPrice = fillPrice, SlippageAmount = 0m };
        return new ExecutionResult(ExecutionOutcome.Filled, adjustedFill);
    }

    private sealed class RunState
    {
        public RunState(FillMode fillMode) => FillMode = fillMode;
        public BacktestStatus Status { get; set; } = BacktestStatus.Completed;
        public MarketDataEvent? LastMarketEvent { get; set; }
        public FillMode FillMode { get; }
        public List<OrderEvent> PendingOrders { get; } = new();
    }

    private static BacktestResult BuildResult(
        ScenarioConfig config,
        Portfolio.Portfolio portfolio,
        BacktestStatus status,
        long durationMs,
        int malformedCount)
    {
        var trades = portfolio.ClosedTrades;
        var curve = portfolio.EquityCurve;
        var startEq = portfolio.StartEquity;
        var endEq = portfolio.TotalEquity;
        return new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: config,
            Status: status,
            EquityCurve: curve,
            Trades: trades,
            StartEquity: startEq,
            EndEquity: endEq,
            MaxDrawdown: MetricsCalculator.ComputeMaxDrawdown(curve),
            SharpeRatio: MetricsCalculator.ComputeSharpeRatio(curve, config.AnnualRiskFreeRate, config.BarsPerYear),
            SortinoRatio: MetricsCalculator.ComputeSortinoRatio(curve, config.AnnualRiskFreeRate, config.BarsPerYear),
            CalmarRatio: MetricsCalculator.ComputeCalmarRatio(curve, startEq, endEq),
            ReturnOnMaxDrawdown: MetricsCalculator.ComputeReturnOnMaxDrawdown(curve, startEq, endEq),
            TotalTrades: trades.Count,
            WinRate: MetricsCalculator.ComputeWinRate(trades),
            ProfitFactor: MetricsCalculator.ComputeProfitFactor(trades),
            AverageWin: MetricsCalculator.ComputeAverageWin(trades),
            AverageLoss: MetricsCalculator.ComputeAverageLoss(trades),
            Expectancy: MetricsCalculator.ComputeExpectancy(trades),
            AverageHoldingPeriod: MetricsCalculator.ComputeAverageHoldingPeriod(trades),
            EquityCurveSmoothness: MetricsCalculator.ComputeEquityCurveSmoothness(curve),
            MaxConsecutiveLosses: MetricsCalculator.ComputeMaxConsecutiveLosses(trades),
            MaxConsecutiveWins: MetricsCalculator.ComputeMaxConsecutiveWins(trades),
            RunDurationMs: durationMs);
    }
}
