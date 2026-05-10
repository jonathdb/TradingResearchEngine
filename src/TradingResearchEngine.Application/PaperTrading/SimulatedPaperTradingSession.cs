using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
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
using CorePortfolio = TradingResearchEngine.Core.Portfolio.Portfolio;

namespace TradingResearchEngine.Application.PaperTrading;

/// <summary>
/// Paper trading session that reuses the same execution pipeline as backtesting.
/// Guarantees metric equivalence with historical backtests for the same data.
/// Implements a state machine: Idle → Connecting → Running ⇄ Paused → Stopped | Error.
/// </summary>
public sealed class SimulatedPaperTradingSession : IPaperTradingSession, IDisposable
{
    private readonly IStreamingDataProvider _streamingDataProvider;
    private readonly IStrategy _strategy;
    private readonly IRiskLayer _riskLayer;
    private readonly IExecutionHandler _executionHandler;
    private readonly ISlippageModel _slippageModel;
    private readonly ICommissionModel _commissionModel;
    private readonly IRepository<PaperSessionRecord> _repository;
    private readonly ILogger<SimulatedPaperTradingSession> _logger;

    private readonly Subject<PaperBarEvent> _barSubject = new();
    private readonly Subject<PaperTradeEvent> _tradeSubject = new();

    private CorePortfolio? _portfolio;
    private EventQueue? _queue;
    private CancellationTokenSource? _pauseCts;
    private CancellationTokenSource? _linkedCts;
    private Task? _processingTask;
    private PaperTradingResult? _cachedResult;
    private DateTimeOffset _startedAt;
    private MarketDataEvent? _lastMarketEvent;
    private ScenarioConfig? _config;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of <see cref="SimulatedPaperTradingSession"/>.
    /// </summary>
    /// <param name="streamingDataProvider">Provides streaming bar data.</param>
    /// <param name="strategy">The strategy to execute on each bar.</param>
    /// <param name="riskLayer">Risk evaluation layer for signal-to-order conversion.</param>
    /// <param name="executionHandler">Handles order execution with slippage and commission.</param>
    /// <param name="slippageModel">Slippage model for fill price adjustment.</param>
    /// <param name="commissionModel">Commission model for trade cost computation.</param>
    /// <param name="repository">Repository for persisting session records.</param>
    /// <param name="logger">Logger instance.</param>
    public SimulatedPaperTradingSession(
        IStreamingDataProvider streamingDataProvider,
        IStrategy strategy,
        IRiskLayer riskLayer,
        IExecutionHandler executionHandler,
        ISlippageModel slippageModel,
        ICommissionModel commissionModel,
        IRepository<PaperSessionRecord> repository,
        ILogger<SimulatedPaperTradingSession> logger)
    {
        _streamingDataProvider = streamingDataProvider;
        _strategy = strategy;
        _riskLayer = riskLayer;
        _executionHandler = executionHandler;
        _slippageModel = slippageModel;
        _commissionModel = commissionModel;
        _repository = repository;
        _logger = logger;
    }

    /// <inheritdoc/>
    public PaperTradingStatus Status { get; private set; } = PaperTradingStatus.Idle;

    /// <inheritdoc/>
    public CorePortfolio Portfolio => _portfolio
        ?? throw new InvalidOperationException("Session has not been started.");

    /// <inheritdoc/>
    public IObservable<PaperBarEvent> BarStream => _barSubject;

    /// <inheritdoc/>
    public IObservable<PaperTradeEvent> TradeStream => _tradeSubject;

    /// <inheritdoc/>
    public async Task StartAsync(ScenarioConfig config, CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (Status != PaperTradingStatus.Idle)
                throw new InvalidOperationException($"Cannot start session in {Status} state.");

            _config = config;
            Status = PaperTradingStatus.Connecting;
            _startedAt = DateTimeOffset.UtcNow;

            var loggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
            _portfolio = new CorePortfolio(config.InitialCash,
                loggerFactory.CreateLogger<CorePortfolio>());
            _queue = new EventQueue();
            _pauseCts = new CancellationTokenSource();
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _pauseCts.Token);

            Status = PaperTradingStatus.Running;
            _processingTask = ProcessBarsAsync(_linkedCts.Token);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<PaperTradingResult> StopAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (Status == PaperTradingStatus.Stopped && _cachedResult is not null)
                return _cachedResult;

            if (Status != PaperTradingStatus.Running && Status != PaperTradingStatus.Paused)
                throw new InvalidOperationException($"Cannot stop session in {Status} state.");

            Status = PaperTradingStatus.Stopped;

            // Cancel the processing loop
            _pauseCts?.Cancel();

            // Wait for processing to complete
            if (_processingTask is not null)
            {
                try { await _processingTask; }
                catch (OperationCanceledException) { /* expected */ }
            }

            _cachedResult = BuildResult();
            await PersistSessionRecord();

            _barSubject.OnCompleted();
            _tradeSubject.OnCompleted();

            return _cachedResult;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task PauseAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            if (Status != PaperTradingStatus.Running)
                throw new InvalidOperationException($"Cannot pause session in {Status} state.");

            Status = PaperTradingStatus.Paused;
            _pauseCts?.Cancel();

