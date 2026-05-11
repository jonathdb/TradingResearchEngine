using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Produces byte arrays for trade log CSV, equity curve CSV, and full result JSON exports.
/// Designed for browser file download via Blazor JS interop.
/// </summary>
public sealed class ResultExportService : IResultExportService
{
    /// <inheritdoc/>
    public Task<byte[]> ExportTradesCsvAsync(BacktestResult result, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var sb = new StringBuilder();
        sb.AppendLine("EntryTime,ExitTime,Direction,EntryPrice,ExitPrice,Quantity,PnL,PnLPct,Commission,RunningEquity");

        if (result.Trades is { Count: > 0 })
        {
            decimal runningEquity = result.StartEquity;
            foreach (var trade in result.Trades)
            {
                runningEquity += trade.NetPnl;
                var pnlPct = trade.EntryPrice * trade.Quantity > 0
                    ? (trade.NetPnl / (trade.EntryPrice * trade.Quantity) * 100m)
                    : 0m;

                sb.AppendLine(string.Join(",",
                    trade.EntryTime.ToString("o", CultureInfo.InvariantCulture),
                    trade.ExitTime.ToString("o", CultureInfo.InvariantCulture),
                    trade.Direction.ToString(),
                    trade.EntryPrice.ToString(CultureInfo.InvariantCulture),
                    trade.ExitPrice.ToString(CultureInfo.InvariantCulture),
                    trade.Quantity.ToString(CultureInfo.InvariantCulture),
                    trade.NetPnl.ToString(CultureInfo.InvariantCulture),
                    pnlPct.ToString("F4", CultureInfo.InvariantCulture),
                    trade.Commission.ToString(CultureInfo.InvariantCulture),
                    runningEquity.ToString(CultureInfo.InvariantCulture)));
            }
        }

        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <inheritdoc/>
    public Task<byte[]> ExportEquityCurveCsvAsync(BacktestResult result, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,TotalEquity,CashBalance,OpenEquity,DrawdownPct");

        if (result.EquityCurve is { Count: > 0 })
        {
            decimal peak = result.EquityCurve[0].TotalEquity;
            foreach (var point in result.EquityCurve)
            {
                if (point.TotalEquity > peak) peak = point.TotalEquity;
                var drawdownPct = peak > 0 ? (point.TotalEquity - peak) / peak * 100m : 0m;

                sb.AppendLine(string.Join(",",
                    point.Timestamp.ToString("o", CultureInfo.InvariantCulture),
                    point.TotalEquity.ToString(CultureInfo.InvariantCulture),
                    point.CashBalance.ToString(CultureInfo.InvariantCulture),
                    point.UnrealisedPnl.ToString(CultureInfo.InvariantCulture),
                    drawdownPct.ToString("F4", CultureInfo.InvariantCulture)));
            }
        }

        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <inheritdoc/>
    public Task<byte[]> ExportResultJsonAsync(BacktestResult result, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(result, options);
        return Task.FromResult(json);
    }
}
