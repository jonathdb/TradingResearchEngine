using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Portfolio;

/// <summary>An immutable record of a completed round-trip trade.</summary>
public sealed record ClosedTrade(
    string Symbol,
    DateTimeOffset EntryTime,
    DateTimeOffset ExitTime,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal Quantity,
    Direction Direction,
    decimal GrossPnl,
    decimal Commission,
    decimal NetPnl,
    /// <summary>
    /// Maximum Adverse Excursion: the worst (most negative) unrealised P&amp;L observed
    /// between entry and exit, expressed in absolute currency terms.
    /// Always tracked regardless of trace mode. Enables edge ratio, R-multiple distribution,
    /// and entry/exit quality scoring downstream.
    /// </summary>
    decimal MaxAdverseExcursion = 0m,
    /// <summary>
    /// Maximum Favorable Excursion: the best (most positive) unrealised P&amp;L observed
    /// between entry and exit, expressed in absolute currency terms.
    /// Always tracked regardless of trace mode. Enables edge ratio, R-multiple distribution,
    /// and entry/exit quality scoring downstream.
    /// </summary>
    decimal MaxFavorableExcursion = 0m,
    /// <summary>
    /// Intra-trade analytics (MAE, MFE, Duration). Null when trace data is unavailable
    /// (i.e., <c>TraceOptions.EnableEventTrace</c> is false).
    /// </summary>
    TradeAnatomy? Anatomy = null)
{
    /// <summary>
    /// Return on risk: <c>NetPnl / (EntryPrice * Quantity)</c>.
    /// Returns 0 when the denominator is zero or negative.
    /// </summary>
    public decimal ReturnOnRisk => EntryPrice * Quantity > 0m
        ? NetPnl / (EntryPrice * Quantity)
        : 0m;
}
