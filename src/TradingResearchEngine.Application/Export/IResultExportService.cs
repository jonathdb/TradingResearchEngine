using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Exports backtest result data (trade log, equity curve) as byte arrays
/// suitable for browser file download via JS interop.
/// </summary>
public interface IResultExportService
{
    /// <summary>
    /// Exports the trade log as CSV bytes.
    /// Columns: EntryTime, ExitTime, Direction, EntryPrice, ExitPrice, Quantity, PnL, PnLPct, Commission, RunningEquity.
    /// </summary>
    Task<byte[]> ExportTradesCsvAsync(BacktestResult result, CancellationToken ct = default);

    /// <summary>
    /// Exports the equity curve as CSV bytes.
    /// Columns: Timestamp, TotalEquity, CashBalance, OpenEquity, DrawdownPct.
    /// </summary>
    Task<byte[]> ExportEquityCurveCsvAsync(BacktestResult result, CancellationToken ct = default);

    /// <summary>
    /// Exports the full backtest result as JSON bytes (round-trips without data loss).
    /// </summary>
    Task<byte[]> ExportResultJsonAsync(BacktestResult result, CancellationToken ct = default);
}
