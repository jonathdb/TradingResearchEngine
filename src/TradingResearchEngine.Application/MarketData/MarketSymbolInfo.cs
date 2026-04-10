namespace TradingResearchEngine.Application.MarketData;

/// <summary>
/// Describes a symbol supported by a market data provider,
/// including its human-readable name and available timeframes.
/// </summary>
public sealed record MarketSymbolInfo(
    string Symbol,
    string DisplayName,
    string[] SupportedTimeframes);
