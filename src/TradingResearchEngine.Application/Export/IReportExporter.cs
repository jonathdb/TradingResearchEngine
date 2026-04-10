using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Exports a <see cref="BacktestResult"/> to various formats.
/// Each method returns the file path of the exported file.
/// </summary>
public interface IReportExporter
{
    /// <summary>Exports a Markdown run report (.md).</summary>
    Task<string> ExportMarkdownAsync(BacktestResult result, CancellationToken ct = default);

    /// <summary>Exports the trade log as CSV.</summary>
    Task<string> ExportTradeCsvAsync(BacktestResult result, CancellationToken ct = default);

    /// <summary>Exports the equity curve as CSV.</summary>
    Task<string> ExportEquityCsvAsync(BacktestResult result, CancellationToken ct = default);

    /// <summary>Exports the full BacktestResult as JSON (round-trips without data loss).</summary>
    Task<string> ExportJsonAsync(BacktestResult result, CancellationToken ct = default);
}
