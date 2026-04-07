namespace TradingResearchEngine.Core.DataHandling;

/// <summary>An immutable OHLCV bar record sourced from an <see cref="IDataProvider"/>.</summary>
public sealed record BarRecord(
    string Symbol,
    string Interval,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume,
    DateTimeOffset Timestamp);
