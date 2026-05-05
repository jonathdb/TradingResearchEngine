namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Configurable thresholds for robustness advisory warnings.
/// Bound via <c>IOptions&lt;RobustnessThresholds&gt;</c> from the <c>"RobustnessThresholds"</c>
/// configuration section.
/// </summary>
public sealed class RobustnessThresholds
{
    /// <summary>
    /// Maximum acceptable Sharpe ratio before a warning is raised.
    /// Values above this threshold suggest potential overfitting or data issues.
    /// </summary>
    public decimal MaxSharpeRatio { get; set; } = 3.0m;

    /// <summary>
    /// Minimum number of trades required before a warning is raised.
    /// Results with fewer trades may lack statistical significance.
    /// </summary>
    public int MinTotalTrades { get; set; } = 30;

    /// <summary>
    /// Minimum K-Ratio (equity curve smoothness) before a warning is raised.
    /// A negative K-Ratio indicates a deteriorating equity curve.
    /// </summary>
    public decimal MinKRatio { get; set; } = 0m;

    /// <summary>
    /// Maximum acceptable drawdown as a decimal fraction (e.g. 0.20 = 20%)
    /// before a warning is raised.
    /// </summary>
    public decimal MaxDrawdownPercent { get; set; } = 0.20m;
}
