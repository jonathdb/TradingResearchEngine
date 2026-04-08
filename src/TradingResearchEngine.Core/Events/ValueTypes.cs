namespace TradingResearchEngine.Core.Events;

/// <summary>A single bid price/size level in an order book.</summary>
public readonly record struct BidLevel(decimal Price, decimal Size);

/// <summary>A single ask price/size level in an order book.</summary>
public readonly record struct AskLevel(decimal Price, decimal Size);

/// <summary>The most recent trade: price, volume, and timestamp.</summary>
public readonly record struct LastTrade(decimal Price, decimal Volume, DateTimeOffset Timestamp);

/// <summary>A single price/size quote level for best bid or best ask.</summary>
public record Quote(decimal Price, decimal Size);
