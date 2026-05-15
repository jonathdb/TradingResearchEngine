namespace TradingResearchEngine.Core.Events;

/// <summary>
/// Trade direction. All three values are fully supported in V6+.
/// <c>Long</c> opens or adds to a long position, <c>Short</c> opens or adds to a short position,
/// and <c>Flat</c> closes the current position in the given symbol.
/// Consumers should use exhaustive switch expressions to handle all cases.
/// </summary>
public enum Direction { Long, Short, Flat }

/// <summary>Order execution type.</summary>
public enum OrderType { Market, Limit, StopMarket, StopLimit }
