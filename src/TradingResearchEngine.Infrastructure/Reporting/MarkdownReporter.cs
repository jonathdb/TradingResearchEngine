using System.Text;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Reporting;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Infrastructure.Reporting;

/// <summary>Renders BacktestResult and ComparisonReport as Markdown.</summary>
public sealed class MarkdownReporter : IReporter
{
    private readonly int _dp;

    /// <inheritdoc cref="MarkdownReporter"/>
    public MarkdownReporter(IOptions<ReportingOptions> options) => _dp = options.Value.DecimalPlaces;

    /// <inheritdoc/>
    public void RenderToConsole(BacktestResult result) { } // Delegated to ConsoleReporter

    /// <inheritdoc/>
    public string RenderToMarkdown(BacktestResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Backtest Report: {result.ScenarioConfig.ScenarioId}");
        sb.AppendLine();
        sb.AppendLine($"**Description:** {result.ScenarioConfig.Description}");
        sb.AppendLine($"**Status:** {result.Status}");
        sb.AppendLine($"**Duration:** {result.RunDurationMs}ms");
        sb.AppendLine();
        sb.AppendLine("## Performance Metrics");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Start Equity | ${result.StartEquity.ToString($"F{_dp}")} |");
        sb.AppendLine($"| End Equity | ${result.EndEquity.ToString($"F{_dp}")} |");
        sb.AppendLine($"| Max Drawdown | {result.MaxDrawdown.ToString($"P{_dp}")} |");
        sb.AppendLine($"| Sharpe Ratio | {result.SharpeRatio?.ToString($"F{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Sortino Ratio | {result.SortinoRatio?.ToString($"F{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Calmar Ratio | {result.CalmarRatio?.ToString($"F{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Return on Max DD | {result.ReturnOnMaxDrawdown?.ToString($"F{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Total Trades | {result.TotalTrades} |");
        sb.AppendLine($"| Win Rate | {result.WinRate?.ToString($"P{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Profit Factor | {result.ProfitFactor?.ToString($"F{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Expectancy | ${result.Expectancy?.ToString($"F{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Avg Win | ${result.AverageWin?.ToString($"F{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Avg Loss | ${result.AverageLoss?.ToString($"F{_dp}") ?? "N/A"} |");
        sb.AppendLine($"| Avg Holding Period | {result.AverageHoldingPeriod?.ToString() ?? "N/A"} |");
        sb.AppendLine($"| K-Ratio | {result.EquityCurveSmoothness?.ToString("F4") ?? "N/A"} |");
        AppendMetricRow(sb, "VaR (95%)", result.VaR95, _dp);
        AppendMetricRow(sb, "CVaR (95%)", result.CVaR95, _dp);
        AppendMetricRow(sb, "Omega Ratio", result.OmegaRatio, _dp);
        AppendMetricRow(sb, "Ulcer Index", result.UlcerIndex, _dp);
        sb.AppendLine($"| Max Consec Losses | {result.MaxConsecutiveLosses} |");
        sb.AppendLine($"| Max Consec Wins | {result.MaxConsecutiveWins} |");
        sb.AppendLine();
        sb.AppendLine($"## Equity Curve Summary");
        sb.AppendLine();
        sb.AppendLine($"Points: {result.EquityCurve.Count}");
        return sb.ToString();
    }

    /// <summary>
    /// Appends a metric row to the Markdown table, rendering "N/A" when the value is null.
    /// </summary>
    /// <param name="sb">The string builder to append to.</param>
    /// <param name="label">The metric display name.</param>
    /// <param name="value">The nullable metric value.</param>
    /// <param name="decimalPlaces">Number of decimal places for formatting.</param>
    private static void AppendMetricRow(StringBuilder sb, string label, decimal? value, int decimalPlaces)
    {
        var formatted = value?.ToString($"F{decimalPlaces}") ?? "N/A";
        sb.AppendLine($"| {label} | {formatted} |");
    }

    /// <inheritdoc/>
    public void RenderToConsole(ComparisonReport report) { }

    /// <inheritdoc/>
    public string RenderToMarkdown(ComparisonReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Scenario Comparison");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Sharpe | Sortino | Max DD | Win Rate | PF | Trades | End Equity |");
        sb.AppendLine("|----------|--------|---------|--------|----------|----|--------|------------|");
        foreach (var r in report.Rows)
        {
            sb.AppendLine($"| {r.ScenarioId} | {r.SharpeRatio?.ToString($"F{_dp}") ?? "N/A"} | {r.SortinoRatio?.ToString($"F{_dp}") ?? "N/A"} | {r.MaxDrawdown.ToString($"P{_dp}")} | {r.WinRate?.ToString($"P{_dp}") ?? "N/A"} | {r.ProfitFactor?.ToString($"F{_dp}") ?? "N/A"} | {r.TotalTrades} | ${r.EndEquity.ToString($"F{_dp}")} |");
        }
        sb.AppendLine();
        sb.AppendLine($"**Best by Sharpe:** {report.BestBySharpe}");
        sb.AppendLine($"**Best by Drawdown:** {report.BestByDrawdown}");
        return sb.ToString();
    }
}
