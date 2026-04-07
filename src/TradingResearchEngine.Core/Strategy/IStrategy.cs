using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Strategy;

/// <summary>
/// Consumes market data events and produces zero or more <see cref="SignalEvent"/>
/// or <see cref="OrderEvent"/> instances per bar/tick.
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// Called for every <see cref="MarketDataEvent"/> dequeued during the inner dispatch loop.
    /// Returns an empty list to produce no output.
    /// </summary>
    IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt);
}
