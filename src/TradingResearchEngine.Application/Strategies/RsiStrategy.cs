using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// RSI strategy using Wilder smoothing accumulators (O(1) per bar after warmup).
/// Buys when RSI drops below the oversold threshold,
/// sells (goes flat) when RSI rises above the overbought threshold.
/// </summary>
[StrategyName("rsi")]
public sealed class RsiStrategy : IStrategy
{
    private readonly int _period;
    private readonly decimal _oversold;
    private readonly decimal _overbought;
    private readonly List<decimal> _closes = new();
    private decimal _avgGain;
    private decimal _avgLoss;
    private bool _warmedUp;
    private Direction _currentPosition = Direction.Flat;

    /// <param name="period">RSI lookback period (default 14).</param>
    /// <param name="oversold">Buy threshold (default 30).</param>
    /// <param name="overbought">Sell threshold (default 70).</param>
    public RsiStrategy(int period = 14, decimal oversold = 30m, decimal overbought = 70m)
    {
        _period = period;
        _oversold = oversold;
        _overbought = overbought;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();
        _closes.Add(bar.Close);

        if (_closes.Count <= _period) return Array.Empty<EngineEvent>();

        if (!_warmedUp)
        {
            // Initial average: simple average of first _period changes
            decimal gainSum = 0m, lossSum = 0m;
            for (int i = _closes.Count - _period; i < _closes.Count; i++)
            {
                decimal change = _closes[i] - _closes[i - 1];
                if (change > 0) gainSum += change;
                else lossSum += Math.Abs(change);
            }
            _avgGain = gainSum / _period;
            _avgLoss = lossSum / _period;
            _warmedUp = true;
        }
        else
        {
            // Wilder smoothing: avgGain = (prev * (period-1) + currentGain) / period
            decimal change = bar.Close - _closes[_closes.Count - 2];
            decimal gain = change > 0 ? change : 0m;
            decimal loss = change < 0 ? Math.Abs(change) : 0m;
            _avgGain = (_avgGain * (_period - 1) + gain) / _period;
            _avgLoss = (_avgLoss * (_period - 1) + loss) / _period;
        }

        decimal rsi;
        if (_avgLoss == 0m) rsi = 100m;
        else
        {
            decimal rs = _avgGain / _avgLoss;
            rsi = 100m - (100m / (1m + rs));
        }

        if (rsi < _oversold && _currentPosition != Direction.Long)
        {
            _currentPosition = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }
        if (rsi > _overbought && _currentPosition == Direction.Long)
        {
            _currentPosition = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }
        return Array.Empty<EngineEvent>();
    }
}
