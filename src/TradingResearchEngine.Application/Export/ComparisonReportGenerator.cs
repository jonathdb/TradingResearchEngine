using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Generates persistent comparison reports in Markdown (and optionally HTML) format.
/// Compares multiple <see cref="BacktestResult"/> instances with key metrics,
/// equity curve summaries, and summary statistics.
/// </summary>
public sealed class ComparisonReportGenerator
{
    private readonly ComparisonReportOptions _options;
    private readonly ILogger<ComparisonReportGenerator> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="ComparisonReportGenerator"/>.
    /// </summary>
    /// <param name="options">Configuration options for report output.</param>
    /// <param name="logger">Logger instance.</param>
    public ComparisonReportGenerator(
        IOptions<ComparisonReportOptions> options,
        ILogger<ComparisonReportGenerator> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Generates a comparison report artifact from the given backtest results.
    /// Persists the Markdown file to the configured output directory.
    /// Optionally generates an HTML report when <see cref="ComparisonReportOptions.EnableHtml"/> is true.
    /// </summary>
    /// <param name="results">Two or more backtest results to compare.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated comparison report artifact with file paths and content.</returns>
    /// <exception cref="ArgumentException">Thrown when fewer than 2 results are supplied.</exception>
    public async Task<ComparisonReportArtifact> GenerateAsync(
        IReadOnlyList<BacktestResult> results,
        CancellationToken ct = default)
    {
        if (results.Count < 2)
            throw new ArgumentException("At least 2 BacktestResult instances are required for comparison.", nameof(results));

        ct.ThrowIfCancellationRequested();

        var markdown = RenderMarkdown(results);
        string? html = _options.EnableHtml ? RenderHtml(markdown, results) : null;

        Directory.CreateDirectory(_options.OutputDirectory);

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var mdFileName = $"comparison_{timestamp}.md";
        var mdPath = Path.Combine(_options.OutputDirectory, mdFileName);

        await File.WriteAllTextAsync(mdPath, markdown, ct);
        _logger.LogInformation("Comparison report persisted to {Path}", mdPath);

        if (html is not null)
        {
            var htmlFileName = $"comparison_{timestamp}.html";
            var htmlPath = Path.Combine(_options.OutputDirectory, htmlFileName);
            await File.WriteAllTextAsync(htmlPath, html, ct);
            _logger.LogInformation("HTML comparison report persisted to {Path}", htmlPath);
        }

        return new ComparisonReportArtifact(markdown, html, mdPath);
    }

    /// <summary>
    /// Renders the Markdown content for a comparison report without persisting to disk.
    /// Useful for preview or in-memory consumption.
    /// </summary>
    /// <param name="results">Two or more backtest results to compare.</param>
    /// <returns>The Markdown content string.</returns>
    public string RenderMarkdown(IReadOnlyList<BacktestResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Strategy Comparison Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"**Scenarios Compared:** {results.Count}");
        sb.AppendLine();

        // Summary: best performers
        var bestBySharpe = results
            .OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue)
            .First();
        var bestByDrawdown = results
            .OrderBy(r => r.MaxDrawdown)
            .First();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Best by Sharpe:** {bestBySharpe.ScenarioConfig.ScenarioId} ({Fmt(bestBySharpe.SharpeRatio)})");
        sb.AppendLine($"- **Best by Drawdown:** {bestByDrawdown.ScenarioConfig.ScenarioId} ({bestByDrawdown.MaxDrawdown:P2})");
        sb.AppendLine();

        // Key metrics comparison table
        sb.AppendLine("## Key Metrics");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Sharpe | Sortino | Calmar | Max DD | Win Rate | PF | Trades | End Equity |");
        sb.AppendLine("|----------|--------|---------|--------|--------|----------|----|--------|------------|");

