using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Simple Moving Average crossover strategy using O(1) rolling sum accumulators.
/// Goes long when the fast SMA crosses above the slow SMA, flat when it crosses below.
/// Emits SignalEvents with the current close price as Strength (used by RiskLayer for position sizing).
/// </summary>
[StrategyName("sma-crossover")]
public sealed class SmaCrossoverStrategy : IStrategy
{
    private readonly int _fastPeriod;
    private readonly int _slowPeriod;
    private readonly List<decimal> _closes = new();
    private decimal _fastSum;
    private decimal _slowSum;
    private Direction _currentPosition = Direction.Flat;

    /// <summary>Creates an SMA crossover with configurable fast and slow periods.</summary>
    public SmaCrossoverStrategy(int fastPeriod = 10, int slowPeriod = 30)
    {
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _closes.Add(bar.Close);
        int count = _closes.Count;

        // Accumulate sums during warmup
        if (count <= _fastPeriod) _fastSum += bar.Close;
        else { _fastSum += bar.Close - _closes[count - _fastPeriod - 1]; }

        if (count <= _slowPeriod) _slowSum += bar.Close;
        else { _slowSum += bar.Close - _closes[count - _slowPeriod - 1]; }

        if (count < _slowPeriod) return Array.Empty<EngineEvent>();

        decimal fastSma = _fastSum / _fastPeriod;
        decimal slowSma = _slowSum / _slowPeriod;

        Direction signal;
        if (fastSma > slowSma && _currentPosition != Direction.Long)
            signal = Direction.Long;
        else if (fastSma <= slowSma && _currentPosition == Direction.Long)
            signal = Direction.Flat;
        else
            return Array.Empty<EngineEvent>();

        _currentPosition = signal;
        return new EngineEvent[]
        {
            new SignalEvent(bar.Symbol, signal, bar.Close, bar.Timestamp)
        };
    }
}
