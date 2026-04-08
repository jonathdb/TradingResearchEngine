namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Performance breakdown by market regime.</summary>
public sealed record RegimePerformanceReport(
    IReadOnlyList<RegimeSegment> Segments);

/// <summary>Performance metrics for a single regime bucket.</summary>
public sealed record RegimeSegment(
    string RegimeName,
    string RegimeDimension,
    int TradeCount,
    decimal WinRate,
    decimal Expectancy,
    TimeSpan AverageHoldTime,
    decimal MaxDrawdownContribution);

/// <summary>Options for regime segmentation.</summary>
public sealed class RegimeSegmentationOptions
{
    /// <summary>Volatility lookback period for regime classification (default 20).</summary>
    public int VolatilityLookback { get; set; } = 20;

    /// <summary>Low volatility percentile threshold (default 0.33).</summary>
    public decimal LowVolThreshold { get; set; } = 0.33m;

    /// <summary>High volatility percentile threshold (default 0.67).</summary>
    public decimal HighVolThreshold { get; set; } = 0.67m;

    /// <summary>Trend lookback period for moving average slope (default 50).</summary>
    public int TrendLookback { get; set; } = 50;
}
