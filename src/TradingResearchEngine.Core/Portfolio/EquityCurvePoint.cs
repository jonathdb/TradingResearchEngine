namespace TradingResearchEngine.Core.Portfolio;

/// <summary>A timestamped total-equity snapshot appended after each fill.</summary>
public sealed record EquityCurvePoint(DateTimeOffset Timestamp, decimal TotalEquity);
