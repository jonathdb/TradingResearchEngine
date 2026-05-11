using Skender.Stock.Indicators;
using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Macro Regime Rotation strategy.
/// 
/// Adapted from a multi-asset ML-driven rotation model. Uses price-derived regime
/// indicators instead of external macro data:
/// 
/// 1. Volatility regime: realized volatility vs its own moving average
///    (proxy for VIX — high vol = risk-off, low vol = risk-on)
/// 2. Trend regime: price vs long-term EMA
///    (proxy for yield curve slope — above EMA = expansion, below = contraction)
/// 3. Momentum regime: RSI-based momentum assessment
///    (proxy for fed funds direction — RSI above 50 = accommodative)
/// 
/// Uses <see cref="EmaIndicator"/> and <see cref="RsiIndicator"/> wrappers backed by
/// Skender.Stock.Indicators for indicator computation.
///
/// Decision rules (simplified decision tree):
/// - Risk-On (100% long): low vol + above EMA + positive momentum
/// - Cautious (50% long): mixed signals (2 of 3 positive)
/// - Risk-Off (0% / flat): high vol + below EMA + negative momentum
/// 
/// Rebalances monthly (every rebalanceDays bars). Allocation is expressed via
/// signal strength which the RiskLayer converts to position size.
/// </summary>
[StrategyName("macro-regime-rotation")]
public sealed class MacroRegimeRotationStrategy : IStrategy
{
    private readonly int _volLookback;
    private readonly int _trendLookback;
    private readonly int _momentumLookback;
    private readonly int _rebalanceDays;
    private readonly EmaIndicator _trendEma;
    private readonly RsiIndicator _rsi;
    private readonly List<decimal> _closes = new();
    private int _barsSinceRebalance;
    private Direction _currentPosition = Direction.Flat;
    private decimal _currentAllocation; // 0.0 to 1.0

    /// <param name="volLookback">Realized volatility lookback (default 21 = ~1 month).</param>
    /// <param name="trendLookback">Trend EMA lookback (default 200 = ~10 months).</param>
    /// <param name="momentumLookback">RSI lookback period (default 63 = ~3 months).</param>
    /// <param name="rebalanceDays">Bars between rebalances (default 21 = monthly).</param>
    public MacroRegimeRotationStrategy(
        [ParameterMeta(DisplayName = "Volatility Lookback", Description = "Realized volatility lookback (~1 month).",
            SensitivityHint = SensitivityHint.Medium, Group = "Signal", DisplayOrder = 0, Min = 5)]
        int volLookback = 21,
        [ParameterMeta(DisplayName = "Trend Lookback", Description = "Trend EMA lookback (~10 months).",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 1, Min = 20)]
        int trendLookback = 200,
        [ParameterMeta(DisplayName = "Momentum Lookback", Description = "RSI lookback period (~3 months).",
            SensitivityHint = SensitivityHint.Medium, Group = "Signal", DisplayOrder = 2, Min = 5)]
        int momentumLookback = 63,
        [ParameterMeta(DisplayName = "Rebalance Days", Description = "Bars between rebalances (monthly).",
            SensitivityHint = SensitivityHint.Low, Group = "Execution", DisplayOrder = 3, Min = 1)]
        int rebalanceDays = 21)
    {
        _volLookback = volLookback;
        _trendLookback = trendLookback;
        _momentumLookback = momentumLookback;
        _rebalanceDays = rebalanceDays;
        _trendEma = new EmaIndicator(trendLookback);
        _rsi = new RsiIndicator(momentumLookback);
    }

