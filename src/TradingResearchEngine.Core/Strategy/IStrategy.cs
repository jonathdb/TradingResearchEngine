using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Strategy;

/// <summary>
/// Consumes market data events and produces zero or more <see cref="SignalEvent"/>
/// or <see cref="OrderEvent"/> instances per bar/tick.
/// <para>
/// V2 scope: all strategies are long-only. Strategies emit <c>Direction.Long</c> to enter
/// and <c>Direction.Flat</c> to exit. Short-selling is out of scope.
/// </para>
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// Called for every <see cref="MarketDataEvent"/> dequeued during the inner dispatch loop.
    /// Returns an empty list to produce no output.
    /// </summary>
    IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt);
}
