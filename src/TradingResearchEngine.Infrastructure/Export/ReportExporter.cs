using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Infrastructure.Export;

/// <summary>
/// Implements <see cref="IReportExporter"/> with Markdown, CSV, and JSON export.
/// All files are written to a configurable export directory.
/// </summary>
public sealed class ReportExporter : IReportExporter
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _exportDir;

    public ReportExporter(string exportDir)
    {
        _exportDir = exportDir;
        Directory.CreateDirectory(_exportDir);
    }

    /// <inheritdoc/>
    public Task<string> ExportMarkdownAsync(BacktestResult result, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var cfg = result.ScenarioConfig;

        sb.AppendLine($"# Run Report: {cfg.StrategyType} — {cfg.ScenarioId}");
        sb.AppendLine($"**Date:** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}  **Status:** {result.Status}  **Duration:** {result.RunDurationMs}ms");
        if (result.DeflatedSharpeRatio is not null)
            sb.AppendLine($"**DSR:** {result.DeflatedSharpeRatio:F4}  **Trial #:** {result.TrialCount}");
        sb.AppendLine();

        // Configuration table
        sb.AppendLine("## Configuration");
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|-------|-------|");
        sb.AppendLine($"| Strategy | {cfg.StrategyType} |");
        sb.AppendLine($"| Initial Cash | ${cfg.InitialCash:N0} |");
        sb.AppendLine($"| Realism | {cfg.RealismProfile} |");
        sb.AppendLine($"| BarsPerYear | {cfg.BarsPerYear} |");
        sb.AppendLine();

        // Key metrics table
        sb.AppendLine("## Key Metrics");
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.AppendLine($"| Sharpe Ratio | {Fmt(result.SharpeRatio)} |");
        sb.AppendLine($"| Deflated Sharpe | {Fmt(result.DeflatedSharpeRatio)} |");
        sb.AppendLine($"| Max Drawdown | {result.MaxDrawdown:P1} |");
        sb.AppendLine($"| Win Rate | {Fmt(result.WinRate, "P1")} |");
        sb.AppendLine($"| Profit Factor | {Fmt(result.ProfitFactor)} |");
        sb.AppendLine($"| Expectancy | {Fmt(result.Expectancy, "F2")} |");
        sb.AppendLine($"| Recovery Factor | {Fmt(result.RecoveryFactor)} |");
        sb.AppendLine($"| Total Trades | {result.TotalTrades} |");
        sb.AppendLine($"| Max Consec Losses | {result.MaxConsecutiveLosses} |");
        sb.AppendLine();

        // Failure detail
        if (result.FailureDetail is not null)
        {
            sb.AppendLine("## Failure Detail");
            sb.AppendLine($"```\n{result.FailureDetail}\n```");
            sb.AppendLine();
        }

        // Trade log
        if (result.Trades.Count > 0)
        {
            sb.AppendLine("## Trade Log");
            sb.AppendLine("| # | Entry | Exit | Dir | Qty | Entry$ | Exit$ | Net P&L |");
            sb.AppendLine("|---|-------|------|-----|-----|--------|-------|---------|");
            int i = 1;
            foreach (var t in result.Trades)
            {
                sb.AppendLine($"| {i++} | {t.EntryTime:yyyy-MM-dd} | {t.ExitTime:yyyy-MM-dd} | {t.Direction} | {t.Quantity} | {t.EntryPrice:F4} | {t.ExitPrice:F4} | {t.NetPnl:F2} |");
            }
        }

        var fileName = $"{cfg.StrategyType}_{result.RunId:N}.md";
        var path = Path.Combine(_exportDir, fileName);
        File.WriteAllText(path, sb.ToString());
        return Task.FromResult(path);
    }

    /// <inheritdoc/>
    public Task<string> ExportTradeCsvAsync(BacktestResult result, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("EntryDate,ExitDate,Direction,Symbol,Quantity,EntryPrice,ExitPrice,GrossPnL,NetPnL,Commission");
        foreach (var t in result.Trades)
        {
            sb.AppendLine(string.Join(",",
                t.EntryTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.ExitTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                t.Direction,
                t.Symbol,
                t.Quantity.ToString(CultureInfo.InvariantCulture),
                t.EntryPrice.ToString(CultureInfo.InvariantCulture),
                t.ExitPrice.ToString(CultureInfo.InvariantCulture),
                t.GrossPnl.ToString(CultureInfo.InvariantCulture),
                t.NetPnl.ToString(CultureInfo.InvariantCulture),
                t.Commission.ToString(CultureInfo.InvariantCulture)));
        }

        var fileName = $"{result.ScenarioConfig.StrategyType}_{result.RunId:N}_trades.csv";
        var path = Path.Combine(_exportDir, fileName);
        File.WriteAllText(path, sb.ToString());
        return Task.FromResult(path);
    }

    /// <inheritdoc/>
    public Task<string> ExportEquityCsvAsync(BacktestResult result, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,TotalEquity,CashBalance,UnrealisedPnl,RealisedPnl,OpenPositionCount");
        foreach (var pt in result.EquityCurve)
        {
            sb.AppendLine(string.Join(",",
                pt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                pt.TotalEquity.ToString(CultureInfo.InvariantCulture),
                pt.CashBalance.ToString(CultureInfo.InvariantCulture),
                pt.UnrealisedPnl.ToString(CultureInfo.InvariantCulture),
                pt.RealisedPnl.ToString(CultureInfo.InvariantCulture),
                pt.OpenPositionCount.ToString(CultureInfo.InvariantCulture)));
        }

        var fileName = $"{result.ScenarioConfig.StrategyType}_{result.RunId:N}_equity.csv";
        var path = Path.Combine(_exportDir, fileName);
        File.WriteAllText(path, sb.ToString());
        return Task.FromResult(path);
    }

    /// <inheritdoc/>
    public Task<string> ExportJsonAsync(BacktestResult result, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(result, JsonOpts);
        var fileName = $"{result.RunId:N}.json";
        var path = Path.Combine(_exportDir, fileName);
        File.WriteAllText(path, json);
        return Task.FromResult(path);
    }

    private static string Fmt(decimal? value, string format = "F4") =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? "N/A";
}
