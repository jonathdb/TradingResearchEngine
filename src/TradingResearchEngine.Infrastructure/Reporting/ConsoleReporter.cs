using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Reporting;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Infrastructure.Reporting;

/// <summary>Writes formatted BacktestResult and ComparisonReport to stdout.</summary>
public sealed class ConsoleReporter : IReporter
{
    private readonly int _dp;

    /// <inheritdoc cref="ConsoleReporter"/>
    public ConsoleReporter(IOptions<ReportingOptions> options) => _dp = options.Value.DecimalPlaces;

    /// <inheritdoc/>
    public void RenderToConsole(BacktestResult result)
    {
        Console.WriteLine($"=== Backtest Result: {result.ScenarioConfig.ScenarioId} ===");
        Console.WriteLine($"Status:       {result.Status}");
        Console.WriteLine($"Start Equity: ${result.StartEquity.ToString($"F{_dp}")}");
        Console.WriteLine($"End Equity:   ${result.EndEquity.ToString($"F{_dp}")}");
        Console.WriteLine($"Max Drawdown: {result.MaxDrawdown.ToString($"P{_dp}")}");
        Console.WriteLine($"Sharpe Ratio: {result.SharpeRatio?.ToString($"F{_dp}") ?? "N/A"}");
        Console.WriteLine($"Sortino:      {result.SortinoRatio?.ToString($"F{_dp}") ?? "N/A"}");
        Console.WriteLine($"Calmar:       {result.CalmarRatio?.ToString($"F{_dp}") ?? "N/A"}");
        Console.WriteLine($"RoMaD:        {result.ReturnOnMaxDrawdown?.ToString($"F{_dp}") ?? "N/A"}");
        Console.WriteLine($"Total Trades: {result.TotalTrades}");
        Console.WriteLine($"Win Rate:     {result.WinRate?.ToString($"P{_dp}") ?? "N/A"}");
        Console.WriteLine($"Profit Factor:{result.ProfitFactor?.ToString($"F{_dp}") ?? "N/A"}");
        Console.WriteLine($"Expectancy:   ${result.Expectancy?.ToString($"F{_dp}") ?? "N/A"}");
        Console.WriteLine($"Avg Win:      ${result.AverageWin?.ToString($"F{_dp}") ?? "N/A"}");
        Console.WriteLine($"Avg Loss:     ${result.AverageLoss?.ToString($"F{_dp}") ?? "N/A"}");
        Console.WriteLine($"Avg Hold:     {result.AverageHoldingPeriod?.ToString() ?? "N/A"}");
        Console.WriteLine($"Smoothness:   {result.EquityCurveSmoothness?.ToString($"F4") ?? "N/A"}");
        Console.WriteLine($"Max Consec L: {result.MaxConsecutiveLosses}");
        Console.WriteLine($"Max Consec W: {result.MaxConsecutiveWins}");
        Console.WriteLine($"Duration:     {result.RunDurationMs}ms");
    }

    /// <inheritdoc/>
    public string RenderToMarkdown(BacktestResult result) => string.Empty; // Delegated to MarkdownReporter

    /// <inheritdoc/>
    public void RenderToConsole(ComparisonReport report)
    {
        Console.WriteLine("=== Scenario Comparison ===");
        Console.WriteLine($"{"Scenario",-20} {"Sharpe",10} {"Sortino",10} {"MaxDD",10} {"WinRate",10} {"PF",10} {"Trades",8} {"EndEquity",14}");
        foreach (var row in report.Rows)
        {
            Console.WriteLine(
                $"{row.ScenarioId,-20} " +
                $"{(row.SharpeRatio?.ToString($"F{_dp}") ?? "N/A"),10} " +
                $"{(row.SortinoRatio?.ToString($"F{_dp}") ?? "N/A"),10} " +
                $"{row.MaxDrawdown.ToString($"P{_dp}"),10} " +
                $"{(row.WinRate?.ToString($"P{_dp}") ?? "N/A"),10} " +
                $"{(row.ProfitFactor?.ToString($"F{_dp}") ?? "N/A"),10} " +
                $"{row.TotalTrades,8} " +
                $"${row.EndEquity.ToString($"F{_dp}"),13}");
        }
        Console.WriteLine($"Best by Sharpe:   {report.BestBySharpe}");
        Console.WriteLine($"Best by Drawdown: {report.BestByDrawdown}");
    }

    /// <inheritdoc/>
    public string RenderToMarkdown(ComparisonReport report) => string.Empty;
}
