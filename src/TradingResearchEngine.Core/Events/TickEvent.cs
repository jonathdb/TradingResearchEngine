namespace TradingResearchEngine.Core.Events;

/// <summary>A market data event carrying full order-book depth and last-trade data.</summary>
public record TickEvent(
    string Symbol,
    IReadOnlyList<BidLevel> BidLevels,
    IReadOnlyList<AskLevel> AskLevels,
    LastTrade LastTrade,
    DateTimeOffset Timestamp)
    : MarketDataEvent(Symbol, Timestamp);
