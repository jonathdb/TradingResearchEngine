namespace TradingResearchEngine.Core.Events;

/// <summary>
/// Trade direction. V2 is long-only; short-selling is out of scope.
/// <c>Long</c> opens or adds to a long position.
/// <c>Flat</c> closes an existing long position.
/// </summary>
public enum Direction { Long, Flat }

/// <summary>Order execution type.</summary>
public enum OrderType { Market, Limit, StopMarket, StopLimit }
