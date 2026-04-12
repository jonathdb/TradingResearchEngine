using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Donchian Channel Breakout trend follower.
/// 
/// Entry: close moves above the PRIOR day's upper Donchian band (highest high over trailing N days).
/// Exit: close falls below the PRIOR day's lower Donchian band (lowest low over trailing N days).
/// 
/// Long-only — breakdowns lead to flat, not short.
/// Uses lagged (prior day) channel values to avoid same-bar lookahead bias.
/// 
/// Based on the classic turtle trading system and QuantConnect Donchian implementation.
/// </summary>
[StrategyName("donchian-breakout")]
public sealed class DonchianBreakoutStrategy : IStrategy
{
    private readonly int _period;
    private readonly List<decimal> _highs = new();
    private readonly List<decimal> _lows = new();
    private decimal _priorUpperBand;
    private decimal _priorLowerBand;
    private Direction _position = Direction.Flat;
    private bool _warmedUp;

    /// <param name="period">Donchian channel lookback period (default 20).</param>
    public DonchianBreakoutStrategy(
        [ParameterMeta(DisplayName = "Period", Description = "Donchian channel lookback period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 0, Min = 5)]
        int period = 20)
    {
        _period = period;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _highs.Add(bar.High);
        _lows.Add(bar.Low);

        if (_highs.Count <= _period)
            return Array.Empty<EngineEvent>();

        // Compute current channel from the last N bars (excluding today)
        // This becomes the "prior day" channel for tomorrow's decision
        // But we USE the prior channel (computed yesterday) for today's decision
        decimal currentUpper = MaxOfRange(_highs, _highs.Count - _period - 1, _period);
        decimal currentLower = MinOfRange(_lows, _lows.Count - _period - 1, _period);

        if (!_warmedUp)
        {
            // First bar after warmup — store channel, no signal yet
            _priorUpperBand = currentUpper;
            _priorLowerBand = currentLower;
            _warmedUp = true;
            return Array.Empty<EngineEvent>();
        }

        var signals = new List<EngineEvent>();

        // Entry: close > prior day's upper band → go long
        if (bar.Close > _priorUpperBand && _position != Direction.Long)
        {
            _position = Direction.Long;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp));
        }
        // Exit: close < prior day's lower band → go flat (long-only, no short)
        else if (bar.Close < _priorLowerBand && _position == Direction.Long)
        {
            _position = Direction.Flat;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp));
        }

        // Update prior bands for next bar's decision
        _priorUpperBand = currentUpper;
        _priorLowerBand = currentLower;

        return signals;
    }

    private static decimal MaxOfRange(List<decimal> list, int start, int count)
    {
        start = Math.Max(0, start);
        decimal max = decimal.MinValue;
        for (int i = start; i < start + count && i < list.Count; i++)
            if (list[i] > max) max = list[i];
        return max;
    }

    private static decimal MinOfRange(List<decimal> list, int start, int count)
    {
        start = Math.Max(0, start);
        decimal min = decimal.MaxValue;
        for (int i = start; i < start + count && i < list.Count; i++)
            if (list[i] < min) min = list[i];
        return min;
    }
}
