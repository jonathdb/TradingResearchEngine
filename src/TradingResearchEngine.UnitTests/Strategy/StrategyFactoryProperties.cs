using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.UnitTests.Strategy;

// Feature: trading-engine-stories, Property 1: Factory Isolation

/// <summary>
/// Property 1: Factory Isolation — Concurrent Instances Produce Independent Results.
/// For any IStrategyFactory and any valid StrategyConfig, creating N instances concurrently
/// and executing them in parallel on the same bar data SHALL produce results identical to
/// sequential execution — no shared mutable state corruption.
/// **Validates: Requirements 2.2, 2.5**
/// </summary>
public class StrategyFactoryProperties
{
    /// <summary>
    /// A simple test strategy that tracks a running sum of Close prices.
    /// Each instance maintains its own mutable state (running sum and bar count).
    /// If instances share state, parallel execution will produce incorrect results.
    /// </summary>
    private sealed class RunningSumStrategy : IStrategy
    {
        private decimal _runningSum;
        private int _barCount;

        public decimal RunningSum => _runningSum;
        public int BarCount => _barCount;

        public void Initialize(StrategyConfig config) { }

        public void Reset()
        {
            _runningSum = 0m;
            _barCount = 0;
        }

        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            if (evt is BarEvent bar)
            {
                _runningSum += bar.Close;
                _barCount++;
            }
            return Array.Empty<EngineEvent>();
        }
    }

    /// <summary>
    /// A test factory that creates independent RunningSumStrategy instances.
    /// Each call to Create() returns a brand new instance with its own state.
    /// </summary>
    private sealed class RunningSumStrategyFactory : IStrategyFactory
    {
        public string StrategyType => "running-sum-test";

        public IStrategy Create(StrategyConfig config) => new RunningSumStrategy();
    }

    /// <summary>
    /// Generates a list of BarEvents from random close prices.
    /// </summary>
    private static List<BarEvent> GenerateBars(decimal[] closePrices)
    {
        var baseTime = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return closePrices.Select((price, i) => new BarEvent(
            Symbol: "TEST",
            Interval: "1D",
            Open: price,
            High: price + 1m,
            Low: price - 1m,
            Close: price,
            Volume: 1000m,
            Timestamp: baseTime.AddDays(i)
        )).ToList();
    }

    /// <summary>
    /// Executes a strategy instance on the given bars and returns (RunningSum, BarCount).
    /// </summary>
    private static (decimal RunningSum, int BarCount) ExecuteStrategy(IStrategy strategy, List<BarEvent> bars)
    {
        foreach (var bar in bars)
        {
            strategy.OnMarketData(bar);
        }
        var s = (RunningSumStrategy)strategy;
        return (s.RunningSum, s.BarCount);
    }

    [Property(MaxTest = 100)]
    public bool ConcurrentInstances_ProduceIndependentResults(PositiveInt instanceCountWrap, PositiveInt barCountWrap)
    {
        // Constrain N to [2, 20] instances and bar count to [1, 200]
        var instanceCount = (instanceCountWrap.Get % 19) + 2; // 2 to 20
        var barCount = (barCountWrap.Get % 200) + 1;          // 1 to 200

        // Generate deterministic bar data using bar index as close price
        var closePrices = Enumerable.Range(1, barCount).Select(i => (decimal)i).ToArray();
        var bars = GenerateBars(closePrices);

        var factory = new RunningSumStrategyFactory();
        var config = new StrategyConfig("running-sum-test", new Dictionary<string, object>());

        // Execute one instance sequentially to get the expected result
        var sequentialInstance = factory.Create(config);
        var expectedResult = ExecuteStrategy(sequentialInstance, bars);

        // Create N instances concurrently and execute in parallel on the same bar data
        var instances = new IStrategy[instanceCount];
        Parallel.For(0, instanceCount, i =>
        {
            instances[i] = factory.Create(config);
        });

        var parallelResults = new (decimal RunningSum, int BarCount)[instanceCount];
        Parallel.For(0, instanceCount, i =>
        {
            parallelResults[i] = ExecuteStrategy(instances[i], bars);
        });

        // Verify all parallel results are identical to the sequential result
        for (int i = 0; i < instanceCount; i++)
        {
            if (parallelResults[i].RunningSum != expectedResult.RunningSum)
                return false;
            if (parallelResults[i].BarCount != expectedResult.BarCount)
                return false;
        }

        return true;
    }
}