    /// <inheritdoc/>
    public void Initialize(StrategyConfig config)
    {
        // Parameters are set via constructor; Initialize is a lifecycle hook.
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _trendEma.Reset();
        _rsi.Reset();
        _closes.Clear();
        _barsSinceRebalance = 0;
        _currentPosition = Direction.Flat;
        _currentAllocation = 0m;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        var barRecord = new BarRecord(
            bar.Symbol, bar.Interval, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, bar.Timestamp);

        _trendEma.Add(barRecord);
        _rsi.Add(barRecord);
        _closes.Add(bar.Close);
        _barsSinceRebalance++;

        int minBars = Math.Max(_trendLookback, Math.Max(_volLookback, _momentumLookback)) + 1;
        if (_closes.Count < minBars) return Array.Empty<EngineEvent>();

        // Only rebalance on schedule
        if (_barsSinceRebalance < _rebalanceDays) return Array.Empty<EngineEvent>();
        _barsSinceRebalance = 0;

        // Compute regime indicators
        bool lowVol = IsLowVolatility();
        bool aboveTrend = IsAboveTrend(bar.Close);
        bool positiveMomentum = IsPositiveMomentum();

        int bullSignals = (lowVol ? 1 : 0) + (aboveTrend ? 1 : 0) + (positiveMomentum ? 1 : 0);

        // Decision tree
        decimal targetAllocation;
        if (bullSignals >= 3)
            targetAllocation = 1.0m;      // Risk-On: full allocation
        else if (bullSignals == 2)
            targetAllocation = 0.5m;      // Cautious: half allocation
        else if (bullSignals == 1)
            targetAllocation = 0.25m;     // Defensive: quarter allocation
        else
            targetAllocation = 0.0m;      // Risk-Off: flat

        return ApplyAllocation(bar, targetAllocation);
    }

    private List<EngineEvent> ApplyAllocation(BarEvent bar, decimal targetAllocation)
    {
        var signals = new List<EngineEvent>();

        if (targetAllocation > 0 && _currentPosition != Direction.Long)
        {
            _currentPosition = Direction.Long;
            _currentAllocation = targetAllocation;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Long,
                bar.Close * targetAllocation, bar.Timestamp));
        }
        else if (targetAllocation > 0 && _currentPosition == Direction.Long
                 && Math.Abs(targetAllocation - _currentAllocation) > 0.1m)
        {
            signals.Add(new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp));
            _currentAllocation = targetAllocation;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Long,
                bar.Close * targetAllocation, bar.Timestamp));
        }
        else if (targetAllocation == 0 && _currentPosition == Direction.Long)
        {
            _currentPosition = Direction.Flat;
            _currentAllocation = 0;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp));
        }

        return signals;
    }

    /// <summary>
    /// Volatility regime: realized vol vs its 2x lookback moving average.
    /// Low vol = current vol below average (calm market, risk-on).
    /// </summary>
    private bool IsLowVolatility()
    {
        var recentReturns = ComputeReturns(_closes, _volLookback);
        decimal currentVol = StdDev(recentReturns);

        var longerReturns = ComputeReturns(_closes, _volLookback * 2);
        decimal avgVol = StdDev(longerReturns);

        return currentVol < avgVol;
    }

    /// <summary>
    /// Trend regime: current price vs EMA indicator.
    /// Above EMA = uptrend (expansion).
    /// </summary>
    private bool IsAboveTrend(decimal currentClose)
    {
        if (!_trendEma.IsWarm) return false;

        var emaResult = _trendEma.Results[^1];
        if (emaResult.Ema is null) return false;

        return currentClose > (decimal)emaResult.Ema.Value;
    }

    /// <summary>
    /// Momentum regime: RSI above 50 indicates positive momentum.
    /// </summary>
    private bool IsPositiveMomentum()
    {
        if (!_rsi.IsWarm) return false;

        var rsiResult = _rsi.Results[^1];
        if (rsiResult.Rsi is null) return false;

        return rsiResult.Rsi.Value > 50.0;
    }

    private static List<decimal> ComputeReturns(List<decimal> closes, int lookback)
    {
        var returns = new List<decimal>(lookback);
        int start = closes.Count - lookback;
        for (int i = start; i < closes.Count; i++)
        {
            if (closes[i - 1] != 0)
                returns.Add((closes[i] - closes[i - 1]) / closes[i - 1]);
        }
        return returns;
    }

    private static decimal StdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0m;
        decimal mean = values.Average();
        decimal variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return (decimal)Math.Sqrt((double)variance);
    }
}
