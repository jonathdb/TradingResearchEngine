using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Indicators;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Z-score mean reversion strategy using <see cref="RollingZScore"/> from the indicator library.
///
/// Computes a rolling z-score of close price: z = (Close - SMA) / StdDev.
/// Buys when z drops below -entryThreshold (price is abnormally low),
/// sells when z rises above exitThreshold (reversion complete).
///
/// Hypothesis: Short-term price dislocations around a rolling equilibrium
/// tend to mean-revert in non-trending regimes.
/// </summary>
[StrategyName("zscore-mean-reversion")]
public sealed class ZScoreMeanReversionStrategy : IStrategy
{
    private readonly decimal _entryThreshold;
    private readonly decimal _exitThreshold;
    private readonly RollingZScore _zScore;
    private Direction _position = Direction.Flat;

    /// <summary>Creates a z-score mean reversion strategy.</summary>
    /// <param name="lookback">Rolling window for SMA and StdDev (default 30).</param>
    /// <param name="entryThreshold">Z-score entry threshold (default 2.0 → buy when z &lt; -2).</param>
    /// <param name="exitThreshold">Z-score exit threshold (default 0.0 → sell when z &gt; 0, i.e. at the mean).</param>
    public ZScoreMeanReversionStrategy(
        [ParameterMeta(DisplayName = "Lookback", Description = "Rolling window for SMA and StdDev.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 0, Min = 5)]
        int lookback = 30,
        [ParameterMeta(DisplayName = "Entry Threshold", Description = "Z-score entry threshold (buy when z < -threshold).",
            SensitivityHint = SensitivityHint.High, Group = "Entry", DisplayOrder = 1, Min = 0.5)]
        decimal entryThreshold = 2.0m,
        [ParameterMeta(DisplayName = "Exit Threshold", Description = "Z-score exit threshold (sell when z > threshold).",
            SensitivityHint = SensitivityHint.Medium, Group = "Exit", DisplayOrder = 2)]
        decimal exitThreshold = 0.0m)
    {
        _entryThreshold = entryThreshold;
        _exitThreshold = exitThreshold;
        _zScore = new RollingZScore(lookback);
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _zScore.Update(bar.Close);

        if (!_zScore.IsReady) return Array.Empty<EngineEvent>();

        decimal zScoreValue = _zScore.Value!.Value;

        // Entry: z < -entryThreshold → buy (price abnormally low, expect reversion up)
        if (zScoreValue < -_entryThreshold && _position != Direction.Long)
        {
            _position = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }

        // V6: Short entry: z > +entryThreshold → short (price abnormally high, expect reversion down)
        if (zScoreValue > _entryThreshold && _position != Direction.Short)
        {
            _position = Direction.Short;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Short, bar.Close, bar.Timestamp)
            };
        }

        // Exit long: z > exitThreshold → sell (reversion complete)
        if (zScoreValue > _exitThreshold && _position == Direction.Long)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        // Exit short: z < -exitThreshold → cover (reversion complete)
        if (zScoreValue < -_exitThreshold && _position == Direction.Short)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        return Array.Empty<EngineEvent>();
    }
}
