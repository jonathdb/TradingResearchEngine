using TradingResearchEngine.Application.Strategy;
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
/// 2. Trend regime: price vs long-term SMA
///    (proxy for yield curve slope — above SMA = expansion, below = contraction)
/// 3. Momentum regime: rate of change over medium term
///    (proxy for fed funds direction — positive momentum = accommodative)
/// 
/// Decision rules (simplified decision tree):
/// - Risk-On (100% long): low vol + above SMA + positive momentum
/// - Cautious (50% long): mixed signals (2 of 3 positive)
/// - Risk-Off (0% / flat): high vol + below SMA + negative momentum
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
    private readonly List<decimal> _closes = new();
    private int _barsSinceRebalance;
    private Direction _currentPosition = Direction.Flat;
    private decimal _currentAllocation; // 0.0 to 1.0

    /// <param name="volLookback">Realized volatility lookback (default 21 = ~1 month).</param>
    /// <param name="trendLookback">Trend SMA lookback (default 200 = ~10 months).</param>
    /// <param name="momentumLookback">Rate of change lookback (default 63 = ~3 months).</param>
    /// <param name="rebalanceDays">Bars between rebalances (default 21 = monthly).</param>
    public MacroRegimeRotationStrategy(
        int volLookback = 21,
        int trendLookback = 200,
        int momentumLookback = 63,
        int rebalanceDays = 21)
    {
        _volLookback = volLookback;
        _trendLookback = trendLookback;
        _momentumLookback = momentumLookback;
        _rebalanceDays = rebalanceDays;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _closes.Add(bar.Close);
        _barsSinceRebalance++;

        int minBars = Math.Max(_trendLookback, Math.Max(_volLookback, _momentumLookback)) + 1;
        if (_closes.Count < minBars) return Array.Empty<EngineEvent>();

        // Only rebalance on schedule
        if (_barsSinceRebalance < _rebalanceDays) return Array.Empty<EngineEvent>();
        _barsSinceRebalance = 0;

        // Compute regime indicators
        bool lowVol = IsLowVolatility();
        bool aboveTrend = IsAboveTrend();
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
            // Enter long with strength = close * allocation (RiskLayer uses strength for sizing)
            _currentPosition = Direction.Long;
            _currentAllocation = targetAllocation;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Long,
                bar.Close * targetAllocation, bar.Timestamp));
        }
        else if (targetAllocation > 0 && _currentPosition == Direction.Long
                 && Math.Abs(targetAllocation - _currentAllocation) > 0.1m)
        {
            // Rebalance: close and re-enter with new allocation
            signals.Add(new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp));
            _currentAllocation = targetAllocation;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Long,
                bar.Close * targetAllocation, bar.Timestamp));
        }
        else if (targetAllocation == 0 && _currentPosition == Direction.Long)
        {
            // Exit to flat
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

        // Compare to longer-term vol average
        var longerReturns = ComputeReturns(_closes, _volLookback * 2);
        decimal avgVol = StdDev(longerReturns);

        return currentVol < avgVol;
    }

    /// <summary>
    /// Trend regime: current price vs long-term SMA.
    /// Above SMA = uptrend (expansion).
    /// </summary>
    private bool IsAboveTrend()
    {
        decimal sma = 0;
        int start = _closes.Count - _trendLookback;
        for (int i = start; i < _closes.Count; i++)
            sma += _closes[i];
        sma /= _trendLookback;
        return _closes[^1] > sma;
    }

    /// <summary>
    /// Momentum regime: rate of change over medium term.
    /// Positive ROC = positive momentum.
    /// </summary>
    private bool IsPositiveMomentum()
    {
        decimal current = _closes[^1];
        decimal past = _closes[_closes.Count - 1 - _momentumLookback];
        return past > 0 && current > past;
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