        foreach (var r in results)
        {
            sb.AppendLine(string.Join("",
                $"| {r.ScenarioConfig.ScenarioId} ",
                $"| {Fmt(r.SharpeRatio)} ",
                $"| {Fmt(r.SortinoRatio)} ",
                $"| {Fmt(r.CalmarRatio)} ",
                $"| {r.MaxDrawdown.ToString("P2", CultureInfo.InvariantCulture)} ",
                $"| {Fmt(r.WinRate, "P1")} ",
                $"| {Fmt(r.ProfitFactor)} ",
                $"| {r.TotalTrades} ",
                $"| ${r.EndEquity.ToString("F2", CultureInfo.InvariantCulture)} |"));
        }

        sb.AppendLine();

        // Extended statistics
        sb.AppendLine("## Extended Statistics");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Expectancy | K-Ratio | Recovery Factor | Max Consec Losses | Avg Holding |");
        sb.AppendLine("|----------|------------|---------|-----------------|-------------------|-------------|");

        foreach (var r in results)
        {
            sb.AppendLine(string.Join("",
                $"| {r.ScenarioConfig.ScenarioId} ",
                $"| {Fmt(r.Expectancy, "F2")} ",
                $"| {Fmt(r.EquityCurveSmoothness, "F4")} ",
                $"| {Fmt(r.RecoveryFactor)} ",
                $"| {r.MaxConsecutiveLosses} ",
                $"| {r.AverageHoldingPeriod?.ToString() ?? "N/A"} |"));
        }

        sb.AppendLine();

        // Equity curve summary
        sb.AppendLine("## Equity Curve Summary");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Start Equity | End Equity | Total Return | Curve Points |");
        sb.AppendLine("|----------|--------------|------------|--------------|--------------|");

        foreach (var r in results)
        {
            var totalReturn = r.StartEquity > 0
                ? (r.EndEquity - r.StartEquity) / r.StartEquity
                : 0m;

            sb.AppendLine(string.Join("",
                $"| {r.ScenarioConfig.ScenarioId} ",
                $"| ${r.StartEquity.ToString("F2", CultureInfo.InvariantCulture)} ",
                $"| ${r.EndEquity.ToString("F2", CultureInfo.InvariantCulture)} ",
                $"| {totalReturn.ToString("P2", CultureInfo.InvariantCulture)} ",
                $"| {r.EquityCurve.Count} |"));
        }

        sb.AppendLine();

        // Configuration comparison
        sb.AppendLine("## Configuration");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Strategy | Realism Profile | BarsPerYear | Initial Cash |");
        sb.AppendLine("|----------|----------|-----------------|-------------|--------------|");

        foreach (var r in results)
        {
            sb.AppendLine(string.Join("",
                $"| {r.ScenarioConfig.ScenarioId} ",
                $"| {r.ScenarioConfig.StrategyType} ",
                $"| {r.ScenarioConfig.RealismProfile} ",
                $"| {r.ScenarioConfig.BarsPerYear} ",
                $"| ${r.ScenarioConfig.InitialCash.ToString("N0", CultureInfo.InvariantCulture)} |"));
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*Report generated by TradingResearchEngine*");

        return sb.ToString();
    }

