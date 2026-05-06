using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Infrastructure.DataProviders;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;

namespace TradingResearchEngine.Benchmarks;

/// <summary>
/// Benchmark comparing allocation with and without BarDataPool.
/// Validates that object pooling achieves ≥ 20% reduction in allocated bytes.
/// 
/// Expected result: BarDataPool reduces GC pressure by pooling List&lt;BarRecord&gt;
/// and decimal[] arrays on the hot path, avoiding per-bar allocations.
/// 
/// Requirements: 21.3
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[ArtifactsPath("artifacts/benchmarks")]
[MarkdownExporter]
[JsonExporterAttribute.Full]
public class BarDataPoolBenchmarks
{
    private string _syntheticDataPath = null!;
    private string _tempDir = null!;

    /// <summary>
    /// Number of bars for the benchmark. 5 years of M15 data provides
    /// a large enough dataset to measure allocation differences.
    /// </summary>
    private const int BarCount = 120960;

    [GlobalSetup]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "tre-pool-bench-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _syntheticDataPath = GenerateSyntheticDataFile(BarCount);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Backtest engine run WITHOUT BarDataPool.
    /// Baseline measurement for allocation comparison.
    /// </summary>
    [Benchmark(Baseline = true, Description = "Without BarDataPool")]
    public async Task WithoutBarDataPool()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var dataProvider = new CsvDataProvider(_syntheticDataPath,
            new NullLogger<CsvDataProvider>());
        var strategy = new MovingAverageCrossoverStrategy(10, 30);
        var riskLayer = CreateRiskLayer();
        var executionHandler = CreateExecutionHandler();

        var engine = new BacktestEngine(
            dataProvider, strategy, riskLayer, executionHandler,
            new NullLogger<BacktestEngine>(),
            barDataPool: null); // No pooling

        var config = CreateConfig();
        await engine.RunAsync(config);
    }

    /// <summary>
    /// Backtest engine run WITH BarDataPool.
    /// Expected: ≥ 20% reduction in allocated bytes compared to baseline.
    /// The pool reuses List&lt;BarRecord&gt; and decimal[] instances across bars,
    /// reducing GC pressure on the hot path.
    /// </summary>
    [Benchmark(Description = "With BarDataPool")]
    public async Task WithBarDataPool()
    {
        var dataProvider = new CsvDataProvider(_syntheticDataPath,
            new NullLogger<CsvDataProvider>());
        var strategy = new MovingAverageCrossoverStrategy(10, 30);
        var riskLayer = CreateRiskLayer();
        var executionHandler = CreateExecutionHandler();
        var pool = new BarDataPool();

        var engine = new BacktestEngine(
            dataProvider, strategy, riskLayer, executionHandler,
            new NullLogger<BacktestEngine>(),
            barDataPool: pool); // With pooling

        var config = CreateConfig();
        await engine.RunAsync(config);
    }

    private ScenarioConfig CreateConfig() => new(
        ScenarioId: "pool-bench",
        Description: "BarDataPool benchmark",
        ReplayMode: ReplayMode.Bar,
        DataProviderType: "csv",
        DataProviderOptions: new Dictionary<string, object>
        {
            ["FilePath"] = _syntheticDataPath,
            ["Symbol"] = "BENCH",
            ["Interval"] = "M15"
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
        BarsPerYear: 24192);

    private static IRiskLayer CreateRiskLayer()
    {
        var options = Options.Create(new RiskOptions());
        return new DefaultRiskLayer(options, new NullLogger<DefaultRiskLayer>());
    }

    private static IExecutionHandler CreateExecutionHandler()
    {
        var slippage = new ZeroSlippageModel();
        var commission = new ZeroCommissionModel();
        return new SimulatedExecutionHandler(slippage, commission,
            new NullLogger<SimulatedExecutionHandler>());
    }

    private string GenerateSyntheticDataFile(int barCount)
    {
        var filePath = Path.Combine(_tempDir, $"pool-bench-{barCount}.csv");

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

            var date = baseDate.AddMinutes(i * 15); // M15 intervals
            writer.WriteLine($"{date:yyyy-MM-dd HH:mm},{open:F4},{high:F4},{low:F4},{close:F4},{volume}");
        }

        return filePath;
    }

    private sealed class ZeroSlippageModel : ISlippageModel
    {
        public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market) => 0m;
    }

    private sealed class ZeroCommissionModel : ICommissionModel
    {
        public decimal ComputeCommission(OrderEvent order, decimal fillPrice, decimal quantity) => 0m;
    }
}
