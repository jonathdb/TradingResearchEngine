using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Volatility-scaled trend-following strategy.
///
/// Uses a fast/slow SMA crossover for trend direction and a trailing ATR
/// for volatility warmup gating. Signal strength is the bar's Close price.
///
/// Uses <see cref="SmaIndicator"/> and <see cref="AtrIndicator"/> wrappers backed by
/// Skender.Stock.Indicators for indicator computation.
///
/// Hypothesis: Persistent directional moves continue long enough for
/// trend-following entries to overcome transaction costs.
/// </summary>
[StrategyName("volatility-scaled-trend")]
public sealed class VolatilityScaledTrendStrategy : IStrategy
{
    private readonly int _fastPeriod;
    private readonly int _slowPeriod;
    private readonly int _atrPeriod;
    private readonly DirectionMode _directionMode;
    private readonly SmaIndicator _fastSma;
    private readonly SmaIndicator _slowSma;
    private readonly AtrIndicator _atr;
    private Direction _position = Direction.Flat;
    private int _barCount;

    /// <summary>Creates a volatility-scaled trend strategy.</summary>
    /// <param name="fastPeriod">Fast SMA lookback period (default 10).</param>
    /// <param name="slowPeriod">Slow SMA lookback period (default 50).</param>
    /// <param name="atrPeriod">ATR lookback period for Wilder smoothing (default 14).</param>
    /// <param name="directionMode">Signal direction mode: Long, Short, or Both (default Long).</param>
    public VolatilityScaledTrendStrategy(
        [ParameterMeta(DisplayName = "Fast Period", Description = "Fast SMA lookback period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 0, Min = 2)]
        int fastPeriod = 10,
        [ParameterMeta(DisplayName = "Slow Period", Description = "Slow SMA lookback period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 1, Min = 5)]
        int slowPeriod = 50,
        [ParameterMeta(DisplayName = "ATR Period", Description = "ATR lookback period for Wilder smoothing.",
            SensitivityHint = SensitivityHint.Medium, Group = "Risk", DisplayOrder = 2, Min = 2)]
        int atrPeriod = 14,
        [ParameterMeta(DisplayName = "Direction", Description = "Signal direction mode.",
            Group = "Signal", DisplayOrder = 3)]
        DirectionMode directionMode = DirectionMode.Long)
    {
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _atrPeriod = atrPeriod;
        _directionMode = directionMode;
        _fastSma = new SmaIndicator(fastPeriod);
        _slowSma = new SmaIndicator(slowPeriod);
        _atr = new AtrIndicator(atrPeriod);
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
        _atr.Reset();
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
        _atr.Add(barRecord);
        _barCount++;

        // Need both SMAs warmed up and ATR warmed up
        if (!_fastSma.IsWarm || !_slowSma.IsWarm || !_atr.IsWarm)
            return Array.Empty<EngineEvent>();

        var fastResult = _fastSma.Results[^1];
        var slowResult = _slowSma.Results[^1];

        if (fastResult.Sma is null || slowResult.Sma is null)
            return Array.Empty<EngineEvent>();

        decimal fastValue = (decimal)fastResult.Sma.Value;
        decimal slowValue = (decimal)slowResult.Sma.Value;

        if (fastValue > slowValue && _position != Direction.Long
            && _directionMode is DirectionMode.Long or DirectionMode.Both)
        {
            _position = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }

        if (fastValue <= slowValue && _position == Direction.Long)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        // V6: Short signal when fast < slow (when mode is Short or Both)
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
