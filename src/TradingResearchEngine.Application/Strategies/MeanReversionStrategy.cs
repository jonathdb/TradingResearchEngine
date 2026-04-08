using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Mean reversion strategy using O(1) rolling sum accumulators.
/// Buys when price drops N standard deviations below the SMA,
/// sells (goes flat) when price reverts to the SMA.
/// </summary>
[StrategyName("mean-reversion")]
public sealed class MeanReversionStrategy : IStrategy
{
    private readonly int _lookback;
    private readonly decimal _entryStdDevs;
    private readonly List<decimal> _closes = new();
    private decimal _sum;
    private decimal _sumSq;
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
        int count = _closes.Count;

        // Rolling sum accumulation
        if (count <= _lookback)
        {
            _sum += bar.Close;
            _sumSq += bar.Close * bar.Close;
        }
        else
        {
            decimal departed = _closes[count - _lookback - 1];
            _sum += bar.Close - departed;
            _sumSq += bar.Close * bar.Close - departed * departed;
        }

        if (count < _lookback) return Array.Empty<EngineEvent>();

        decimal sma = _sum / _lookback;
        // Population variance: E[X²] - (E[X])²
        decimal variance = _sumSq / _lookback - sma * sma;
        if (variance <= 0m) return Array.Empty<EngineEvent>();
        decimal stdDev = (decimal)Math.Sqrt((double)variance);

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
}
