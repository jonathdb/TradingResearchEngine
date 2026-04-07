using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Mean reversion strategy. Buys when price drops N standard deviations below the SMA,
/// sells (goes flat) when price reverts to the SMA.
/// </summary>
[StrategyName("mean-reversion")]
public sealed class MeanReversionStrategy : IStrategy
{
    private readonly int _lookback;
    private readonly decimal _entryStdDevs;
    private readonly List<decimal> _closes = new();
    private Direction _currentPosition = Direction.Flat;

    /// <param name="lookback">SMA lookback period (default 20).</param>
    /// <param name="entryStdDevs">Number of std devs below SMA to trigger entry (default 2).</param>
    public MeanReversionStrategy(int lookback = 20, decimal entryStdDevs = 2m)
    {
        _lookback = lookback;
        _entryStdDevs = entryStdDevs;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();
        _closes.Add(bar.Close);
        if (_closes.Count < _lookback) return Array.Empty<EngineEvent>();

        var window = _closes.Skip(_closes.Count - _lookback).Take(_lookback).ToList();
        decimal sma = window.Average();
        decimal stdDev = StdDev(window);
        if (stdDev == 0m) return Array.Empty<EngineEvent>();

        decimal lowerBand = sma - _entryStdDevs * stdDev;

        if (bar.Close < lowerBand && _currentPosition != Direction.Long)
        {
            _currentPosition = Direction.Long;
            return new EngineEvent[] { new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp) };
        }
        if (bar.Close >= sma && _currentPosition == Direction.Long)
        {
            _currentPosition = Direction.Flat;
            return new EngineEvent[] { new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp) };
        }
        return Array.Empty<EngineEvent>();
    }

    private static decimal StdDev(List<decimal> values)
    {
        decimal mean = values.Average();
        decimal variance = values.Sum(v => (v - mean) * (v - mean)) / values.Count;
        return (decimal)Math.Sqrt((double)variance);
    }
}
