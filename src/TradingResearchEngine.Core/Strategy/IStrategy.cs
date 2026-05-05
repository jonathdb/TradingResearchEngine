using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Strategy;

/// <summary>
/// Consumes market data events and produces zero or more <see cref="SignalEvent"/>
/// or <see cref="OrderEvent"/> instances per bar/tick.
/// </summary>
/// <remarks>
/// <para>
/// Strategies emit directional signals using the <see cref="Direction"/> enum:
/// <list type="bullet">
///   <item><c>Direction.Long</c> — enter a long position.</item>
///   <item><c>Direction.Short</c> — enter a short position.</item>
///   <item><c>Direction.Flat</c> — exit the current position (close long or short).</item>
/// </list>
/// </para>
/// <para>
/// The full position lifecycle supports entering long, entering short, and exiting
/// to flat. When <c>AllowReversals</c> is enabled on <c>ExecutionConfig</c>, the engine
/// permits direct long-to-short and short-to-long transitions without requiring an
/// intermediate flat signal.
/// </para>
/// <para>
/// Strategy implementations return <see cref="SignalEvent"/> for directional signals
/// or <see cref="OrderEvent"/> for explicit order instructions. Returning an empty list
/// indicates no action for the current bar/tick.
/// </para>
/// </remarks>
public interface IStrategy
{
    /// <summary>
    /// Called for every <see cref="MarketDataEvent"/> dequeued during the inner dispatch loop.
    /// Returns an empty list to produce no output.
    /// </summary>
    IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt);
}
