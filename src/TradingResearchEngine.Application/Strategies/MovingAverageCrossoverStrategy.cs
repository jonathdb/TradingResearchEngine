using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Moving Average Crossover strategy using IIndicatorSeries wrappers.
///
/// Entry: fast SMA crosses above slow SMA → Long.
/// Exit: fast SMA crosses below slow SMA → Flat.
///
/// Uses <see cref="SmaIndicator"/> wrappers backed by Skender.Stock.Indicators
/// for indicator computation.
///
/// Hypothesis: Persistent directional moves are captured by moving average crossovers,
/// providing trend-following entries that overcome transaction costs.
/// </summary>
[StrategyName("moving-average-crossover")]
public sealed class MovingAverageCrossoverStrategy : IStrategy
{
    private readonly int _fastPeriod;
    private readonly int _slowPeriod;
    private readonly DirectionMode _directionMode;
    private readonly SmaIndicator _fastSma;
    private readonly SmaIndicator _slowSma;
    private Direction _position = Direction.Flat;
    private int _barCount;

    /// <summary>Creates a moving average crossover strategy.</summary>
    /// <param name="fastPeriod">Fast SMA lookback period (default 10).</param>
    /// <param name="slowPeriod">Slow SMA lookback period (default 30).</param>
    /// <param name="directionMode">Signal direction mode: Long, Short, or Both (default Long).</param>
    public MovingAverageCrossoverStrategy(
        [ParameterMeta(DisplayName = "Fast Period", Description = "Fast SMA lookback period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 0, Min = 2)]
        int fastPeriod = 10,
        [ParameterMeta(DisplayName = "Slow Period", Description = "Slow SMA lookback period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 1, Min = 5)]
        int slowPeriod = 30,
        [ParameterMeta(DisplayName = "Direction", Description = "Signal direction mode.",
            Group = "Signal", DisplayOrder = 2)]
        DirectionMode directionMode = DirectionMode.Long)
    {
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _directionMode = directionMode;
        _fastSma = new SmaIndicator(fastPeriod);
        _slowSma = new SmaIndicator(slowPeriod);
    }

    /// <inheritdoc/>
    public void Initialize(StrategyConfig config)
    {
        // Parameters are set via constructor; Initialize is a lifecycle hook.
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _fastSma.Reset();
        _slowSma.Reset();
        _position = Direction.Flat;
        _barCount = 0;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        var barRecord = new BarRecord(
            bar.Symbol, bar.Interval, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, bar.Timestamp);

        _fastSma.Add(barRecord);
        _slowSma.Add(barRecord);
        _barCount++;

        // Need both SMAs warmed up
        if (!_fastSma.IsWarm || !_slowSma.IsWarm)
            return Array.Empty<EngineEvent>();

        var fastResult = _fastSma.Results[^1];
        var slowResult = _slowSma.Results[^1];

        if (fastResult.Sma is null || slowResult.Sma is null)
            return Array.Empty<EngineEvent>();

        decimal fastValue = (decimal)fastResult.Sma.Value;
        decimal slowValue = (decimal)slowResult.Sma.Value;

        // Long entry: fast crosses above slow
        if (fastValue > slowValue && _position != Direction.Long
            && _directionMode is DirectionMode.Long or DirectionMode.Both)
        {
            _position = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }

        // Exit long: fast crosses below slow
        if (fastValue <= slowValue && _position == Direction.Long)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        // Short entry: fast crosses below slow (when mode is Short or Both)
        if (fastValue < slowValue && _position != Direction.Short
            && _directionMode is DirectionMode.Short or DirectionMode.Both)
        {
            _position = Direction.Short;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Short, bar.Close, bar.Timestamp)
            };
        }

        // Exit short: fast crosses above slow
        if (fastValue >= slowValue && _position == Direction.Short)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        return Array.Empty<EngineEvent>();
    }
}
