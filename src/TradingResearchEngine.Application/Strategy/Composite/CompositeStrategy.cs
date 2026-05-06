using TradingResearchEngine.Application.Strategy.Composite.Conditions;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategy.Composite;

/// <summary>
/// Runtime-configurable strategy that evaluates declarative condition expressions
/// against dynamically instantiated indicators. Registered as <c>[StrategyName("composite")]</c>.
/// Slots into the existing engine pipeline identically to compiled strategies.
/// </summary>
[StrategyName("composite")]
public sealed class CompositeStrategy : IStrategy
{
    private readonly CompositeStrategyConfig _config;
    private readonly IReadOnlyList<IIndicatorInstance> _indicators;
    private readonly IndicatorValueProvider _valueProvider;
    private readonly Func<IndicatorValueProvider, BarRecord, bool> _entryDelegate;
    private readonly Func<IndicatorValueProvider, BarRecord, bool> _exitDelegate;
    private readonly DirectionMode _directionMode;
    private bool _isInPosition;

    /// <summary>
    /// Initialises a new instance of <see cref="CompositeStrategy"/> with the specified configuration.
    /// Validates the config, instantiates indicators, and compiles entry/exit expressions at construction time (fail-fast).
    /// </summary>
    /// <param name="config">The composite strategy configuration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the configuration is invalid.</exception>
    public CompositeStrategy(CompositeStrategyConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _config = config;
        _directionMode = config.DirectionMode;

        // 1. Validate config — fail-fast with all violations
        var errors = CompositeStrategyConfigValidator.Validate(config);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Invalid CompositeStrategyConfig: {string.Join("; ", errors)}");
        }

        // 2. Instantiate indicators via IndicatorFactory
        var indicators = new List<IIndicatorInstance>(config.Indicators.Count);
        foreach (var indicatorConfig in config.Indicators)
        {
            indicators.Add(IndicatorFactory.Create(indicatorConfig));
        }
        _indicators = indicators;

        // 3. Create value provider
        _valueProvider = new IndicatorValueProvider();

        // 4. Parse, validate, and compile entry/exit expressions
        var definedIds = config.Indicators.Select(i => i.Id).ToList();

        var entryAst = ConditionParser.Parse(config.EntryCondition);
        ConditionValidator.Validate(entryAst, definedIds);
        _entryDelegate = ExpressionCompiler.Compile(entryAst);

        var exitAst = ConditionParser.Parse(config.ExitCondition);
        ConditionValidator.Validate(exitAst, definedIds);
        _exitDelegate = ExpressionCompiler.Compile(exitAst);
    }

    /// <summary>
    /// Processes a market data event by feeding the bar to all indicators, evaluating
    /// entry/exit conditions, and emitting signals based on the state machine.
    /// </summary>
    /// <param name="evt">The market data event to process.</param>
    /// <returns>
    /// A list of engine events (typically zero or one <see cref="SignalEvent"/>).
    /// Returns an empty list when no signal is generated.
    /// </returns>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        // Only process BarEvent instances
        if (evt is not BarEvent barEvent)
            return Array.Empty<EngineEvent>();

        // Convert BarEvent to BarRecord for indicator consumption
        var bar = new BarRecord(
            barEvent.Symbol,
            barEvent.Interval,
            barEvent.Open,
            barEvent.High,
            barEvent.Low,
            barEvent.Close,
            barEvent.Volume,
            barEvent.Timestamp);

        // 1. Feed bar to all indicators
        foreach (var indicator in _indicators)
        {
            indicator.Add(bar);
        }

        // 2. Update value provider with latest indicator values
        _valueProvider.Update(_indicators);

        // 3. Check AllWarm gate — no signals until all indicators are warm
        if (!_valueProvider.AllWarm)
            return Array.Empty<EngineEvent>();

        // 4. Evaluate compiled entry/exit delegates
        var entrySignal = _entryDelegate(_valueProvider, bar);
        var exitSignal = _exitDelegate(_valueProvider, bar);

        // 5. Emit signal based on state machine and direction mode
        return EvaluateStateMachine(entrySignal, exitSignal, barEvent.Symbol, barEvent.Timestamp);
    }

    /// <summary>
    /// Evaluates the state machine to determine signal emission based on entry/exit signals
    /// and the current position state.
    /// </summary>
    private IReadOnlyList<EngineEvent> EvaluateStateMachine(
        bool entrySignal,
        bool exitSignal,
        string symbol,
        DateTimeOffset timestamp)
    {
        switch (_directionMode)
        {
            case DirectionMode.Long:
                return EvaluateLongMode(entrySignal, exitSignal, symbol, timestamp);

            case DirectionMode.Short:
                return EvaluateShortMode(entrySignal, exitSignal, symbol, timestamp);

            case DirectionMode.Both:
                return EvaluateBothMode(entrySignal, exitSignal, symbol, timestamp);

            default:
                return Array.Empty<EngineEvent>();
        }
    }

    /// <summary>
    /// Long mode: entry emits Direction.Long, exit emits Direction.Flat.
    /// </summary>
    private IReadOnlyList<EngineEvent> EvaluateLongMode(
        bool entrySignal,
        bool exitSignal,
        string symbol,
        DateTimeOffset timestamp)
    {
        if (entrySignal && !_isInPosition)
        {
            _isInPosition = true;
            return new[] { new SignalEvent(symbol, Direction.Long, null, timestamp) };
        }

        if (exitSignal && _isInPosition)
        {
            _isInPosition = false;
            return new[] { new SignalEvent(symbol, Direction.Flat, null, timestamp) };
        }

        return Array.Empty<EngineEvent>();
    }

    /// <summary>
    /// Short mode: entry emits Direction.Short, exit emits Direction.Flat.
    /// </summary>
    private IReadOnlyList<EngineEvent> EvaluateShortMode(
        bool entrySignal,
        bool exitSignal,
        string symbol,
        DateTimeOffset timestamp)
    {
        if (entrySignal && !_isInPosition)
        {
            _isInPosition = true;
            return new[] { new SignalEvent(symbol, Direction.Short, null, timestamp) };
        }

        if (exitSignal && _isInPosition)
        {
            _isInPosition = false;
            return new[] { new SignalEvent(symbol, Direction.Flat, null, timestamp) };
        }

        return Array.Empty<EngineEvent>();
    }

    /// <summary>
    /// Both mode: entry emits Direction.Long (default for Both mode entry),
    /// exit emits Direction.Flat. In Both mode, the entry condition triggers a Long entry.
    /// To support Short entries in Both mode, the exit condition while not in position
    /// triggers a Short entry.
    /// </summary>
    private IReadOnlyList<EngineEvent> EvaluateBothMode(
        bool entrySignal,
        bool exitSignal,
        string symbol,
        DateTimeOffset timestamp)
    {
        if (entrySignal && !_isInPosition)
        {
            _isInPosition = true;
            return new[] { new SignalEvent(symbol, Direction.Long, null, timestamp) };
        }

        if (exitSignal && _isInPosition)
        {
            _isInPosition = false;
            return new[] { new SignalEvent(symbol, Direction.Flat, null, timestamp) };
        }

        return Array.Empty<EngineEvent>();
    }
}
