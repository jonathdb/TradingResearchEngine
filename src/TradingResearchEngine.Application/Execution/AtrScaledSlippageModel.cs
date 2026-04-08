using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>
/// Scales slippage as a configurable fraction of the Average True Range (ATR).
/// Maintains a rolling ATR from recent bars. Deterministic given the same inputs.
/// </summary>
public sealed class AtrScaledSlippageModel : ISlippageModel
{
    private readonly int _atrPeriod;
    private readonly decimal _atrFraction;
    private readonly List<decimal> _trueRanges = new();
    private decimal _prevClose;
    private decimal _rollingAtr;

    /// <param name="atrPeriod">ATR lookback period (default 14).</param>
    /// <param name="atrFraction">Fraction of ATR to use as slippage (default 0.1 = 10% of ATR).</param>
    public AtrScaledSlippageModel(int atrPeriod = 14, decimal atrFraction = 0.1m)
    {
        _atrPeriod = atrPeriod;
        _atrFraction = atrFraction;
    }

    /// <inheritdoc/>
    public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market)
    {
        if (market is not BarEvent bar) return 0m;

        // True Range = max(High-Low, |High-PrevClose|, |Low-PrevClose|)
        decimal tr = bar.High - bar.Low;
        if (_prevClose > 0m)
        {
            tr = Math.Max(tr, Math.Abs(bar.High - _prevClose));
            tr = Math.Max(tr, Math.Abs(bar.Low - _prevClose));
        }
        _prevClose = bar.Close;

        _trueRanges.Add(tr);
        if (_trueRanges.Count <= _atrPeriod)
        {
            _rollingAtr = _trueRanges.Sum() / _trueRanges.Count;
        }
        else
        {
            // Wilder smoothing: ATR = (prevATR * (period-1) + TR) / period
            _rollingAtr = (_rollingAtr * (_atrPeriod - 1) + tr) / _atrPeriod;
        }

        return _rollingAtr * _atrFraction;
    }
}
