namespace TradingResearchEngine.Core.Events;

/// <summary>A market data event carrying OHLCV bar data for a single instrument and interval.</summary>
public record BarEvent(
    string Symbol,
    string Interval,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    DateTimeOffset Timestamp)
    : MarketDataEvent(Symbol, Timestamp);
