using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.UnitTests.V7;

// Feature: trading-engine-stories, Property 10: Fill Delay Deferral

/// <summary>
/// Property 10: Fill Delay Deferral.
/// For any order and FillDelayBars=D&gt;0, the order SHALL not be eligible for fill until bar B+D.
/// When an order is submitted on bar B=0 with FillDelayBars=D, it enters the delay queue
/// with BarsRemaining=D. Each subsequent bar decrements BarsRemaining. When it reaches 0,
/// the order is promoted to PendingOrders and filled on that same bar (since promotion
/// happens before ProcessPendingOrders in the bar processing pipeline).
/// Therefore: fill occurs on bar D for D&gt;0, and bar 1 for D=0 (standard NextBarOpen).
/// **Validates: Requirements 19.2**
/// </summary>
public class FillDelayProperties
{
    /// <summary>
    /// A strategy that emits a single market order on the first bar only.
    /// This allows us to precisely track when the fill occurs relative to the order submission bar.
    /// </summary>
    private sealed class EmitOnFirstBarStrategy : IStrategy
    {
        private bool _emitted;

        public void Initialize(StrategyConfig config) { }
        public void Reset() { _emitted = false; }

        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            if (!_emitted)
            {
                _emitted = true;
                return new EngineEvent[]
                {
                    new OrderEvent(
                        Symbol: evt.Symbol,
                        Direction: Direction.Long,
                        Quantity: 1m,
                        OrderType: OrderType.Market,
                        LimitPrice: null,
                        Timestamp: evt.Timestamp,
                        RiskApproved: false)
                };
            }
            return Array.Empty<EngineEvent>();
        }
    }

    /// <summary>
    /// Factory that creates EmitOnFirstBarStrategy instances.
    /// </summary>
    private sealed class EmitOnFirstBarFactory : IStrategyFactory
    {
        public string StrategyType => "emit-on-first-bar";
        public IStrategy Create(StrategyConfig config) => new EmitOnFirstBarStrategy();
    }

    /// <summary>
    /// A simple in-memory data provider that yields a fixed number of bars.
    /// </summary>
    private sealed class InMemoryBarProvider : IDataProvider
    {
        private readonly List<BarRecord> _bars;

        public InMemoryBarProvider(List<BarRecord> bars) => _bars = bars;

        public async IAsyncEnumerable<BarRecord> GetBars(
            string symbol, string interval,
            DateTimeOffset from, DateTimeOffset to,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var bar in _bars)
            {
                yield return bar;
                await Task.CompletedTask;
            }
        }

        public IAsyncEnumerable<TickRecord> GetTicks(
            string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// A pass-through risk layer that approves all orders unchanged.
    /// </summary>
    private sealed class PassThroughRiskLayer : IRiskLayer
    {
        public OrderEvent? ConvertSignal(SignalEvent signal, PortfolioSnapshot snapshot) => null;

        public OrderEvent? EvaluateOrder(OrderEvent order, PortfolioSnapshot snapshot)
            => order with { RiskApproved = true };
    }

    private static readonly DateTimeOffset BaseTime = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Generates a list of bars with consistent OHLCV data for testing.
    /// Each bar has a unique timestamp (one per day) and a stable price.
    /// </summary>
    private static List<BarRecord> GenerateBars(int count, decimal basePrice)
    {
        var bars = new List<BarRecord>(count);
        for (int i = 0; i < count; i++)
        {
            decimal price = basePrice + i;
            bars.Add(new BarRecord(
                Symbol: "TEST",
                Interval: "1D",
                Open: price,
                High: price + 1m,
                Low: price - 1m,
                Close: price,
                Volume: 1000m,
                Timestamp: BaseTime.AddDays(i)));
        }
        return bars;
    }

    /// <summary>
    /// Runs the engine with the given FillDelayBars and returns the bar index where the fill occurred.
    /// The fill bar is identified as the first equity curve point with OpenPositionCount &gt; 0.
    /// Returns -1 if no fill occurred.
    /// </summary>
    private static int RunAndGetFillBarIndex(int fillDelayBars, int barCount, decimal basePrice)
    {
        var bars = GenerateBars(barCount, basePrice);
        var dataProvider = new InMemoryBarProvider(bars);
        var factory = new EmitOnFirstBarFactory();
        var riskLayer = new PassThroughRiskLayer();
        var executionHandler = new SimulatedExecutionHandler(
            new ZeroSlippageModel(),
            new ZeroCommissionModel(),
            NullLogger<SimulatedExecutionHandler>.Instance);

        var executionConfig = new ExecutionConfig(
            FillMode: FillMode.NextBarOpen,
            FillDelayBars: fillDelayBars);

        var config = new ScenarioConfig(
            ScenarioId: "fill-delay-test",
            Description: "Fill delay property test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "in-memory",
            DataProviderOptions: new Dictionary<string, object>
            {
                ["Symbol"] = "TEST",
                ["Interval"] = "1D"
            },
            StrategyType: "emit-on-first-bar",
            StrategyParameters: new Dictionary<string, object>(),
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: "ZeroSlippageModel",
            CommissionModelType: "ZeroCommissionModel",
            InitialCash: 100_000m,
            AnnualRiskFreeRate: 0m,
            RandomSeed: null,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null,
            FillMode: FillMode.NextBarOpen,
            Execution: executionConfig);

        var engine = new BacktestEngine(
            dataProvider,
            factory,
            riskLayer,
            executionHandler,
            NullLogger<BacktestEngine>.Instance,
            NullLoggerFactory.Instance);

        var result = engine.RunAsync(config).GetAwaiter().GetResult();

        // Find the first equity curve point where OpenPositionCount > 0
        for (int i = 0; i < result.EquityCurve.Count; i++)
        {
            if (result.EquityCurve[i].OpenPositionCount > 0)
            {
                var fillTime = result.EquityCurve[i].Timestamp;
                return (int)(fillTime - BaseTime).TotalDays;
            }
        }

        return -1; // No fill found
    }

    /// <summary>
    /// For any FillDelayBars=D&gt;0, a market order emitted on bar 0 SHALL NOT be eligible
    /// for fill before bar D. The delay queue holds the order for D bars before promoting
    /// it to the pending queue, where it fills on the same bar (promotion precedes fill evaluation).
    /// **Validates: Requirements 19.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool OrderNotEligibleForFill_BeforeBarD(PositiveInt delayWrap)
    {
        // Constrain delay to [1, 5] — matching the UI range 0-5
        int delay = (delayWrap.Get % 5) + 1;
        // Need enough bars: bar 0 (emit) + D bars for delay + 1 extra
        int barCount = delay + 2;
        decimal basePrice = 100m;

        int fillBarIndex = RunAndGetFillBarIndex(delay, barCount, basePrice);

        // The fill must not occur before bar D.
        // With D bars of delay, the order is promoted on bar D and filled on bar D.
        return fillBarIndex >= delay;
    }

    /// <summary>
    /// For FillDelayBars=0, a market order emitted on bar 0 fills on bar 1 (standard NextBarOpen).
    /// This serves as the baseline comparison for the delay property.
    /// **Validates: Requirements 19.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ZeroDelay_FillsOnNextBar(PositiveInt basePriceWrap)
    {
        decimal basePrice = (basePriceWrap.Get % 900) + 100m;
        int barCount = 5;

        int fillBarIndex = RunAndGetFillBarIndex(0, barCount, basePrice);

        // With zero delay, the order fills on bar 1 (NextBarOpen)
        return fillBarIndex == 1;
    }

    /// <summary>
    /// For any FillDelayBars=D&gt;0, the fill occurs exactly on bar D.
    /// The delay queue decrements BarsRemaining each bar; when it reaches 0 on bar D,
    /// the order is promoted to PendingOrders and filled on that same bar.
    /// This means D=1 fills on bar 1 (same as D=0), D=2 fills on bar 2, etc.
    /// **Validates: Requirements 19.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool DelayedFill_OccursExactlyOnBarD(PositiveInt delayWrap)
    {
        int delay = (delayWrap.Get % 5) + 1;
        int barCount = delay + 2;
        decimal basePrice = 100m;

        int fillBarIndex = RunAndGetFillBarIndex(delay, barCount, basePrice);

        // Fill occurs on bar D: the order is promoted from delay queue on bar D
        // and filled immediately (promotion precedes ProcessPendingOrders)
        return fillBarIndex == delay;
    }
}
