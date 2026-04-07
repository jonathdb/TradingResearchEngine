using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Bollinger Bands mean-reversion strategy.
/// 
/// Entry: close drops below the lower Bollinger Band → buy (expect reversion to mean).
/// Exit: close rises above the upper Bollinger Band → sell (mean reversion complete).
/// Alternative exit: close crosses above the middle band (SMA) for a tighter exit.
/// 
/// Parameters:
/// - period: SMA lookback (default 30)
/// - stdDevMultiplier: band width in standard deviations (default 2)
/// - exitAtMiddle: if true, exit when price crosses above SMA instead of upper band (default false)
/// </summary>
[StrategyName("bollinger-bands")]
public sealed class BollingerBandsStrategy : IStrategy
{
    private readonly int _period;
    private readonly decimal _stdDevMultiplier;
    private readonly bool _exitAtMiddle;
    private readonly List<decimal> _closes = new();
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
        if (_closes.Count < _period) return Array.Empty<EngineEvent>();

        // Compute Bollinger Bands
        decimal sma = 0m;
        int start = _closes.Count - _period;
        for (int i = start; i < _closes.Count; i++)
            sma += _closes[i];
        sma /= _period;

        decimal variance = 0m;
        for (int i = start; i < _closes.Count; i++)
        {
            decimal diff = _closes[i] - sma;
            variance += diff * diff;
        }
        variance /= _period;
        decimal stdDev = (decimal)Math.Sqrt((double)variance);

        decimal upperBand = sma + _stdDevMultiplier * stdDev;
        decimal lowerBand = sma - _stdDevMultiplier * stdDev;

        // Entry: close < lower band → buy (oversold, expect reversion)
        if (bar.Close < lowerBand && _position != Direction.Long)
        {
            _position = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }

        // Exit: close > upper band (or middle band if exitAtMiddle)
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
