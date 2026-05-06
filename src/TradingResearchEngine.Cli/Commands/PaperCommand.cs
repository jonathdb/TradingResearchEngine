using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.PaperTrading;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.PaperTrading;
using TradingResearchEngine.Infrastructure.DataProviders;
using TradingResearchEngine.Infrastructure.Reporting;

namespace TradingResearchEngine.Cli.Commands;

/// <summary>
/// Handles the 'paper' subcommand for running paper trading sessions from the CLI.
/// </summary>
public static class PaperCommand
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// Executes the paper trading subcommand.
    /// </summary>
    /// <param name="scenarioPath">Path to the scenario JSON file.</param>
    /// <param name="speed">Playback speed ratio (default 1.0 = real-time).</param>
    /// <param name="serviceProvider">The DI service provider.</param>
    /// <returns>Exit code: 0 for success, 1 for error.</returns>
    public static async Task<int> ExecuteAsync(string scenarioPath, double speed, IServiceProvider serviceProvider)
    {
        if (!File.Exists(scenarioPath))
        {
            Console.Error.WriteLine($"Scenario file not found: {scenarioPath}");
            return 1;
        }

        ScenarioConfig config;
        try
        {
            var json = await File.ReadAllTextAsync(scenarioPath);
            config = JsonSerializer.Deserialize<ScenarioConfig>(json, s_jsonOptions)!;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to parse scenario file: {ex.Message}");
            return 1;
        }

        if (speed <= 0)
        {
            Console.Error.WriteLine("Speed must be greater than zero.");
            return 1;
        }

        // Resolve the streaming data provider with the configured speed
        var dataProvider = serviceProvider.GetRequiredService<IDataProvider>();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var streamingProvider = new PollingStreamingDataProvider(
            dataProvider,
            TimeSpan.FromSeconds(1),
            speed,
            loggerFactory.CreateLogger<PollingStreamingDataProvider>());

        // Resolve the paper trading session
        var strategy = serviceProvider.GetRequiredService<TradingResearchEngine.Core.Strategy.IStrategy>();
        var riskLayer = serviceProvider.GetRequiredService<TradingResearchEngine.Core.Risk.IRiskLayer>();
        var executionHandler = serviceProvider.GetRequiredService<TradingResearchEngine.Core.Execution.IExecutionHandler>();
        var slippageModel = serviceProvider.GetRequiredService<TradingResearchEngine.Core.Execution.ISlippageModel>();
        var commissionModel = serviceProvider.GetRequiredService<TradingResearchEngine.Core.Execution.ICommissionModel>();
        var repository = serviceProvider.GetRequiredService<TradingResearchEngine.Core.Persistence.IRepository<PaperSessionRecord>>();
        var sessionLogger = loggerFactory.CreateLogger<SimulatedPaperTradingSession>();

        using var session = new SimulatedPaperTradingSession(
            streamingProvider, strategy, riskLayer, executionHandler,
            slippageModel, commissionModel, repository, sessionLogger);

        using var cts = new CancellationTokenSource();

        // Subscribe to bar events for live console output
        var barSubscription = session.BarStream.Subscribe(
            barEvent =>
            {
                var bar = barEvent.Bar;
                var snapshot = barEvent.Snapshot;
                Console.WriteLine(
                    $"[{bar.Timestamp:yyyy-MM-dd HH:mm}] {bar.Symbol} Close={bar.Close:F4} " +
                    $"Equity={snapshot.TotalEquity:F2} Positions={snapshot.Positions.Count}");
            },
            ex => Console.Error.WriteLine($"Error: {ex.Message}"));

        // Handle Ctrl+C for graceful stop
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        Console.WriteLine($"Starting paper trading session (speed={speed:F1}x)...");
        Console.WriteLine("Press Ctrl+C to stop.");
        Console.WriteLine();

        try
        {
            await session.StartAsync(config, cts.Token);

            // Wait until cancelled
            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected on Ctrl+C
            }
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Stop session and get results
        Console.WriteLine();
        Console.WriteLine("Stopping session...");

        PaperTradingResult result;
        try
        {
            result = await session.StopAsync();
        }
        catch (InvalidOperationException)
        {
            // Session may already be stopped if data ran out
            Console.WriteLine("Session ended (data exhausted).");
            barSubscription.Dispose();
            return 0;
        }

        barSubscription.Dispose();

        // Write Markdown report
        var reportPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            $"paper-session-{DateTime.Now:yyyyMMdd-HHmmss}.md");

        var reportContent = GenerateReport(result, config, speed);
        await File.WriteAllTextAsync(reportPath, reportContent);

        Console.WriteLine($"Session complete. Report written to: {reportPath}");
        Console.WriteLine($"  Final Equity: {result.FinalPortfolio.TotalEquity:F2}");
        Console.WriteLine($"  Trades: {result.ClosedTrades.Count}");
        Console.WriteLine($"  Duration: {result.StoppedAt - result.StartedAt:hh\\:mm\\:ss}");

        return 0;
    }

    private static string GenerateReport(PaperTradingResult result, ScenarioConfig config, double speed)
    {
        var metrics = result.EquivalentBacktestResult;
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# Paper Trading Session Report");
        sb.AppendLine();
        sb.AppendLine("## Session Details");
        sb.AppendLine();
        sb.AppendLine($"- **Strategy**: {config.StrategyType}");
        sb.AppendLine($"- **Speed**: {speed:F1}x");
        sb.AppendLine($"- **Started**: {result.StartedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"- **Stopped**: {result.StoppedAt:yyyy-MM-dd HH:mm:ss UTC}");
        sb.AppendLine($"- **Duration**: {result.StoppedAt - result.StartedAt:hh\\:mm\\:ss}");
        sb.AppendLine($"- **Status**: {result.FinalStatus}");
        sb.AppendLine();
        sb.AppendLine("## Performance Summary");
        sb.AppendLine();
        sb.AppendLine($"| Metric | Value |");
        sb.AppendLine($"|--------|-------|");
        sb.AppendLine($"| Start Equity | {metrics.StartEquity:F2} |");
        sb.AppendLine($"| End Equity | {metrics.EndEquity:F2} |");
        sb.AppendLine($"| Total PnL | {metrics.EndEquity - metrics.StartEquity:F2} |");
        sb.AppendLine($"| Total Trades | {metrics.TotalTrades} |");
        sb.AppendLine($"| Win Rate | {metrics.WinRate?.ToString("P2") ?? "N/A"} |");
        sb.AppendLine($"| Sharpe Ratio | {metrics.SharpeRatio?.ToString("F4") ?? "N/A"} |");
        sb.AppendLine($"| Max Drawdown | {metrics.MaxDrawdown:P2} |");
        sb.AppendLine($"| Profit Factor | {metrics.ProfitFactor?.ToString("F2") ?? "N/A"} |");
        sb.AppendLine();

        if (result.ClosedTrades.Count > 0)
        {
            sb.AppendLine("## Trades");
            sb.AppendLine();
            sb.AppendLine("| # | Symbol | Entry | Exit | PnL |");
            sb.AppendLine("|---|--------|-------|------|-----|");

            int tradeNum = 1;
            foreach (var trade in result.ClosedTrades.Take(50))
            {
                sb.AppendLine($"| {tradeNum++} | {trade.Symbol} | {trade.EntryPrice:F4} | {trade.ExitPrice:F4} | {trade.NetPnl:F2} |");
            }

            if (result.ClosedTrades.Count > 50)
                sb.AppendLine($"| ... | ({result.ClosedTrades.Count - 50} more trades) | | | |");
        }

        return sb.ToString();
    }

    /// <summary>Prints help for the paper subcommand.</summary>
    public static void PrintHelp()
    {
        Console.WriteLine("  paper                    Run a paper trading session");
        Console.WriteLine("    --scenario <path>      Path to scenario JSON file (required)");
        Console.WriteLine("    --speed <ratio>        Playback speed ratio (default: 1.0)");
    }
}
