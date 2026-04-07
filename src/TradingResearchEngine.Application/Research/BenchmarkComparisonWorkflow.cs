using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>Options for benchmark comparison.</summary>
public sealed class BenchmarkOptions
{
    /// <summary>Initial cash for the benchmark buy-and-hold simulation.</summary>
    public decimal InitialCash { get; set; } = 100_000m;
}

/// <summary>
/// Compares a strategy's equity curve against a buy-and-hold benchmark on the same data.
/// Computes alpha, beta, information ratio, and tracking error.
/// </summary>
public sealed class BenchmarkComparisonWorkflow
    : IResearchWorkflow<BenchmarkOptions, BenchmarkComparisonResult>
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly IDataProviderFactory _dataProviderFactory;

    /// <inheritdoc cref="BenchmarkComparisonWorkflow"/>
    public BenchmarkComparisonWorkflow(
        RunScenarioUseCase runScenario, IDataProviderFactory dataProviderFactory)
    {
        _runScenario = runScenario;
        _dataProviderFactory = dataProviderFactory;
    }

    /// <inheritdoc/>
    public async Task<BenchmarkComparisonResult> RunAsync(
        ScenarioConfig baseConfig, BenchmarkOptions options, CancellationToken ct = default)
    {
        // Run the strategy
        var stratResult = await _runScenario.RunAsync(baseConfig, ct);
        if (!stratResult.IsSuccess || stratResult.Result is null)
            throw new InvalidOperationException("Strategy run failed.");

        // Build buy-and-hold benchmark equity curve from raw bar data
        var provider = _dataProviderFactory.Create(
            baseConfig.DataProviderType, baseConfig.DataProviderOptions);

        var dataOpts = baseConfig.DataProviderOptions;
        string symbol = dataOpts.TryGetValue("Symbol", out var s) ? s?.ToString() ?? "" : "";
        string interval = dataOpts.TryGetValue("Interval", out var iv) ? iv?.ToString() ?? "1D" : "1D";
        var from = dataOpts.TryGetValue("From", out var f) && f is DateTimeOffset df ? df : DateTimeOffset.MinValue;
        var to = dataOpts.TryGetValue("To", out var t) && t is DateTimeOffset dt ? dt : DateTimeOffset.MaxValue;

        var bars = new List<Core.DataHandling.BarRecord>();
        await foreach (var bar in provider.GetBars(symbol, interval, from, to, ct))
            bars.Add(bar);

        if (bars.Count == 0)
            throw new InvalidOperationException("No bar data available for benchmark.");

        // Buy-and-hold: buy at first close, track equity at each bar
        decimal shares = Math.Floor(options.InitialCash / bars[0].Close);
        decimal cash = options.InitialCash - shares * bars[0].Close;
        var benchCurve = bars.Select(b =>
            new EquityCurvePoint(b.Timestamp, cash + shares * b.Close)).ToList();

        decimal stratReturn = (stratResult.Result.EndEquity - stratResult.Result.StartEquity) / stratResult.Result.StartEquity;
        decimal benchReturn = (benchCurve[^1].TotalEquity - options.InitialCash) / options.InitialCash;
        decimal alpha = stratReturn - benchReturn;

        // Compute beta, tracking error, information ratio from period returns
        var stratReturns = ComputePeriodReturns(stratResult.Result.EquityCurve);
        var benchReturns = ComputePeriodReturns(benchCurve);
        int n = Math.Min(stratReturns.Count, benchReturns.Count);

        decimal? beta = null;
        decimal trackingError = 0m;
        decimal? informationRatio = null;

        if (n >= 2)
        {
            var diffs = new List<decimal>(n);
            decimal sumSB = 0, sumBB = 0;
            decimal meanB = benchReturns.Take(n).Average();
            decimal meanS = stratReturns.Take(n).Average();

            for (int i = 0; i < n; i++)
            {
                sumSB += (stratReturns[i] - meanS) * (benchReturns[i] - meanB);
                sumBB += (benchReturns[i] - meanB) * (benchReturns[i] - meanB);
                diffs.Add(stratReturns[i] - benchReturns[i]);
            }

            beta = sumBB != 0 ? sumSB / sumBB : null;

            decimal meanDiff = diffs.Average();
            decimal varDiff = diffs.Sum(d => (d - meanDiff) * (d - meanDiff)) / (n - 1);
            trackingError = (decimal)Math.Sqrt((double)varDiff);
            informationRatio = trackingError != 0 ? meanDiff / trackingError : null;
        }

        return new BenchmarkComparisonResult(
            stratReturn, benchReturn, alpha, beta,
            informationRatio, trackingError, benchCurve);
    }

    private static List<decimal> ComputePeriodReturns(IReadOnlyList<EquityCurvePoint> curve)
    {
        var returns = new List<decimal>();
        for (int i = 1; i < curve.Count; i++)
        {
            if (curve[i - 1].TotalEquity != 0)
                returns.Add((curve[i].TotalEquity - curve[i - 1].TotalEquity) / curve[i - 1].TotalEquity);
        }
        return returns;
    }
}
