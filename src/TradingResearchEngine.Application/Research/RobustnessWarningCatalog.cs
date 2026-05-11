namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Provides plain-English explanations for robustness warning labels.
/// Used by the Dashboard to show tooltips on warning chips.
/// </summary>
public static class RobustnessWarningCatalog
{
    /// <summary>Mapping of warning labels to human-readable explanations.</summary>
    public static readonly IReadOnlyDictionary<string, string> Explanations = new Dictionary<string, string>
    {
        // Sharpe-related warnings
        ["High Sharpe"] = "An unusually high Sharpe ratio is extremely rare in live trading and often indicates overfitting to historical data or a data error.",
        ["Sharpe > 3"] = "A Sharpe ratio above 3 is extremely rare in live trading and often indicates overfitting to historical data or a data error.",
        ["Sharpe > 2.5"] = "A Sharpe ratio above 2.5 is unusually high and may indicate curve-fitting. Validate with walk-forward analysis.",

        // Trade count warnings
        ["Low Trades"] = "Too few trades provides insufficient statistical significance. Results may not be reliable.",
        ["Trades < 30"] = "Fewer than 30 trades provides insufficient statistical significance. Results may not be reliable.",
        ["Trades < 10"] = "Fewer than 10 trades makes any statistical inference meaningless. Increase the backtest period or reduce signal selectivity.",

        // Equity curve quality
        ["K-Ratio < 0"] = "A negative K-Ratio indicates the equity curve is deteriorating over time, suggesting the strategy's edge is decaying.",
        ["Flat equity"] = "The equity curve shows no meaningful growth, suggesting the strategy has no exploitable edge.",

        // Drawdown warnings
        ["Max DD > 30%"] = "A maximum drawdown exceeding 30% poses significant risk of account ruin and psychological pressure in live trading.",
        ["Max DD > 50%"] = "A maximum drawdown exceeding 50% means the strategy lost more than half its peak value. Recovery requires a 100%+ gain.",

        // Performance quality
        ["Win Rate < 30%"] = "A win rate below 30% requires very large average wins relative to losses. Verify the profit factor supports this.",
        ["Profit Factor < 1.2"] = "A profit factor below 1.2 leaves very little margin for execution costs and slippage in live trading.",
        ["Negative expectancy"] = "The strategy has negative expected value per trade. It is expected to lose money over time.",
        ["Recovery Factor < 2"] = "A recovery factor below 2 means the strategy takes a long time to recover from drawdowns relative to its returns.",

        // Validation gaps
        ["Short backtest"] = "The backtest period may be too short to capture multiple market regimes. Results may not generalize.",
        ["No walk-forward"] = "Without walk-forward validation, there is no evidence the strategy generalizes beyond the training period.",

        // Statistical concerns
        ["High parameter count"] = "Strategies with many parameters are more susceptible to overfitting. Each parameter adds a degree of freedom.",
        ["DSR < 1"] = "A Deflated Sharpe Ratio below 1 suggests the observed Sharpe may be due to multiple testing bias rather than genuine skill.",
    };

    /// <summary>
    /// Returns the explanation for a warning label, or the raw label as fallback.
    /// Never throws, never returns null.
    /// </summary>
    public static string GetExplanation(string warningLabel)
        => Explanations.GetValueOrDefault(warningLabel, warningLabel);
}
