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
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Core.Engine;

/// <summary>
/// Event-driven backtest engine. Outer heartbeat loop drives the simulation;
/// inner dispatch loop routes typed events through the pipeline.
/// </summary>
public sealed class BacktestEngine : IBacktestEngine
{
    private readonly IDataProvider _dataProvider;
    private readonly IStrategy _strategy;
    private readonly IRiskLayer _riskLayer;
    private readonly IExecutionHandler _executionHandler;
    private readonly ILogger<BacktestEngine> _logger;

    /// <summary>Initialises the engine with all required pipeline components.</summary>
    public BacktestEngine(
        IDataProvider dataProvider,
        IStrategy strategy,
        IRiskLayer riskLayer,
        IExecutionHandler executionHandler,
        ILogger<BacktestEngine> logger)
    {
        _dataProvider = dataProvider;
        _strategy = strategy;
        _riskLayer = riskLayer;
        _executionHandler = executionHandler;
        _logger = logger;
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
        var state = new RunState();

        try
        {
            // Outer heartbeat loop
            while (dataHandler.HasMore && !ct.IsCancellationRequested)
            {
                await dataHandler.EmitNextAsync(queue, ct);

                // Inner event-dispatch loop
                while (queue.TryDequeue(out var evt) && evt is not null)
                {
                    Dispatch(evt, queue, portfolio, state);
                    if (state.Status == BacktestStatus.Failed) break;
                }
                if (state.Status == BacktestStatus.Failed) break;
            }

            if (ct.IsCancellationRequested) state.Status = BacktestStatus.Cancelled;

            // Drain remaining events on clean exit
            if (state.Status == BacktestStatus.Completed)
            {
                while (queue.TryDequeue(out var evt) && evt is not null)
                    Dispatch(evt, queue, portfolio, state);
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

    private void Dispatch(EngineEvent evt, IEventQueue queue, Portfolio.Portfolio portfolio, RunState state)
    {
        switch (evt)
        {
            case MarketDataEvent mde:
                state.LastMarketEvent = mde;
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
                }
                break;

            case SignalEvent signal:
                var order = _riskLayer.ConvertSignal(signal, portfolio.TakeSnapshot());
                if (order is not null)
                    queue.Enqueue(order with { RiskApproved = true });
                break;

            case OrderEvent { RiskApproved: false } rawOrder:
                var approved = _riskLayer.EvaluateOrder(rawOrder, portfolio.TakeSnapshot());
                if (approved is not null)
                    queue.Enqueue(approved with { RiskApproved = true });
                break;

            case OrderEvent { RiskApproved: true } approvedOrder:
                if (state.LastMarketEvent is not null)
                    queue.Enqueue(_executionHandler.Execute(approvedOrder, state.LastMarketEvent));
                break;

            case FillEvent fill:
                portfolio.Update(fill);
                break;

            default:
                _logger.LogWarning("UnrecognisedEvent: event type {Type} discarded.", evt.GetType().Name);
                break;
        }
    }

    private sealed class RunState
    {
        public BacktestStatus Status { get; set; } = BacktestStatus.Completed;
        public MarketDataEvent? LastMarketEvent { get; set; }
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
            SharpeRatio: MetricsCalculator.ComputeSharpeRatio(trades, config.AnnualRiskFreeRate),
            SortinoRatio: MetricsCalculator.ComputeSortinoRatio(trades, config.AnnualRiskFreeRate),
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
