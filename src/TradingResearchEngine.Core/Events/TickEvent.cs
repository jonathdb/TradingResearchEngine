namespace TradingResearchEngine.Core.Events;

/// <summary>A market data event carrying full order-book depth, best bid/ask, and last-trade data.</summary>
public record TickEvent(
    string Symbol,
    IReadOnlyList<BidLevel> BidLevels,
    IReadOnlyList<AskLevel> AskLevels,
    LastTrade LastTrade,
    DateTimeOffset Timestamp,
    Quote? Bid = null,
    Quote? Ask = null)
    : MarketDataEvent(Symbol, Timestamp);
