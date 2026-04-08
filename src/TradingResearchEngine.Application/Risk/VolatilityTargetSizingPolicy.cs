using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Application.Risk;

/// <summary>
/// Targets a volatility level using ATR. Quantity = (equity * targetVol) / (ATR * price).
/// Maintains a rolling ATR from recent bars.
/// </summary>
public sealed class VolatilityTargetSizingPolicy : IPositionSizingPolicy
{
    private readonly decimal _targetVol;
    private readonly int _atrPeriod;
    private readonly List<decimal> _trueRanges = new();
    private decimal _prevClose;
    private decimal _rollingAtr;

    /// <param name="targetVol">Target annualised volatility fraction (default 0.10 = 10%).</param>
    /// <param name="atrPeriod">ATR lookback period (default 14).</param>
    public VolatilityTargetSizingPolicy(decimal targetVol = 0.10m, int atrPeriod = 14)
    {
        _targetVol = targetVol;
        _atrPeriod = atrPeriod;
    }

    /// <inheritdoc/>
    public decimal ComputeSize(SignalEvent signal, PortfolioSnapshot snapshot, MarketDataEvent market)
    {
        if (market is not BarEvent bar) return 0m;

        // Compute true range
        decimal tr = bar.High - bar.Low;
        if (_prevClose > 0m)
        {
            tr = Math.Max(tr, Math.Abs(bar.High - _prevClose));
            tr = Math.Max(tr, Math.Abs(bar.Low - _prevClose));
        }
        _prevClose = bar.Close;

        _trueRanges.Add(tr);
        if (_trueRanges.Count <= _atrPeriod)
            _rollingAtr = _trueRanges.Sum() / _trueRanges.Count;
        else
            _rollingAtr = (_rollingAtr * (_atrPeriod - 1) + tr) / _atrPeriod;

        if (_rollingAtr <= 0m || bar.Close <= 0m) return 0m;

        // Daily vol target = annualised target / sqrt(252)
        decimal dailyTarget = _targetVol / (decimal)Math.Sqrt(252);
        decimal dollarRisk = snapshot.TotalEquity * dailyTarget;
        return Math.Floor(dollarRisk / (_rollingAtr * bar.Close / bar.Close)); // simplified: dollarRisk / ATR
    }
}
