namespace TradingResearchEngine.Core.Portfolio;

/// <summary>
/// Tracks the running minimum and maximum of unrealised P&amp;L during a trade's lifetime.
/// Always active regardless of trace mode. Provides the data for
/// <see cref="ClosedTrade.MaxAdverseExcursion"/> and <see cref="ClosedTrade.MaxFavorableExcursion"/>.
/// </summary>
internal sealed class PnlWatermarkTracker
{
    /// <summary>The lowest unrealised P&amp;L observed (most adverse excursion).</summary>
    public decimal MinPnl { get; private set; }

    /// <summary>The highest unrealised P&amp;L observed (most favorable excursion).</summary>
    public decimal MaxPnl { get; private set; }

    /// <summary>
    /// Updates the watermarks with the current unrealised P&amp;L value.
    /// </summary>
    /// <param name="unrealisedPnl">The current unrealised P&amp;L for the position.</param>
    public void Update(decimal unrealisedPnl)
    {
        if (unrealisedPnl < MinPnl)
            MinPnl = unrealisedPnl;
        if (unrealisedPnl > MaxPnl)
            MaxPnl = unrealisedPnl;
    }
}