    private static string RenderHtml(string markdown, IReadOnlyList<BacktestResult> results)
    {
        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine("  <title>Strategy Comparison Report</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; margin: 2rem; line-height: 1.6; }");
        sb.AppendLine("    h1 { color: #1a1a2e; border-bottom: 2px solid #16213e; padding-bottom: 0.5rem; }");
        sb.AppendLine("    h2 { color: #16213e; margin-top: 2rem; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; margin: 1rem 0; }");
        sb.AppendLine("    th, td { border: 1px solid #ddd; padding: 8px 12px; text-align: left; }");
        sb.AppendLine("    th { background-color: #16213e; color: white; }");
        sb.AppendLine("    tr:nth-child(even) { background-color: #f8f9fa; }");
        sb.AppendLine("    tr:hover { background-color: #e9ecef; }");
        sb.AppendLine("    .best { font-weight: bold; color: #28a745; }");
        sb.AppendLine("    .summary { background: #f0f4f8; padding: 1rem; border-radius: 8px; margin: 1rem 0; }");
        sb.AppendLine("    .footer { margin-top: 2rem; color: #6c757d; font-size: 0.9rem; border-top: 1px solid #dee2e6; padding-top: 1rem; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        sb.AppendLine($"  <h1>Strategy Comparison Report</h1>");
        sb.AppendLine($"  <p><strong>Generated:</strong> {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | <strong>Scenarios:</strong> {results.Count}</p>");

        // Summary
        var bestBySharpe = results
            .OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue)
            .First();
        var bestByDrawdown = results
            .OrderBy(r => r.MaxDrawdown)
            .First();

        sb.AppendLine("  <div class=\"summary\">");
        sb.AppendLine($"    <p><strong>Best by Sharpe:</strong> <span class=\"best\">{bestBySharpe.ScenarioConfig.ScenarioId}</span> ({Fmt(bestBySharpe.SharpeRatio)})</p>");
        sb.AppendLine($"    <p><strong>Best by Drawdown:</strong> <span class=\"best\">{bestByDrawdown.ScenarioConfig.ScenarioId}</span> ({bestByDrawdown.MaxDrawdown:P2})</p>");
        sb.AppendLine("  </div>");

        // Key metrics table
        sb.AppendLine("  <h2>Key Metrics</h2>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <thead><tr><th>Scenario</th><th>Sharpe</th><th>Sortino</th><th>Calmar</th><th>Max DD</th><th>Win Rate</th><th>PF</th><th>Trades</th><th>End Equity</th></tr></thead>");
        sb.AppendLine("    <tbody>");

        foreach (var r in results)
        {
            sb.AppendLine(string.Join("",
                "      <tr>",
                $"<td>{r.ScenarioConfig.ScenarioId}</td>",
                $"<td>{Fmt(r.SharpeRatio)}</td>",
                $"<td>{Fmt(r.SortinoRatio)}</td>",
                $"<td>{Fmt(r.CalmarRatio)}</td>",
                $"<td>{r.MaxDrawdown.ToString("P2", CultureInfo.InvariantCulture)}</td>",
                $"<td>{Fmt(r.WinRate, "P1")}</td>",
                $"<td>{Fmt(r.ProfitFactor)}</td>",
                $"<td>{r.TotalTrades}</td>",
                $"<td>${r.EndEquity.ToString("F2", CultureInfo.InvariantCulture)}</td>",
                "</tr>"));
        }

        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");

        // Equity curve summary
        sb.AppendLine("  <h2>Equity Curve Summary</h2>");
        sb.AppendLine("  <table>");
        sb.AppendLine("    <thead><tr><th>Scenario</th><th>Start Equity</th><th>End Equity</th><th>Total Return</th><th>Curve Points</th></tr></thead>");
        sb.AppendLine("    <tbody>");

        foreach (var r in results)
        {
            var totalReturn = r.StartEquity > 0
                ? (r.EndEquity - r.StartEquity) / r.StartEquity
                : 0m;

            sb.AppendLine(string.Join("",
                "      <tr>",
                $"<td>{r.ScenarioConfig.ScenarioId}</td>",
                $"<td>${r.StartEquity.ToString("F2", CultureInfo.InvariantCulture)}</td>",
                $"<td>${r.EndEquity.ToString("F2", CultureInfo.InvariantCulture)}</td>",
                $"<td>{totalReturn.ToString("P2", CultureInfo.InvariantCulture)}</td>",
                $"<td>{r.EquityCurve.Count}</td>",
                "</tr>"));
        }

        sb.AppendLine("    </tbody>");
        sb.AppendLine("  </table>");

        sb.AppendLine("  <div class=\"footer\">");
        sb.AppendLine("    <p><em>Report generated by TradingResearchEngine</em></p>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string Fmt(decimal? value, string format = "F4") =>
        value?.ToString(format, CultureInfo.InvariantCulture) ?? "N/A";
}
