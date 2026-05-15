using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Bootstrap-resamples the closed-trade return sequence to produce a distribution of outcomes.
/// Supports parallel execution with deterministic seeding via <see cref="ConcurrencyBudget"/>.
/// </summary>
public sealed class MonteCarloWorkflow : IResearchWorkflow<MonteCarloOptions, MonteCarloResult>
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly ConcurrencyBudget? _concurrencyBudget;

    /// <summary>
    /// Initializes a new <see cref="MonteCarloWorkflow"/> with scenario execution and concurrency control.
    /// </summary>
    /// <param name="runScenario">Use case for running the base scenario.</param>
    /// <param name="concurrencyBudget">Global concurrency budget for bounded parallelism.</param>
    public MonteCarloWorkflow(RunScenarioUseCase runScenario, ConcurrencyBudget concurrencyBudget)
    {
        _runScenario = runScenario;
        _concurrencyBudget = concurrencyBudget;
    }

    /// <summary>
    /// Initializes a new <see cref="MonteCarloWorkflow"/> without concurrency control (sequential execution).
    /// Primarily used for testing.
    /// </summary>
    /// <param name="runScenario">Use case for running the base scenario.</param>
    public MonteCarloWorkflow(RunScenarioUseCase runScenario)
    {
        _runScenario = runScenario;
        _concurrencyBudget = null;
    }

    /// <inheritdoc/>
    public async Task<MonteCarloResult> RunAsync(
        ScenarioConfig baseConfig, MonteCarloOptions options, CancellationToken ct = default)
    {
        if (options.SimulationCount < 1)
            throw new ArgumentException("SimulationCount must be >= 1.", nameof(options));

        var runResult = await _runScenario.RunAsync(baseConfig, ct, autoSave: false);
        if (!runResult.IsSuccess || runResult.Result is null)
            throw new InvalidOperationException(
                "MonteCarloWorkflow: base scenario run failed. "
                + string.Join("; ", runResult.Errors ?? Array.Empty<string>()));

        return await RunSimulationAsync(runResult.Result, options, ct);
    }

    /// <inheritdoc/>
    public async Task<MonteCarloResult> RunAsync(
        ScenarioConfig baseConfig, MonteCarloOptions options,
        IProgress<ProgressUpdate>? progress, CancellationToken ct = default)
    {
        if (options.SimulationCount < 1)
            throw new ArgumentException("SimulationCount must be >= 1.", nameof(options));

        var runResult = await _runScenario.RunAsync(baseConfig, ct, autoSave: false);
        if (!runResult.IsSuccess || runResult.Result is null)
            throw new InvalidOperationException(
                "MonteCarloWorkflow: base scenario run failed. "
                + string.Join("; ", runResult.Errors ?? Array.Empty<string>()));

        return await RunSimulationAsync(runResult.Result, options, ct, progress);
    }

    /// <summary>Runs Monte Carlo simulation on an existing backtest result's trade sequence.</summary>
    public MonteCarloResult RunAsync(
        BacktestResult sourceResult, MonteCarloOptions options, CancellationToken ct = default)
    {
        if (options.SimulationCount < 1)
            throw new ArgumentException("SimulationCount must be >= 1.", nameof(options));
        return RunSimulationSequential(sourceResult, options, ct);
    }

    /// <summary>Runs Monte Carlo simulation on an existing backtest result's trade sequence with progress reporting.</summary>
    public MonteCarloResult RunAsync(
        BacktestResult sourceResult, MonteCarloOptions options,
        CancellationToken ct, IProgress<ProgressUpdate>? progress)
    {
        if (options.SimulationCount < 1)
            throw new ArgumentException("SimulationCount must be >= 1.", nameof(options));
        return RunSimulationSequential(sourceResult, options, ct, progress);
    }

    /// <summary>
    /// Runs Monte Carlo simulation with parallel execution when a concurrency budget is available.
    /// Falls back to sequential execution otherwise.
    /// </summary>
    private async Task<MonteCarloResult> RunSimulationAsync(
        BacktestResult sourceResult, MonteCarloOptions options, CancellationToken ct,
        IProgress<ProgressUpdate>? progress = null)
    {
        if (_concurrencyBudget is null)
            return RunSimulationSequential(sourceResult, options, ct, progress);

        return await RunSimulationParallel(sourceResult, options, ct, progress);
    }

    /// <summary>
    /// Parallel Monte Carlo simulation with deterministic seeding.
    /// Pre-generates per-iteration seeds sequentially from the master RNG,
    /// then dispatches simulations via <see cref="Parallel.ForEachAsync{TSource}"/>
    /// with bounded concurrency from <see cref="ConcurrencyBudget"/>.
    /// </summary>
    private async Task<MonteCarloResult> RunSimulationParallel(
        BacktestResult sourceResult, MonteCarloOptions options, CancellationToken ct,
        IProgress<ProgressUpdate>? progress)
    {
        var trades = sourceResult.Trades;
        if (trades.Count == 0)
        {
            return new MonteCarloResult(
                sourceResult.EndEquity, sourceResult.EndEquity, sourceResult.EndEquity,
                0m, 0m, new List<decimal> { sourceResult.EndEquity }, 0, 0,
                new List<MonteCarloPath>(), new List<MonteCarloPercentileBand>());
        }

        var returns = trades.Select(t => t.ReturnOnRisk).ToArray();
        int tradeCount = returns.Length;
        int simulationCount = options.SimulationCount;

        // 1. Pre-generate per-iteration seeds sequentially from master RNG (deterministic)
        var masterRng = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();
        var seeds = new int[simulationCount];
        for (int i = 0; i < simulationCount; i++)
            seeds[i] = masterRng.Next();

        // Clamp BlockSize to tradeCount when it exceeds the number of trades
        int effectiveBlockSize = Math.Min(Math.Max(options.BlockSize, 1), tradeCount);
        var ruinThreshold = sourceResult.StartEquity * (1m - options.RuinThresholdPercent);

        // 2. Pre-allocate indexed arrays for results
        var allPaths = new MonteCarloPath[simulationCount];
        var endEquities = new decimal[simulationCount];
        var maxDrawdowns = new decimal[simulationCount];
        var maxConsecLosses = new int[simulationCount];
        var maxConsecWins = new int[simulationCount];
        var ruinFlags = new int[simulationCount]; // 1 = ruined, 0 = not

        // Matrix for percentile band computation: [step][sim]
        var stepEquities = new decimal[tradeCount + 1][];
        for (int s = 0; s <= tradeCount; s++)
            stepEquities[s] = new decimal[simulationCount];

        // Progress tracking
        int completedCount = 0;
        int progressInterval = Math.Max(1, simulationCount / 100);

        // 3. Dispatch simulations via Parallel.ForEachAsync with ConcurrencyBudget
        await Parallel.ForEachAsync(
            Enumerable.Range(0, simulationCount),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _concurrencyBudget!.Available,
                CancellationToken = ct
            },
            async (sim, token) =>
            {
                using var permit = await _concurrencyBudget.AcquireAsync(token);

                // Each iteration uses its own deterministic RNG seeded from the pre-generated seed
                var rng = new Random(seeds[sim]);

                decimal equity = sourceResult.StartEquity;
                decimal peak = equity;
                decimal maxDd = 0m;
                bool ruined = false;
                int consecLosses = 0, maxCL = 0;
                int consecWins = 0, maxCW = 0;
                var path = new decimal[tradeCount + 1];
                path[0] = equity;
                stepEquities[0][sim] = equity;
                int blockStart = 0;

                for (int i = 0; i < tradeCount; i++)
                {
                    int idx;
                    if (effectiveBlockSize <= 1)
                    {
                        // IID bootstrap
                        idx = rng.Next(tradeCount);
                    }
                    else
                    {
                        // Block bootstrap: pick a new block start every effectiveBlockSize trades
                        if (i % effectiveBlockSize == 0)
                            blockStart = rng.Next(tradeCount);
                        idx = (blockStart + (i % effectiveBlockSize)) % tradeCount;
                    }

                    decimal sampledReturn = returns[idx];
                    equity *= (1m + sampledReturn);
                    path[i + 1] = equity;
                    stepEquities[i + 1][sim] = equity;

                    if (equity > peak) peak = equity;
                    decimal dd = peak > 0 ? (peak - equity) / peak : 0m;
                    if (dd > maxDd) maxDd = dd;
                    if (!ruined && equity <= ruinThreshold) ruined = true;

                    if (sampledReturn < 0) { consecLosses++; consecWins = 0; if (consecLosses > maxCL) maxCL = consecLosses; }
                    else if (sampledReturn > 0) { consecWins++; consecLosses = 0; if (consecWins > maxCW) maxCW = consecWins; }
                    else { consecLosses = 0; consecWins = 0; }
                }

                // Write results into pre-allocated indexed positions (no contention — each sim writes its own index)
                allPaths[sim] = new MonteCarloPath(path);
                endEquities[sim] = equity;
                maxDrawdowns[sim] = maxDd;
                maxConsecLosses[sim] = maxCL;
                maxConsecWins[sim] = maxCW;
                ruinFlags[sim] = ruined ? 1 : 0;

                // Report progress at intervals using thread-safe counter
                int completed = Interlocked.Increment(ref completedCount);
                if (progress is not null && completed % progressInterval == 0)
                {
                    progress.Report(new ProgressUpdate(completed, simulationCount,
                        $"Simulating path {completed} of {simulationCount}"));
                }
            });

        // 4. Aggregate results (order-independent — indexed array preserves deterministic ordering)
        int ruinCount = ruinFlags.Sum();

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

        var sortedEndEquities = endEquities.OrderBy(v => v).ToList();
        var sortedMaxDrawdowns = maxDrawdowns.OrderBy(v => v).ToList();
        var sortedMaxConsecLosses = maxConsecLosses.OrderBy(v => v).ToList();
        var sortedMaxConsecWins = maxConsecWins.OrderBy(v => v).ToList();

        int count = sortedEndEquities.Count;
        decimal p10 = sortedEndEquities[Math.Max(0, (int)(count * 0.10) - 1)];
        decimal p50 = sortedEndEquities[Math.Max(0, (int)(count * 0.50) - 1)];
        decimal p90 = sortedEndEquities[Math.Min((int)(count * 0.90), count - 1)];
        decimal ruinProb = (decimal)ruinCount / count;
        decimal medianDd = sortedMaxDrawdowns[Math.Max(0, (int)(sortedMaxDrawdowns.Count * 0.50) - 1)];
        int p90ConsecLosses = sortedMaxConsecLosses[Math.Min((int)(count * 0.90), count - 1)];
        int p90ConsecWins = sortedMaxConsecWins[Math.Min((int)(count * 0.90), count - 1)];

        // Final progress report
        progress?.Report(new ProgressUpdate(simulationCount, simulationCount,
            $"Completed {simulationCount} simulations"));

        return new MonteCarloResult(p10, p50, p90, ruinProb, medianDd, sortedEndEquities,
            p90ConsecLosses, p90ConsecWins, allPaths.ToList(), bands);
    }

    /// <summary>
    /// Sequential Monte Carlo simulation preserving original algorithm behavior.
    /// Used when no concurrency budget is available or for direct BacktestResult overloads.
    /// </summary>
    private static MonteCarloResult RunSimulationSequential(
        BacktestResult sourceResult, MonteCarloOptions options, CancellationToken ct,
        IProgress<ProgressUpdate>? progress = null)
    {
        var trades = sourceResult.Trades;
        if (trades.Count == 0)
        {
            return new MonteCarloResult(
                sourceResult.EndEquity, sourceResult.EndEquity, sourceResult.EndEquity,
                0m, 0m, new List<decimal> { sourceResult.EndEquity }, 0, 0,
                new List<MonteCarloPath>(), new List<MonteCarloPercentileBand>());
        }

        var returns = trades.Select(t => t.ReturnOnRisk).ToArray();
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

        // Block bootstrap resampling:
        // When BlockSize == 1 (default), this is standard IID bootstrap — each trade is sampled independently.
        // When BlockSize > 1, contiguous blocks of trades are sampled together to preserve serial
        // autocorrelation in the return sequence. This is important for trend-following strategies
        // where consecutive trade outcomes are correlated. The block start is randomized every
        // `effectiveBlockSize` trades, and indices wrap around using modular arithmetic.
        // Clamp BlockSize to tradeCount when it exceeds the number of trades
        int effectiveBlockSize = Math.Min(Math.Max(options.BlockSize, 1), tradeCount);

        // Progress reporting interval: emit ~100 updates per run
        int progressInterval = Math.Max(1, options.SimulationCount / 100);

        for (int sim = 0; sim < options.SimulationCount; sim++)
        {
            ct.ThrowIfCancellationRequested();

            // Emit progress at regular intervals
            if (progress is not null && sim % progressInterval == 0)
            {
                progress.Report(new ProgressUpdate(sim, options.SimulationCount,
                    $"Simulating path {sim + 1} of {options.SimulationCount}"));
            }

            decimal equity = sourceResult.StartEquity;
            decimal peak = equity;
            decimal maxDd = 0m;
            bool ruined = false;
            int consecLosses = 0, maxCL = 0;
            int consecWins = 0, maxCW = 0;
            var path = new decimal[tradeCount + 1];
            path[0] = equity;
            stepEquities[0][sim] = equity;
            int blockStart = 0;

            for (int i = 0; i < tradeCount; i++)
            {
                int idx;
                if (effectiveBlockSize <= 1)
                {
                    // IID bootstrap: identical RNG call sequence to V5.0
                    idx = rng.Next(tradeCount);
                }
                else
                {
                    // Block bootstrap: pick a new block start every effectiveBlockSize trades
                    if (i % effectiveBlockSize == 0)
                        blockStart = rng.Next(tradeCount);
                    idx = (blockStart + (i % effectiveBlockSize)) % tradeCount;
                }

                decimal sampledReturn = returns[idx];
                equity *= (1m + sampledReturn);
                path[i + 1] = equity;
                stepEquities[i + 1][sim] = equity;

                if (equity > peak) peak = equity;
                decimal dd = peak > 0 ? (peak - equity) / peak : 0m;
                if (dd > maxDd) maxDd = dd;
                if (!ruined && equity <= ruinThreshold) ruined = true;

                if (sampledReturn < 0) { consecLosses++; consecWins = 0; if (consecLosses > maxCL) maxCL = consecLosses; }
                else if (sampledReturn > 0) { consecWins++; consecLosses = 0; if (consecWins > maxCW) maxCW = consecWins; }
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

        // Final progress report
        progress?.Report(new ProgressUpdate(options.SimulationCount, options.SimulationCount,
            $"Completed {options.SimulationCount} simulations"));

        return new MonteCarloResult(p10, p50, p90, ruinProb, medianDd, endEquities,
            p90ConsecLosses, p90ConsecWins, allPaths, bands);
    }
}
