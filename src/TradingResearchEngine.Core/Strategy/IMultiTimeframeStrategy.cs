using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Core.Strategy;

/// <summary>
/// A strategy that consumes price data from multiple timeframes simultaneously.
/// Extends <see cref="IStrategy"/> with a callback for secondary timeframe bars,
/// enabling strategies that use higher-timeframe context for lower-timeframe decisions.
/// </summary>
/// <remarks>
/// <para>
/// The engine delivers secondary timeframe bars via <see cref="OnSecondaryBar"/> in
/// chronological order, interleaved with primary timeframe bars. A secondary bar is
/// delivered before any primary bar whose timestamp is equal to or later than the
/// secondary bar's timestamp.
/// </para>
/// <para>
/// Strategies implementing this interface should maintain internal state per timeframe
/// (e.g. indicator values computed on higher-timeframe bars) and use that state to
/// inform decisions made in <see cref="IStrategy.OnMarketData"/>.
/// </para>
/// </remarks>
public interface IMultiTimeframeStrategy : IStrategy
{
    /// <summary>
    /// Called when a bar from a secondary timeframe is available.
    /// The strategy should update its internal higher-timeframe state but should not
    /// produce trading signals from this callback — signals are produced only from
    /// <see cref="IStrategy.OnMarketData"/> on the primary timeframe.
    /// </summary>
    /// <param name="timeframe">The timeframe label identifying which secondary source produced this bar.</param>
    /// <param name="bar">The bar record from the secondary timeframe.</param>
    void OnSecondaryBar(string timeframe, BarRecord bar);
}
