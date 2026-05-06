using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Portfolio;

/// <summary>
/// Orchestrates parallel multi-symbol backtests and aggregates results
/// into a portfolio-level view with correlation analysis, equity curve merging,
/// and turnover computation.
/// </summary>
public sealed class PortfolioBacktestRunner
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly ILogger<PortfolioBacktestRunner> _logger;

    /// <summary>
    /// Initialises the portfolio backtest runner with the scenario use case and logger.
    /// </summary>
    /// <param name="runScenario">Use case for running individual symbol backtests.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public PortfolioBacktestRunner(
        RunScenarioUseCase runScenario,
        ILogger<PortfolioBacktestRunner> logger)
    {
        _runScenario = runScenario;
        _logger = logger;
    }

    /// <summary>
    /// Runs a portfolio backtest with parallel per-symbol execution.
    /// Each symbol is backtested independently, then results are aggregated
    /// into a portfolio-level view with merged equity curves, correlation matrix,
    /// and annualised turnover.
    /// </summary>
    /// <param name="config">Portfolio backtest configuration with symbols, strategies, and risk settings.</param>
    /// <param name="progress">Progress reporter for long-running operation feedback.</param>
    /// <param name="ct">Cancellation token propagated to all per-symbol runs.</param>
    /// <returns>Aggregated portfolio backtest result.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when strategy count does not match symbol count (unless exactly one strategy is provided).
    /// </exception>
    public async Task<PortfolioBacktestResult> RunAsync(
        PortfolioBacktestConfig config,
        IProgressReporter progress,
        CancellationToken ct)
    {
        ValidateConfig(config);

        int symbolCount = config.Symbols.Count;
        var symbolResults = new BacktestResult[symbolCount];
        int completed = 0;

        int maxParallelism = Math.Max(1, Environment.ProcessorCount - 1);

        await Parallel.ForEachAsync(
            Enumerable.Range(0, symbolCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = ct
            },
            async (index, token) =>
            {
                var scenarioConfig = BuildScenarioConfig(config, index);
                var result = await _runScenario.RunAsync(scenarioConfig, token, autoSave: false);

                if (!result.IsSuccess || result.Result is null)
                {
                    var errors = result.Errors ?? Array.Empty<string>();
                    throw new InvalidOperationException(
                        $"Symbol {index} backtest failed: {string.Join("; ", errors)}");
                }

                symbolResults[index] = result.Result;

                int done = Interlocked.Increment(ref completed);
                progress.Report(done, symbolCount, $"Completed symbol {done}/{symbolCount}");
            });

        ct.ThrowIfCancellationRequested();

        // Merge equity curves based on rebalance mode
        var rebalanceMode = config.PortfolioRisk.RebalanceMode;
        var mergedCurve = MergeEquityCurves(symbolResults, rebalanceMode, config.InitialCash);

        // Compute correlation matrix from daily return series
        var correlationMatrix = ComputeCorrelationMatrix(symbolResults);

        // Compute annualised turnover
        var annualisedTurnover = ComputeTurnover(symbolResults);

        // Build portfolio-level BacktestResult from merged curve
        var portfolioResult = BuildPortfolioResult(config, mergedCurve, symbolResults);

        return new PortfolioBacktestResult(
            symbolResults.ToList().AsReadOnly(),
            portfolioResult,
            correlationMatrix,
            annualisedTurnover,
            rebalanceMode);
    }

    /// <summary>
    /// Validates the portfolio backtest configuration.
    /// </summary>
    private static void ValidateConfig(PortfolioBacktestConfig config)
    {
        if (config.Symbols.Count == 0)
            throw new InvalidOperationException("Portfolio must contain at least one symbol.");

        if (config.Strategies.Count != 1 && config.Strategies.Count != config.Symbols.Count)
            throw new InvalidOperationException(
                $"Strategy count must be 1 (applied to all symbols) or equal to symbol count ({config.Symbols.Count}). Got {config.Strategies.Count}.");
    }

    /// <summary>
    /// Builds a <see cref="ScenarioConfig"/> for a single symbol from the portfolio configuration.
    /// </summary>
    private static ScenarioConfig BuildScenarioConfig(PortfolioBacktestConfig config, int symbolIndex)
    {
        var dataConfig = config.Symbols[symbolIndex];
        var strategyConfig = config.Strategies.Count == 1
            ? config.Strategies[0]
            : config.Strategies[symbolIndex];

        return new ScenarioConfig(
            ScenarioId: $"portfolio-symbol-{symbolIndex}",
            Description: $"Portfolio backtest symbol {symbolIndex}",
            ReplayMode: Core.Engine.ReplayMode.Bar,
            DataProviderType: dataConfig.DataProviderType,
            DataProviderOptions: dataConfig.DataProviderOptions,
            StrategyType: strategyConfig.StrategyType,
            StrategyParameters: strategyConfig.StrategyParameters,
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: config.Execution.SlippageModelType,
            CommissionModelType: config.Execution.CommissionModelType,
            InitialCash: config.InitialCash / config.Symbols.Count,
            AnnualRiskFreeRate: 0m,
            RandomSeed: config.Seed,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null,
            FillMode: config.Execution.FillMode,
            BarsPerYear: dataConfig.BarsPerYear,
            RealismProfile: config.Execution.RealismProfile,
            Timeframe: config.Timeframe ?? dataConfig.Timeframe,
            Data: dataConfig,
            Strategy: strategyConfig,
            Execution: config.Execution);
    }

    /// <summary>
    /// Merges per-symbol equity curves based on the specified rebalance mode.
    /// </summary>
    /// <param name="results">Per-symbol backtest results.</param>
    /// <param name="mode">Rebalance mode determining merge strategy.</param>
    /// <param name="initialCash">Total portfolio initial cash.</param>
    /// <returns>Merged equity curve points aligned by timestamp.</returns>
    internal static IReadOnlyList<EquityCurvePoint> MergeEquityCurves(
        BacktestResult[] results,
        PortfolioRebalanceMode mode,
        decimal initialCash)
    {
        if (results.Length == 0)
            return Array.Empty<EquityCurvePoint>();

        if (results.Length == 1)
            return results[0].EquityCurve;

        // Collect all unique timestamps across all symbols, sorted
        var allTimestamps = results
            .SelectMany(r => r.EquityCurve.Select(p => p.Timestamp))
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        if (allTimestamps.Count == 0)
            return Array.Empty<EquityCurvePoint>();

        int symbolCount = results.Length;
        decimal[] weights = ComputeWeights(results, mode, symbolCount);

        var merged = new List<EquityCurvePoint>(allTimestamps.Count);

        // Build lookup dictionaries for each symbol's equity curve by timestamp
        var curveByTimestamp = results
            .Select(r => r.EquityCurve.ToDictionary(p => p.Timestamp, p => p))
            .ToArray();

        foreach (var timestamp in allTimestamps)
        {
            decimal totalEquity = 0m;

            for (int i = 0; i < symbolCount; i++)
            {
                if (curveByTimestamp[i].TryGetValue(timestamp, out var point))
                {
                    // Scale the equity by the weight
                    decimal symbolEquity = point.TotalEquity;
                    decimal scaledEquity = mode switch
                    {
                        PortfolioRebalanceMode.EqualWeight =>
                            symbolEquity * (initialCash / symbolCount) / results[i].StartEquity,
                        PortfolioRebalanceMode.VolatilityParity =>
                            symbolEquity * weights[i] * initialCash / results[i].StartEquity,
                        _ => symbolEquity // None: simple sum
                    };
                    totalEquity += scaledEquity;
                }
            }

            merged.Add(new EquityCurvePoint(timestamp, totalEquity));
        }

        return merged.AsReadOnly();
    }

    /// <summary>
    /// Computes weights for each symbol based on the rebalance mode.
    /// </summary>
    private static decimal[] ComputeWeights(
        BacktestResult[] results,
        PortfolioRebalanceMode mode,
        int symbolCount)
    {
        var weights = new decimal[symbolCount];

        switch (mode)
        {
            case PortfolioRebalanceMode.EqualWeight:
                decimal equalWeight = 1m / symbolCount;
                for (int i = 0; i < symbolCount; i++)
                    weights[i] = equalWeight;
                break;

            case PortfolioRebalanceMode.VolatilityParity:
                var inverseVols = new decimal[symbolCount];
                decimal sumInverseVol = 0m;

                for (int i = 0; i < symbolCount; i++)
                {
                    decimal vol = ComputeAnnualisedVolatility(results[i]);
                    // Avoid division by zero: use a small floor
                    decimal invVol = vol > 0m ? 1m / vol : 1m;
                    inverseVols[i] = invVol;
                    sumInverseVol += invVol;
                }

                for (int i = 0; i < symbolCount; i++)
                    weights[i] = sumInverseVol > 0m ? inverseVols[i] / sumInverseVol : 1m / symbolCount;
                break;

            default: // None
                for (int i = 0; i < symbolCount; i++)
                    weights[i] = 1m;
                break;
        }

        return weights;
    }

    /// <summary>
    /// Computes annualised volatility (standard deviation of returns × √BarsPerYear) for a symbol.
    /// </summary>
    private static decimal ComputeAnnualisedVolatility(BacktestResult result)
    {
        var curve = result.EquityCurve;
        if (curve.Count < 2) return 0m;

        var returns = new List<decimal>(curve.Count - 1);
        for (int i = 1; i < curve.Count; i++)
        {
            decimal prev = curve[i - 1].TotalEquity;
            if (prev > 0m)
                returns.Add((curve[i].TotalEquity - prev) / prev);
        }

        if (returns.Count < 2) return 0m;

        decimal mean = returns.Average();
        decimal variance = returns.Sum(r => (r - mean) * (r - mean)) / (returns.Count - 1);
        decimal stdDev = (decimal)Math.Sqrt((double)variance);

        int barsPerYear = result.ScenarioConfig.BarsPerYear;
        return stdDev * (decimal)Math.Sqrt(barsPerYear);
    }

    /// <summary>
    /// Computes the N×N Pearson correlation matrix from daily return series
    /// derived from each symbol's equity curve.
    /// </summary>
    /// <param name="results">Per-symbol backtest results.</param>
    /// <returns>Symmetric correlation matrix with diagonal = 1.0.</returns>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> ComputeCorrelationMatrix(
        BacktestResult[] results)
    {
        int n = results.Length;
        var symbols = new string[n];
        var returnSeries = new double[n][];

        for (int i = 0; i < n; i++)
        {
            symbols[i] = GetSymbolName(results[i], i);
            returnSeries[i] = GetDailyReturns(results[i].EquityCurve);
        }

        var matrix = new Dictionary<string, IReadOnlyDictionary<string, double>>(n);

        for (int i = 0; i < n; i++)
        {
            var row = new Dictionary<string, double>(n);
            for (int j = 0; j < n; j++)
            {
                row[symbols[j]] = i == j ? 1.0 : PearsonCorrelation(returnSeries[i], returnSeries[j]);
            }
            matrix[symbols[i]] = row;
        }

        return matrix;
    }

    /// <summary>
    /// Computes Pearson correlation coefficient between two return series.
    /// Uses the standard formula: r = Σ((xi - x̄)(yi - ȳ)) / √(Σ(xi - x̄)² × Σ(yi - ȳ)²).
    /// Returns 0.0 when series are empty or have zero variance.
    /// </summary>
    private static double PearsonCorrelation(double[] x, double[] y)
    {
        int length = Math.Min(x.Length, y.Length);
        if (length < 2) return 0.0;

        double sumX = 0, sumY = 0;
        for (int i = 0; i < length; i++)
        {
            sumX += x[i];
            sumY += y[i];
        }

        double meanX = sumX / length;
        double meanY = sumY / length;

        double cov = 0, varX = 0, varY = 0;
        for (int i = 0; i < length; i++)
        {
            double dx = x[i] - meanX;
            double dy = y[i] - meanY;
            cov += dx * dy;
            varX += dx * dx;
            varY += dy * dy;
        }

        double denominator = Math.Sqrt(varX * varY);
        return denominator > 0 ? cov / denominator : 0.0;
    }

    /// <summary>
    /// Extracts daily return series from an equity curve.
    /// </summary>
    private static double[] GetDailyReturns(IReadOnlyList<EquityCurvePoint> curve)
    {
        if (curve.Count < 2) return Array.Empty<double>();

        var returns = new double[curve.Count - 1];
        for (int i = 1; i < curve.Count; i++)
        {
            double prev = (double)curve[i - 1].TotalEquity;
            if (prev > 0)
                returns[i - 1] = ((double)curve[i].TotalEquity - prev) / prev;
        }
        return returns;
    }

    /// <summary>
    /// Computes annualised turnover from all symbol trades.
    /// Formula: (TotalPositionChanges / MonthsInBacktest) × 12.
    /// </summary>
    /// <param name="results">Per-symbol backtest results.</param>
    /// <returns>Annualised turnover as a decimal.</returns>
    internal static decimal ComputeTurnover(BacktestResult[] results)
    {
        if (results.Length == 0) return 0m;

        int totalPositionChanges = results.Sum(r => r.Trades.Count * 2); // entry + exit = 2 changes per trade

        // Determine the backtest duration in months
        var allTimestamps = results
            .SelectMany(r => r.EquityCurve.Select(p => p.Timestamp))
            .ToList();

        if (allTimestamps.Count < 2) return 0m;

        var earliest = allTimestamps.Min();
        var latest = allTimestamps.Max();
        double totalDays = (latest - earliest).TotalDays;
        decimal months = (decimal)totalDays / 30.44m; // average days per month

        if (months <= 0m) return 0m;

        return (totalPositionChanges / months) * 12m;
    }

    /// <summary>
    /// Extracts a symbol name from the backtest result's scenario config or trades.
    /// </summary>
    private static string GetSymbolName(BacktestResult result, int index)
    {
        // Try to get symbol from trades
        if (result.Trades.Count > 0)
            return result.Trades[0].Symbol;

        // Try from data provider options
        if (result.ScenarioConfig.DataProviderOptions.TryGetValue("Symbol", out var sym) && sym is string s)
            return s;

        return $"Symbol_{index}";
    }

    /// <summary>
    /// Builds a portfolio-level <see cref="BacktestResult"/> from the merged equity curve.
    /// </summary>
    private static BacktestResult BuildPortfolioResult(
        PortfolioBacktestConfig config,
        IReadOnlyList<EquityCurvePoint> mergedCurve,
        BacktestResult[] symbolResults)
    {
        var allTrades = symbolResults.SelectMany(r => r.Trades).ToList().AsReadOnly();
        decimal startEquity = config.InitialCash;
        decimal endEquity = mergedCurve.Count > 0 ? mergedCurve[^1].TotalEquity : startEquity;

        // Use the first symbol's BarsPerYear as representative
        int barsPerYear = symbolResults.Length > 0
            ? symbolResults[0].ScenarioConfig.BarsPerYear
            : 252;

        var portfolioConfig = new ScenarioConfig(
            ScenarioId: "portfolio-aggregate",
            Description: "Portfolio-level aggregated result",
            ReplayMode: Core.Engine.ReplayMode.Bar,
            DataProviderType: "portfolio",
            DataProviderOptions: new Dictionary<string, object>(),
            StrategyType: "portfolio",
            StrategyParameters: new Dictionary<string, object>(),
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: config.Execution.SlippageModelType,
            CommissionModelType: config.Execution.CommissionModelType,
            InitialCash: config.InitialCash,
            AnnualRiskFreeRate: 0m,
            RandomSeed: config.Seed,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null,
            BarsPerYear: barsPerYear);

        return new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: portfolioConfig,
            Status: BacktestStatus.Completed,
            EquityCurve: mergedCurve,
            Trades: allTrades,
            StartEquity: startEquity,
            EndEquity: endEquity,
            MaxDrawdown: MetricsCalculator.ComputeMaxDrawdown(mergedCurve),
            SharpeRatio: MetricsCalculator.ComputeSharpeRatio(mergedCurve, 0m, barsPerYear),
            SortinoRatio: MetricsCalculator.ComputeSortinoRatio(mergedCurve, 0m, barsPerYear),
            CalmarRatio: MetricsCalculator.ComputeCalmarRatio(mergedCurve, startEquity, endEquity),
            ReturnOnMaxDrawdown: MetricsCalculator.ComputeReturnOnMaxDrawdown(mergedCurve, startEquity, endEquity),
            TotalTrades: allTrades.Count,
            WinRate: MetricsCalculator.ComputeWinRate(allTrades),
            ProfitFactor: MetricsCalculator.ComputeProfitFactor(allTrades),
            AverageWin: MetricsCalculator.ComputeAverageWin(allTrades),
            AverageLoss: MetricsCalculator.ComputeAverageLoss(allTrades),
            Expectancy: MetricsCalculator.ComputeExpectancy(allTrades),
            AverageHoldingPeriod: MetricsCalculator.ComputeAverageHoldingPeriod(allTrades),
            EquityCurveSmoothness: MetricsCalculator.ComputeEquityCurveSmoothness(mergedCurve),
            MaxConsecutiveLosses: MetricsCalculator.ComputeMaxConsecutiveLosses(allTrades),
            MaxConsecutiveWins: MetricsCalculator.ComputeMaxConsecutiveWins(allTrades),
            RunDurationMs: symbolResults.Sum(r => r.RunDurationMs),
            RecoveryFactor: MetricsCalculator.ComputeRecoveryFactor(mergedCurve, startEquity, endEquity));
    }
}
