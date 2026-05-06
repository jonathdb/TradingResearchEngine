namespace TradingResearchEngine.Application.TickImport;

/// <summary>A single row in the per-day tick CSV cache.</summary>
public readonly record struct TickCsvRow(
    DateTimeOffset Timestamp,
    decimal Bid,
    decimal Ask,
    decimal BidVolume,
    decimal AskVolume);
