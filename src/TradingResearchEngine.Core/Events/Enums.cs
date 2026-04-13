namespace TradingResearchEngine.Core.Events;

/// <summary>
/// Trade direction. V5 adds <c>Short</c> to improve visibility of long-only assumptions;
/// runtime short-selling is guarded by <see cref="LongOnlyGuard"/> and throws
/// <see cref="NotSupportedException"/>. Short execution is a V6 task.
/// Adding the enum value forces handling in exhaustive switch expressions, but does not
/// guarantee coverage in if/else chains or default cases — explicit guard calls are required.
/// </summary>
public enum Direction { Long, Short, Flat }

/// <summary>Order execution type.</summary>
public enum OrderType { Market, Limit, StopMarket, StopLimit }
