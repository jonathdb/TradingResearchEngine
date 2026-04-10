namespace TradingResearchEngine.Application.MarketData;

/// <summary>
/// Result of writing a canonical CSV file from a market data provider.
/// Metadata is derived from the written output stream, not from request parameters.
/// </summary>
public sealed record CsvWriteResult(
    string FilePath,
    string Symbol,
    string Timeframe,
    DateTimeOffset FirstBar,
    DateTimeOffset LastBar,
    int BarCount);
