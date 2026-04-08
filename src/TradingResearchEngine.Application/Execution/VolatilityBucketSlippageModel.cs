using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>
/// Maps recent realised volatility into configurable slippage bands.
/// Low vol → low slippage, medium vol → medium slippage, high vol → high slippage.
/// Deterministic given the same inputs.
/// </summary>
public sealed class VolatilityBucketSlippageModel : ISlippageModel
{
    private readonly int _lookback;
    private readonly decimal _lowThreshold;
    private readonly decimal _highThreshold;
    private readonly decimal _lowSlippage;
    private readonly decimal _mediumSlippage;
    private readonly decimal _highSlippage;
    private readonly List<decimal> _returns = new();
    private decimal _prevClose;

    /// <param name="lookback">Volatility lookback period (default 20).</param>
    /// <param name="lowThreshold">Annualised vol below this = low bucket (default 0.10 = 10%).</param>
    /// <param name="highThreshold">Annualised vol above this = high bucket (default 0.30 = 30%).</param>
    /// <param name="lowSlippage">Slippage for low vol (default 0.01).</param>
    /// <param name="mediumSlippage">Slippage for medium vol (default 0.05).</param>
    /// <param name="highSlippage">Slippage for high vol (default 0.15).</param>
    public VolatilityBucketSlippageModel(
        int lookback = 20,
        decimal lowThreshold = 0.10m,
        decimal highThreshold = 0.30m,
        decimal lowSlippage = 0.01m,
        decimal mediumSlippage = 0.05m,
        decimal highSlippage = 0.15m)
    {
        _lookback = lookback;
        _lowThreshold = lowThreshold;
        _highThreshold = highThreshold;
        _lowSlippage = lowSlippage;
        _mediumSlippage = mediumSlippage;
        _highSlippage = highSlippage;
    }

    /// <inheritdoc/>
    public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market)
    {
        if (market is not BarEvent bar) return _mediumSlippage;

        if (_prevClose > 0m)
            _returns.Add((bar.Close - _prevClose) / _prevClose);
        _prevClose = bar.Close;

        if (_returns.Count < _lookback) return _mediumSlippage;

        // Compute realised volatility (annualised) from recent returns
        var window = _returns.Skip(_returns.Count - _lookback).ToList();
        decimal mean = window.Average();
        decimal variance = window.Sum(r => (r - mean) * (r - mean)) / (_lookback - 1);
        decimal dailyVol = (decimal)Math.Sqrt((double)variance);
        decimal annualisedVol = dailyVol * (decimal)Math.Sqrt(252);

        if (annualisedVol < _lowThreshold) return _lowSlippage;
        if (annualisedVol > _highThreshold) return _highSlippage;
        return _mediumSlippage;
    }
}
