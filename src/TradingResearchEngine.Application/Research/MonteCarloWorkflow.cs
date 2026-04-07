using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Bootstrap-resamples the closed-trade return sequence to produce a distribution of outcomes.
/// </summary>
public sealed class MonteCarloWorkflow : IResearchWorkflow<MonteCarloOptions, MonteCarloResult>
{
    private readonly RunScenarioUseCase _runScenario;

    /// <inheritdoc cref="MonteCarloWorkflow"/>
    public MonteCarloWorkflow(RunScenarioUseCase runScenario) => _runScenario = runScenario;

    /// <inheritdoc/>
    public async Task<MonteCarloResult> RunAsync(
        ScenarioConfig baseConfig, MonteCarloOptions options, CancellationToken ct = default)
    {
        if (options.SimulationCount < 1)
            throw new ArgumentException("SimulationCount must be >= 1.", nameof(options));

        var runResult = await _runScenario.RunAsync(baseConfig, ct);
        if (!runResult.IsSuccess || runResult.Result is null)
            throw new InvalidOperationException(
                "MonteCarloWorkflow: base scenario run failed. "
                + string.Join("; ", runResult.Errors ?? Array.Empty<string>()));

        return RunSimulation(runResult.Result, options, ct);
    }

    /// <summary>Runs Monte Carlo simulation on an existing backtest result's trade sequence.</summary>
    public MonteCarloResult RunAsync(
        BacktestResult sourceResult, MonteCarloOptions options, CancellationToken ct = default)
    {
        if (options.SimulationCount < 1)
            throw new ArgumentException("SimulationCount must be >= 1.", nameof(options));
        return RunSimulation(sourceResult, options, ct);
    }

    private static MonteCarloResult RunSimulation(
        BacktestResult sourceResult, MonteCarloOptions options, CancellationToken ct)
    {
        var trades = sourceResult.Trades;
        if (trades.Count == 0)
        {
            return new MonteCarloResult(
                sourceResult.EndEquity, sourceResult.EndEquity, sourceResult.EndEquity,
                0m, 0m, new List<decimal> { sourceResult.EndEquity }, 0, 0,
                new List<MonteCarloPath>(), new List<MonteCarloPercentileBand>());
        }

        var returns = trades.Select(t => t.NetPnl).ToArray();
        int tradeCount = returns.Length;
        var rng = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        var endEquities = new List<decimal>(options.SimulationCount);
        var maxDrawdowns = new List<decimal>(options.SimulationCount);
        var maxConsecLosses = new List<int>(options.SimulationCount);
        var maxConsecWins = new List<int>(options.SimulationCount);
        var allPaths = new List<MonteCarloPath>(options.SimulationCount);
        var ruinThreshold = sourceResult.StartEquity * (1m - options.RuinThresholdPercent);
        int ruinCount = 0;

        // Matrix for percentile band computation: [step][sim]
        var stepEquities = new decimal[tradeCount + 1][];
        for (int s = 0; s <= tradeCount; s++)
            stepEquities[s] = new decimal[options.SimulationCount];

        for (int sim = 0; sim < options.SimulationCount; sim++)
        {
            ct.ThrowIfCancellationRequested();

            decimal equity = sourceResult.StartEquity;
            decimal peak = equity;
            decimal maxDd = 0m;
            bool ruined = false;
            int consecLosses = 0, maxCL = 0;
            int consecWins = 0, maxCW = 0;
            var path = new decimal[tradeCount + 1];
            path[0] = equity;
            stepEquities[0][sim] = equity;

            for (int i = 0; i < tradeCount; i++)
            {
                int idx = rng.Next(tradeCount);
                decimal pnl = returns[idx];
                equity += pnl;
                path[i + 1] = equity;
                stepEquities[i + 1][sim] = equity;

                if (equity > peak) peak = equity;
                decimal dd = peak > 0 ? (peak - equity) / peak : 0m;
                if (dd > maxDd) maxDd = dd;
                if (!ruined && equity <= ruinThreshold) ruined = true;

                if (pnl < 0) { consecLosses++; consecWins = 0; if (consecLosses > maxCL) maxCL = consecLosses; }
                else if (pnl > 0) { consecWins++; consecLosses = 0; if (consecWins > maxCW) maxCW = consecWins; }
                else { consecLosses = 0; consecWins = 0; }
            }

            allPaths.Add(new MonteCarloPath(path));
            endEquities.Add(equity);
            maxDrawdowns.Add(maxDd);
            maxConsecLosses.Add(maxCL);
            maxConsecWins.Add(maxCW);
            if (ruined) ruinCount++;
        }

        // Compute percentile bands at each step
        var bands = new List<MonteCarloPercentileBand>(tradeCount + 1);
        for (int s = 0; s <= tradeCount; s++)
        {
            var sorted = stepEquities[s].OrderBy(v => v).ToArray();
            int n = sorted.Length;
            bands.Add(new MonteCarloPercentileBand(
                s,
                sorted[Math.Max(0, (int)(n * 0.10) - 1)],
                sorted[Math.Max(0, (int)(n * 0.50) - 1)],
                sorted[Math.Min((int)(n * 0.90), n - 1)]));
        }

        endEquities.Sort();
        maxDrawdowns.Sort();
        maxConsecLosses.Sort();
        maxConsecWins.Sort();

        int count = endEquities.Count;
        decimal p10 = endEquities[Math.Max(0, (int)(count * 0.10) - 1)];
        decimal p50 = endEquities[Math.Max(0, (int)(count * 0.50) - 1)];
        decimal p90 = endEquities[Math.Min((int)(count * 0.90), count - 1)];
        decimal ruinProb = (decimal)ruinCount / count;
        decimal medianDd = maxDrawdowns[Math.Max(0, (int)(maxDrawdowns.Count * 0.50) - 1)];
        int p90ConsecLosses = maxConsecLosses[Math.Min((int)(count * 0.90), count - 1)];
        int p90ConsecWins = maxConsecWins[Math.Min((int)(count * 0.90), count - 1)];

        return new MonteCarloResult(p10, p50, p90, ruinProb, medianDd, endEquities,
            p90ConsecLosses, p90ConsecWins, allPaths, bands);
    }
}
