using Skender.Stock.Indicators;
using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Donchian Channel Breakout trend follower.
/// 
/// Entry: close moves above the PRIOR day's upper Donchian band (highest high over trailing N days).
/// Exit: close falls below the PRIOR day's lower Donchian band (lowest low over trailing N days).
/// 
/// Uses <see cref="DonchianIndicator"/> wrapper backed by Skender.Stock.Indicators
/// for channel computation.
///
/// V6: Supports bidirectional signals via DirectionMode parameter.
/// Uses lagged (prior day) channel values to avoid same-bar lookahead bias.
/// </summary>
[StrategyName("donchian-breakout")]
public sealed class DonchianBreakoutStrategy : IStrategy
{
    private readonly int _period;
    private readonly DirectionMode _directionMode;
    private readonly DonchianIndicator _donchian;
    private decimal _priorUpperBand;
    private decimal _priorLowerBand;
    private Direction _position = Direction.Flat;
    private bool _hasPriorBands;
    private int _barCount;

    /// <param name="period">Donchian channel lookback period (default 20).</param>
    /// <param name="directionMode">Signal direction mode: Long, Short, or Both (default Long).</param>
    public DonchianBreakoutStrategy(
        [ParameterMeta(DisplayName = "Period", Description = "Donchian channel lookback period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 0, Min = 5)]
        int period = 20,
        [ParameterMeta(DisplayName = "Direction", Description = "Signal direction mode.",
            Group = "Signal", DisplayOrder = 1)]
        DirectionMode directionMode = DirectionMode.Long)
    {
        _period = period;
        _directionMode = directionMode;
        _donchian = new DonchianIndicator(period);
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        var barRecord = new BarRecord(
            bar.Symbol, bar.Interval, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, bar.Timestamp);

        _donchian.Add(barRecord);
        _barCount++;

        if (!_donchian.IsWarm)
            return Array.Empty<EngineEvent>();

        var result = _donchian.Results[^1];

        if (result.UpperBand is null || result.LowerBand is null)
            return Array.Empty<EngineEvent>();

        decimal currentUpper = (decimal)result.UpperBand.Value;
        decimal currentLower = (decimal)result.LowerBand.Value;

        if (!_hasPriorBands)
        {
            _priorUpperBand = currentUpper;
            _priorLowerBand = currentLower;
            _hasPriorBands = true;
            return Array.Empty<EngineEvent>();
        }

        var signals = new List<EngineEvent>();

        // Upper breakout → Long (when mode is Long or Both)
        if (bar.Close > _priorUpperBand && _position != Direction.Long
            && _directionMode is DirectionMode.Long or DirectionMode.Both)
        {
            _position = Direction.Long;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp));
        }
        // Lower breakdown → Short (when mode is Short or Both)
        else if (bar.Close < _priorLowerBand && _position != Direction.Short
            && _directionMode is DirectionMode.Short or DirectionMode.Both)
        {
            _position = Direction.Short;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Short, bar.Close, bar.Timestamp));
        }
        // Exit long: close < lower band
        else if (bar.Close < _priorLowerBand && _position == Direction.Long)
        {
            _position = Direction.Flat;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp));
        }
        // Exit short: close > upper band
        else if (bar.Close > _priorUpperBand && _position == Direction.Short)
        {
            _position = Direction.Flat;
            signals.Add(new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp));
        }

        _priorUpperBand = currentUpper;
        _priorLowerBand = currentLower;

        return signals;
    }
}
