using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Application;
using TradingResearchEngine.Application.Portfolio;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Strategy;
using TradingResearchEngine.Infrastructure;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.Benchmarks;

/// <summary>
/// Backtest engine performance benchmarks measuring throughput and memory allocation
/// across various bar counts and portfolio configurations.
/// Results are exported to artifacts/benchmarks/ as markdown and JSON.
/// Requirements: 20.2, 20.3, 20.4
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[ArtifactsPath("artifacts/benchmarks")]
[MarkdownExporter]
[JsonExporterAttribute.Full]
public class BacktestEngineBenchmarks
{
    private ServiceProvider _serviceProvider = null!;
    private string _tempDataDir = null!;

    [GlobalSetup]
    public void Setup()
    {
        _tempDataDir = Path.Combine(Path.GetTempPath(), "tre-bench-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDataDir);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Repository:BasePath"] = _tempDataDir,
                ["DataProvider:FilePath"] = GetSpyDataPath()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddTradingResearchEngine(configuration);
        services.AddTradingResearchEngineInfrastructure(configuration);
        services.AddStrategyAssembly(typeof(DonchianBreakoutStrategy).Assembly);

        _serviceProvider = services.BuildServiceProvider();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _serviceProvider?.Dispose();
        if (Directory.Exists(_tempDataDir))
        {
            try { Directory.Delete(_tempDataDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Single symbol, 1 year of daily bars (252 bars).
    /// Baseline benchmark for engine throughput.
    /// </summary>
    [Benchmark(Description = "SingleSymbol_1Year_Daily (252 bars)")]
    public async Task SingleSymbol_1Year_Daily()
    {
        var config = CreateSingleSymbolConfig(barsPerYear: 252, barCount: 252);
        await RunSingleBacktestAsync(config);
    }

    /// <summary>
    /// Single symbol, 1 year of H1 bars (6048 bars).
    /// Tests engine performance at intraday resolution.
    /// </summary>
    [Benchmark(Description = "SingleSymbol_1Year_H1 (6048 bars)")]
    public async Task SingleSymbol_1Year_H1()
    {
        var config = CreateSingleSymbolConfig(barsPerYear: 6048, barCount: 6048);
        await RunSingleBacktestAsync(config);
    }

    /// <summary>
    /// Single symbol, 5 years of M15 bars (120960 bars).
    /// Stress test for large datasets.
    /// </summary>
    [Benchmark(Description = "SingleSymbol_5Year_M15 (120960 bars)")]
    public async Task SingleSymbol_5Year_M15()
    {
        var config = CreateSingleSymbolConfig(barsPerYear: 24192, barCount: 120960);
        await RunSingleBacktestAsync(config);
    }

    /// <summary>
    /// Portfolio run with 5 symbols, 1 year of daily bars each.
    /// Tests parallel execution and portfolio aggregation overhead.
    /// </summary>
    [Benchmark(Description = "PortfolioRun_5Symbols_1Year_Daily")]
    public async Task PortfolioRun_5Symbols_1Year_Daily()
    {
        var runner = _serviceProvider.GetRequiredService<PortfolioBacktestRunner>();
        var spyPath = GetSpyDataPath();

        var symbols = Enumerable.Range(0, 5)
            .Select(i => new DataConfig(
                DataProviderType: "csv",
                DataProviderOptions: new Dictionary<string, object>
                {
                    ["FilePath"] = spyPath,
                    ["Symbol"] = $"SYM{i}",
                    ["Interval"] = "1D"
                },
                Timeframe: "Daily",
                BarsPerYear: 252))
            .ToList();

        var config = new PortfolioBacktestConfig(
            Symbols: symbols,
            Strategies: new List<StrategyConfig>
            {
                new StrategyConfig("moving-average-crossover", new Dictionary<string, object>
                {
                    ["FastPeriod"] = 10,
                    ["SlowPeriod"] = 30
                })
            },
            PortfolioRisk: new PortfolioRiskConfig(),
            Execution: new ExecutionConfig(
                SlippageModelType: "Zero",
                CommissionModelType: "Zero"),
            InitialCash: 500_000m,
            Seed: 42,
            Timeframe: "Daily");

        await runner.RunAsync(config, new NullProgressReporter(), CancellationToken.None);
    }

    /// <summary>
    /// Parameter sweep with 10×10 grid (100 combinations) on daily data.
    /// Tests workflow orchestration overhead.
    /// </summary>
    [Benchmark(Description = "ParameterSweep_10x10_Daily")]
    public async Task ParameterSweep_10x10_Daily()
    {
        var runner = _serviceProvider.GetRequiredService<PortfolioBacktestRunner>();
        var spyPath = GetSpyDataPath();

        // Simulate a 10x10 sweep by running 100 portfolio configs with varying parameters
        var tasks = new List<Task>();
        for (int fast = 5; fast <= 50; fast += 5)
        {
            for (int slow = 20; slow <= 110; slow += 10)
            {
                if (slow <= fast) continue; // skip invalid combos

                var config = new PortfolioBacktestConfig(
                    Symbols: new List<DataConfig>
                    {
                        new DataConfig(
                            DataProviderType: "csv",
                            DataProviderOptions: new Dictionary<string, object>
                            {
                                ["FilePath"] = spyPath,
                                ["Symbol"] = "SPY",
                                ["Interval"] = "1D"
                            },
                            Timeframe: "Daily",
                            BarsPerYear: 252)
                    },
                    Strategies: new List<StrategyConfig>
                    {
                        new StrategyConfig("moving-average-crossover", new Dictionary<string, object>
                        {
                            ["FastPeriod"] = fast,
                            ["SlowPeriod"] = slow
                        })
                    },
                    PortfolioRisk: new PortfolioRiskConfig(),
                    Execution: new ExecutionConfig(
                        SlippageModelType: "Zero",
                        CommissionModelType: "Zero"),
                    InitialCash: 100_000m,
                    Seed: 42,
                    Timeframe: "Daily");

                await runner.RunAsync(config, new NullProgressReporter(), CancellationToken.None);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private ScenarioConfig CreateSingleSymbolConfig(int barsPerYear, int barCount)
    {
        // Generate synthetic data file for the benchmark
        var dataPath = GenerateSyntheticDataFile(barCount);

        return new ScenarioConfig(
            ScenarioId: $"bench-{barCount}",
            Description: $"Benchmark {barCount} bars",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>
            {
                ["FilePath"] = dataPath,
                ["Symbol"] = "BENCH",
                ["Interval"] = "1D"
            },
            StrategyType: "moving-average-crossover",
            StrategyParameters: new Dictionary<string, object>
            {
                ["FastPeriod"] = 10,
                ["SlowPeriod"] = 30
            },
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: "Zero",
            CommissionModelType: "Zero",
            InitialCash: 100_000m,
            AnnualRiskFreeRate: 0m,
            RandomSeed: 42,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null,
            BarsPerYear: barsPerYear);
    }

    private async Task RunSingleBacktestAsync(ScenarioConfig config)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var dataProvider = new CsvDataProvider(
            config.DataProviderOptions["FilePath"]?.ToString() ?? "",
            loggerFactory.CreateLogger<CsvDataProvider>());

        var strategy = new MovingAverageCrossoverStrategy(10, 30);
        var riskLayer = _serviceProvider.GetRequiredService<Core.Risk.IRiskLayer>();
        var executionHandler = _serviceProvider.GetRequiredService<Core.Execution.IExecutionHandler>();
        var barDataPool = _serviceProvider.GetService<BarDataPool>();

        var engine = new BacktestEngine(
            dataProvider, strategy, riskLayer, executionHandler,
            loggerFactory.CreateLogger<BacktestEngine>(),
            barDataPool: barDataPool);

        await engine.RunAsync(config);
    }

    private string GenerateSyntheticDataFile(int barCount)
    {
        var filePath = Path.Combine(_tempDataDir, $"bench-{barCount}.csv");
        if (File.Exists(filePath)) return filePath;

        using var writer = new StreamWriter(filePath);
        writer.WriteLine("Date,Open,High,Low,Close,Volume");

        var rng = new Random(42);
        var baseDate = new DateTime(2015, 1, 1);
        var price = 100.0;

        for (int i = 0; i < barCount; i++)
        {
            var change = (rng.NextDouble() - 0.5) * 2.0;
            price = Math.Max(10.0, price + change);

            var open = price + (rng.NextDouble() - 0.5);
            var close = price + (rng.NextDouble() - 0.5);
            var high = Math.Max(open, close) + rng.NextDouble() * 2.0;
            var low = Math.Min(open, close) - rng.NextDouble() * 2.0;
            low = Math.Max(1.0, low);
            high = Math.Max(low + 0.01, high);
            var volume = rng.Next(100000, 10000000);

            var date = baseDate.AddDays(i);
            writer.WriteLine($"{date:yyyy-MM-dd},{open:F4},{high:F4},{low:F4},{close:F4},{volume}");
        }

        return filePath;
    }

    private static string GetSpyDataPath()
    {
        // Try multiple relative paths to find the sample data
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "data", "spy-daily.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "samples", "data", "spy-daily.csv"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "samples", "data", "spy-daily.csv"),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath)) return fullPath;
        }

        // Fallback: return the first candidate (will fail with descriptive error)
        return Path.GetFullPath(candidates[0]);
    }

    private sealed class NullProgressReporter : IProgressReporter
    {
        public void Report(int current, int total, string label) { }
        public void Report(ProgressSnapshot snapshot) { }
    }
}
