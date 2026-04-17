using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Core.Risk;

/// <summary>An immutable point-in-time snapshot of portfolio state passed to the RiskLayer.</summary>
public sealed record PortfolioSnapshot(
    IReadOnlyDictionary<string, Position> Positions,
    decimal CashBalance,
    decimal TotalEquity,
    /// <summary>V6: Open short positions keyed by symbol.</summary>
    IReadOnlyDictionary<string, Position>? ShortPositions = null);
