namespace TradingResearchEngine.Core.Portfolio;

/// <summary>
/// A timestamped portfolio snapshot appended on every bar (via mark-to-market)
/// and after each fill. Provides full portfolio state for equity curve analysis.
/// </summary>
public sealed record EquityCurvePoint(
    DateTimeOffset Timestamp,
    decimal TotalEquity,
    decimal CashBalance = 0m,
    decimal UnrealisedPnl = 0m,
    decimal RealisedPnl = 0m,
    int OpenPositionCount = 0);
