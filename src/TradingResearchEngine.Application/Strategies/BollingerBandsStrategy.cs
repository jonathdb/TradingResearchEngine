using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Bollinger Bands mean-reversion strategy using O(1) rolling sum accumulators.
/// Entry: close drops below the lower Bollinger Band → buy (expect reversion to mean).
/// Exit: close rises above the upper Bollinger Band → sell (mean reversion complete).
/// </summary>
[StrategyName("bollinger-bands")]
public sealed class BollingerBandsStrategy : IStrategy
{
    private readonly int _period;
    private readonly decimal _stdDevMultiplier;
    private readonly bool _exitAtMiddle;
    private readonly List<decimal> _closes = new();
    private decimal _sum;
    private decimal _sumSq;
    private Direction _position = Direction.Flat;

    /// <param name="period">SMA lookback period (default 30).</param>
    /// <param name="stdDevMultiplier">Band width multiplier (default 2).</param>
    /// <param name="exitAtMiddle">Exit at middle band instead of upper (default false).</param>
    public BollingerBandsStrategy(int period = 30, decimal stdDevMultiplier = 2m, bool exitAtMiddle = false)
    {
        _period = period;
        _stdDevMultiplier = stdDevMultiplier;
        _exitAtMiddle = exitAtMiddle;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _closes.Add(bar.Close);
        int count = _closes.Count;

        if (count <= _period)
        {
            _sum += bar.Close;
            _sumSq += bar.Close * bar.Close;
        }
        else
        {
            decimal departed = _closes[count - _period - 1];
            _sum += bar.Close - departed;
            _sumSq += bar.Close * bar.Close - departed * departed;
        }

        if (count < _period) return Array.Empty<EngineEvent>();

        decimal sma = _sum / _period;
        decimal variance = _sumSq / _period - sma * sma;
        if (variance <= 0m) return Array.Empty<EngineEvent>();
        decimal stdDev = (decimal)Math.Sqrt((double)variance);

        decimal upperBand = sma + _stdDevMultiplier * stdDev;
        decimal lowerBand = sma - _stdDevMultiplier * stdDev;

        if (bar.Close < lowerBand && _position != Direction.Long)
        {
            _position = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }

        decimal exitLevel = _exitAtMiddle ? sma : upperBand;
        if (bar.Close > exitLevel && _position == Direction.Long)
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
