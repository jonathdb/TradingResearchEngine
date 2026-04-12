using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Volatility-scaled trend-following strategy.
///
/// Uses a fast/slow SMA crossover for trend direction and a trailing ATR
/// for volatility warmup gating. Signal strength is the bar's Close price.
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
    private readonly List<decimal> _closes = new();
    private decimal _fastSum;
    private decimal _slowSum;
    private decimal _atr;
    private bool _atrWarmedUp;
    private int _atrCount;
    private decimal _atrInitialSum;
    private decimal _prevClose;
    private Direction _position = Direction.Flat;

    /// <summary>Creates a volatility-scaled trend strategy.</summary>
    /// <param name="fastPeriod">Fast SMA lookback period (default 10).</param>
    /// <param name="slowPeriod">Slow SMA lookback period (default 50).</param>
    /// <param name="atrPeriod">ATR lookback period for Wilder smoothing (default 14).</param>
    public VolatilityScaledTrendStrategy(
        [ParameterMeta(DisplayName = "Fast Period", Description = "Fast SMA lookback period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 0, Min = 2)]
        int fastPeriod = 10,
        [ParameterMeta(DisplayName = "Slow Period", Description = "Slow SMA lookback period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 1, Min = 5)]
        int slowPeriod = 50,
        [ParameterMeta(DisplayName = "ATR Period", Description = "ATR lookback period for Wilder smoothing.",
            SensitivityHint = SensitivityHint.Medium, Group = "Risk", DisplayOrder = 2, Min = 2)]
        int atrPeriod = 14)
    {
        _fastPeriod = fastPeriod;
        _slowPeriod = slowPeriod;
        _atrPeriod = atrPeriod;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _closes.Add(bar.Close);
        int count = _closes.Count;

        // --- SMA accumulators (O(1) rolling) ---
        if (count <= _fastPeriod) _fastSum += bar.Close;
        else _fastSum += bar.Close - _closes[count - _fastPeriod - 1];

        if (count <= _slowPeriod) _slowSum += bar.Close;
        else _slowSum += bar.Close - _closes[count - _slowPeriod - 1];

        // --- ATR via Wilder smoothing ---
        if (count >= 2)
        {
            decimal tr = ComputeTrueRange(bar.High, bar.Low, _prevClose);
            _atrCount++;

            if (!_atrWarmedUp)
            {
                _atrInitialSum += tr;
                if (_atrCount >= _atrPeriod)
                {
                    _atr = _atrInitialSum / _atrPeriod;
                    _atrWarmedUp = true;
                }
            }
            else
            {
                // Wilder smoothing: ATR = (prev * (period-1) + currentTR) / period
                _atr = (_atr * (_atrPeriod - 1) + tr) / _atrPeriod;
            }
        }

        _prevClose = bar.Close;

        // Need both SMAs warmed up and ATR warmed up
        int minBars = Math.Max(_slowPeriod, _atrPeriod + 1);
        if (count < minBars || !_atrWarmedUp) return Array.Empty<EngineEvent>();

        decimal fastSma = _fastSum / _fastPeriod;
        decimal slowSma = _slowSum / _slowPeriod;

        if (fastSma > slowSma && _position != Direction.Long)
        {
            _position = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }

        if (fastSma <= slowSma && _position == Direction.Long)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        return Array.Empty<EngineEvent>();
    }

    private static decimal ComputeTrueRange(decimal high, decimal low, decimal prevClose)
    {
        decimal hl = high - low;
        decimal hc = Math.Abs(high - prevClose);
        decimal lc = Math.Abs(low - prevClose);
        return Math.Max(hl, Math.Max(hc, lc));
    }
}
