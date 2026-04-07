using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// RSI strategy. Buys when RSI drops below the oversold threshold,
/// sells (goes flat) when RSI rises above the overbought threshold.
/// </summary>
[StrategyName("rsi")]
public sealed class RsiStrategy : IStrategy
{
    private readonly int _period;
    private readonly decimal _oversold;
    private readonly decimal _overbought;
    private readonly List<decimal> _closes = new();
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

        decimal rsi = ComputeRsi();

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

    private decimal ComputeRsi()
    {
        decimal avgGain = 0m, avgLoss = 0m;
        for (int i = _closes.Count - _period; i < _closes.Count; i++)
        {
            decimal change = _closes[i] - _closes[i - 1];
            if (change > 0) avgGain += change;
            else avgLoss += Math.Abs(change);
        }
        avgGain /= _period;
        avgLoss /= _period;
        if (avgLoss == 0m) return 100m;
        decimal rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }
}