            // Wait for processing to actually stop
            if (_processingTask is not null)
            {
                try { await _processingTask; }
                catch (OperationCanceledException) { /* expected */ }
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ResumeAsync(CancellationToken ct)
    {
        await _stateLock.WaitAsync(ct);
        try
        {
            if (Status != PaperTradingStatus.Paused)
                throw new InvalidOperationException($"Cannot resume session in {Status} state.");

            _pauseCts = new CancellationTokenSource();
            _linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _pauseCts.Token);

            Status = PaperTradingStatus.Running;
            _processingTask = ProcessBarsAsync(_linkedCts.Token);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task ProcessBarsAsync(CancellationToken ct)
    {
        try
        {
            var dataConfig = _config!.EffectiveDataConfig;
            var symbol = dataConfig.DataProviderOptions.TryGetValue("Symbol", out var sym)
                ? sym?.ToString() ?? "UNKNOWN"
                : "UNKNOWN";
            var interval = dataConfig.Timeframe ?? "1d";

            await foreach (var bar in _streamingDataProvider.StreamAsync(symbol, interval, ct))
            {
                ct.ThrowIfCancellationRequested();
                ProcessSingleBar(bar);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on pause or stop
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Paper trading session encountered an error.");
            Status = PaperTradingStatus.Error;
            _barSubject.OnError(ex);
            _tradeSubject.OnError(ex);
        }
    }

    private void ProcessSingleBar(BarRecord bar)
    {
        var barEvent = new BarEvent(
            bar.Symbol, bar.Interval, bar.Open, bar.High, bar.Low,
            bar.Close, bar.Volume, bar.Timestamp);

        _lastMarketEvent = barEvent;
        var previousTradeCount = _portfolio!.ClosedTrades.Count;

        // Step 1: Mark-to-market with current bar
        _portfolio.MarkToMarket(bar.Symbol, bar.Close, bar.Timestamp);

        // Step 2: Pass to strategy
        var outputs = _strategy.OnMarketData(barEvent);
        foreach (var output in outputs)
            _queue!.Enqueue(output);

        // Step 3: Drain events through risk layer and execution
        DrainEvents();

        // Emit bar event
        var snapshot = _portfolio.TakeSnapshot();
        _barSubject.OnNext(new PaperBarEvent(bar, DateTimeOffset.UtcNow, snapshot));

        // Emit trade events for any new closed trades
        var closedTrades = _portfolio.ClosedTrades;
        for (int i = previousTradeCount; i < closedTrades.Count; i++)
        {
            var tradeSnapshot = _portfolio.TakeSnapshot();
            _tradeSubject.OnNext(new PaperTradeEvent(closedTrades[i], DateTimeOffset.UtcNow, tradeSnapshot));
        }
    }

    private void DrainEvents()
    {
        while (_queue!.TryDequeue(out var evt) && evt is not null)
        {
            switch (evt)
            {
                case SignalEvent signal:
                    var order = _riskLayer.ConvertSignal(signal, _portfolio!.TakeSnapshot());
                    if (order is not null)
                    {
                        ExecuteOrder(order with { RiskApproved = true });
                    }
                    break;

                case OrderEvent { RiskApproved: false } rawOrder:
                    var approved = _riskLayer.EvaluateOrder(rawOrder, _portfolio!.TakeSnapshot());
                    if (approved is not null)
                    {
                        ExecuteOrder(approved with { RiskApproved = true });
                    }
                    break;

                case OrderEvent { RiskApproved: true } approvedOrder:
                    ExecuteOrder(approvedOrder);
                    break;

                case FillEvent fill:
                    _portfolio!.Update(fill);
                    break;
            }
        }
    }

    private void ExecuteOrder(OrderEvent order)
    {
        if (_lastMarketEvent is null) return;

        var result = _executionHandler.Execute(order, _lastMarketEvent);
        if (result.Fill is not null)
        {
            _portfolio!.Update(result.Fill);
        }
    }

    private PaperTradingResult BuildResult()
    {
        var trades = _portfolio!.ClosedTrades;
        var curve = _portfolio.EquityCurve;
        var config = _config!;

        var backtestResult = new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: config,
            Status: BacktestStatus.Completed,
            EquityCurve: curve,
            Trades: trades,
            StartEquity: _portfolio.StartEquity,
            EndEquity: _portfolio.TotalEquity,
            MaxDrawdown: MetricsCalculator.ComputeMaxDrawdown(curve),
            SharpeRatio: MetricsCalculator.ComputeSharpeRatio(curve, config.AnnualRiskFreeRate, config.BarsPerYear),
            SortinoRatio: MetricsCalculator.ComputeSortinoRatio(curve, config.AnnualRiskFreeRate, config.BarsPerYear),
            CalmarRatio: MetricsCalculator.ComputeCalmarRatio(curve, _portfolio.StartEquity, _portfolio.TotalEquity, config.BarsPerYear),
            ReturnOnMaxDrawdown: MetricsCalculator.ComputeReturnOnMaxDrawdown(curve, _portfolio.StartEquity, _portfolio.TotalEquity),
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
            RunDurationMs: (long)(DateTimeOffset.UtcNow - _startedAt).TotalMilliseconds);

        return new PaperTradingResult(
            FinalPortfolio: _portfolio,
            ClosedTrades: trades,
            EquivalentBacktestResult: backtestResult,
            FinalStatus: PaperTradingStatus.Stopped,
            StartedAt: _startedAt,
            StoppedAt: DateTimeOffset.UtcNow);
    }

    private async Task PersistSessionRecord()
    {
        var record = new PaperSessionRecord(
            Id: Guid.NewGuid().ToString(),
            StrategyVersionId: _config?.ScenarioId ?? "unknown",
            StartedAt: _startedAt,
            StoppedAt: DateTimeOffset.UtcNow,
            Status: PaperTradingStatus.Stopped,
            FinalPnl: _portfolio!.TotalEquity - _portfolio.StartEquity,
            TradeCount: _portfolio.ClosedTrades.Count);

        await _repository.SaveAsync(record);
    }

    /// <summary>Disposes resources used by the session.</summary>
    public void Dispose()
    {
        _pauseCts?.Dispose();
        _linkedCts?.Dispose();
        _barSubject.Dispose();
        _tradeSubject.Dispose();
        _stateLock.Dispose();
    }
}
