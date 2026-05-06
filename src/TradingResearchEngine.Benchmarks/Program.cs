using BenchmarkDotNet.Running;
using TradingResearchEngine.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(BacktestEngineBenchmarks).Assembly).Run(args);
