using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Buy-and-hold benchmark strategy.
///
/// Emits a single Long signal after a configurable warmup period and never exits.
/// Provides a passive exposure baseline for comparing active strategy performance.
///
/// Hypothesis: Markets have a positive long-term drift; any active strategy must
/// outperform passive exposure to justify its complexity.
/// </summary>
[StrategyName("baseline-buy-and-hold")]
public sealed class BaselineBuyAndHoldStrategy : IStrategy
{
    private readonly int _warmupBars;
    private int _barCount;
    private bool _entered;

    /// <summary>Creates a buy-and-hold benchmark strategy.</summary>
    /// <param name="warmupBars">Number of bars before entering (default 1).</param>
    public BaselineBuyAndHoldStrategy(int warmupBars = 1)
    {
        _warmupBars = warmupBars;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _barCount++;

        if (!_entered && _barCount >= _warmupBars)
        {
            _entered = true;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, 1.0m, bar.Timestamp)
            };
        }

        return Array.Empty<EngineEvent>();
    }
}
