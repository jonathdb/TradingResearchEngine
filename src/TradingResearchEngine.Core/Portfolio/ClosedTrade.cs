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
    decimal NetPnl)
{
    /// <summary>
    /// Return on risk: <c>NetPnl / (EntryPrice * Quantity)</c>.
    /// Returns 0 when the denominator is zero or negative.
    /// </summary>
    public decimal ReturnOnRisk => EntryPrice * Quantity > 0m
        ? NetPnl / (EntryPrice * Quantity)
        : 0m;
}
