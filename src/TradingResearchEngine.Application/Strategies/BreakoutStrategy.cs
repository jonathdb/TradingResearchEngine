using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Breakout strategy. Buys on N-bar high breakout, sells (goes flat) on N-bar low breakdown.
/// </summary>
[StrategyName("breakout")]
public sealed class BreakoutStrategy : IStrategy
{
    private readonly int _lookback;
    private readonly List<decimal> _highs = new();
    private readonly List<decimal> _lows = new();
    private readonly List<decimal> _closes = new();
    private Direction _currentPosition = Direction.Flat;

    /// <param name="lookback">Number of bars for high/low channel (default 20).</param>
    public BreakoutStrategy(int lookback = 20) => _lookback = lookback;

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _highs.Add(bar.High);
        _lows.Add(bar.Low);
        _closes.Add(bar.Close);

        if (_closes.Count <= _lookback) return Array.Empty<EngineEvent>();

        decimal channelHigh = _highs.Skip(_highs.Count - _lookback - 1).Take(_lookback).Max();
        decimal channelLow = _lows.Skip(_lows.Count - _lookback - 1).Take(_lookback).Min();

        if (bar.Close > channelHigh && _currentPosition != Direction.Long)
        {
            _currentPosition = Direction.Long;
            return new EngineEvent[] { new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp) };
        }
        if (bar.Close < channelLow && _currentPosition == Direction.Long)
        {
            _currentPosition = Direction.Flat;
            return new EngineEvent[] { new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp) };
        }
        return Array.Empty<EngineEvent>();
    }
}
