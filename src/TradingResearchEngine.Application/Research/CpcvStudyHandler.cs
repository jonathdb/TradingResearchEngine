using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Combinatorial Purged Cross-Validation (De Prado, 2018).
/// Generates C(N,k) train/test combinations across N folds and computes
/// the Probability of Backtest Overfitting (PBO).
/// Fold evaluations execute in parallel using <see cref="ConcurrencyBudget"/> for bounded concurrency.
/// </summary>
public sealed class CpcvStudyHandler : IResearchWorkflow<CpcvOptions, CpcvResult>
{
    private readonly RunScenarioUseCase _runScenario;
    private readonly ConcurrencyBudget _concurrencyBudget;

    /// <inheritdoc cref="CpcvStudyHandler"/>
    public CpcvStudyHandler(RunScenarioUseCase runScenario, ConcurrencyBudget concurrencyBudget)
    {
        _runScenario = runScenario;
        _concurrencyBudget = concurrencyBudget;
    }

    /// <inheritdoc/>
    public async Task<CpcvResult> RunAsync(
        ScenarioConfig baseConfig, CpcvOptions options, CancellationToken ct = default)
    {
        return await RunAsync(baseConfig, options, progress: null, ct);
    }

    /// <inheritdoc/>
    public async Task<CpcvResult> RunAsync(
        ScenarioConfig baseConfig, CpcvOptions options,
        IProgress<ProgressUpdate>? progress, CancellationToken ct = default)
    {
        ValidateOptions(options);

        // Parse data range from config using typed property access
#pragma warning disable CS0618 // Legacy dictionary access for backward compatibility
        var dataOpts = baseConfig.DataProviderOptions;
#pragma warning restore CS0618
        var dataFrom = dataOpts.GetFrom();
        var dataTo = dataOpts.GetTo();
        if (dataFrom == DateTimeOffset.MinValue)
            throw new InvalidOperationException("CPCV requires a 'From' date in DataProviderOptions.");
        if (dataTo == DateTimeOffset.MaxValue)
            throw new InvalidOperationException("CPCV requires a 'To' date in DataProviderOptions.");

        // Step 1: Split data into N equal-length folds
        var totalDuration = dataTo - dataFrom;
        var foldDuration = TimeSpan.FromTicks(totalDuration.Ticks / options.NumPaths);

        // Validate minimum bars per fold
        var barsPerYear = baseConfig.BarsPerYear;
        double yearsPerFold = foldDuration.TotalDays / 365.25;
        int estimatedBarsPerFold = (int)(yearsPerFold * barsPerYear);
        if (estimatedBarsPerFold < 30)
            throw new InvalidOperationException(
                $"Each fold has approximately {estimatedBarsPerFold} bars, which is below the minimum of 30. " +
                $"Increase the data range or reduce NumPaths (currently {options.NumPaths}).");

        var folds = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        for (int i = 0; i < options.NumPaths; i++)
        {
            var start = dataFrom + TimeSpan.FromTicks(foldDuration.Ticks * i);
            var end = i == options.NumPaths - 1 ? dataTo : dataFrom + TimeSpan.FromTicks(foldDuration.Ticks * (i + 1));
            folds.Add((start, end));
        }

        // Step 2: Generate all C(N, k) combinations
        var combinations = GenerateCombinations(options.NumPaths, options.TestFolds);
        int totalCombinations = combinations.Count;

        // Step 3: Run each combination in parallel with bounded concurrency.
        // Each fold evaluation creates its own engine instance via RunScenarioUseCase
        // (which creates a per-run service scope), ensuring no shared mutable state.
        var results = new (decimal IsSharpe, decimal OosSharpe)[totalCombinations];
        int completedCombinations = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalCombinations),
            new ParallelOptions
            {
                CancellationToken = ct
            },
            async (comboIndex, token) =>
            {
                using var permit = await _concurrencyBudget.AcquireAsync(token);

                var testIndices = combinations[comboIndex];
                var trainIndices = Enumerable.Range(0, options.NumPaths)
                    .Where(i => !testIndices.Contains(i))
                    .ToList();

                // Train: run engine on concatenated training folds → this combination's IS Sharpe
                var trainConfig = BuildConfigForFolds(baseConfig, folds, trainIndices);
                var trainResult = await _runScenario.RunAsync(trainConfig, token, autoSave: false);
                decimal comboIsSharpe = trainResult.Result?.SharpeRatio ?? 0m;

                // Test: run engine on concatenated test folds → this combination's OOS Sharpe
                var testConfig = BuildConfigForFolds(baseConfig, folds, testIndices.ToList());
                var testResult = await _runScenario.RunAsync(testConfig, token, autoSave: false);
                decimal comboOosSharpe = testResult.Result?.SharpeRatio ?? 0m;

                // Store in indexed position — no shared mutable state between folds
                results[comboIndex] = (comboIsSharpe, comboOosSharpe);

                // Thread-safe progress reporting via Interlocked
                int completed = Interlocked.Increment(ref completedCombinations);
                progress?.Report(new ProgressUpdate(completed, totalCombinations,
                    $"Completed combination {completed} of {totalCombinations}"));
            });

        // Step 4: Aggregate results after all folds complete (order-independent)
        var oosDistribution = new List<decimal>(totalCombinations);
        var isDistribution = new List<decimal>(totalCombinations);
        int overfitCount = 0;

        for (int i = 0; i < totalCombinations; i++)
        {
            var (isSharpe, oosSharpe) = results[i];
            isDistribution.Add(isSharpe);
            oosDistribution.Add(oosSharpe);

            if (oosSharpe < isSharpe)
                overfitCount++;
        }

        decimal medianOos = Median(oosDistribution);
        decimal medianIs = Median(isDistribution);
        decimal probOverfit = totalCombinations > 0
            ? (decimal)overfitCount / totalCombinations
            : 1.0m;
        decimal degradation = medianIs != 0m
            ? 1m - (medianOos / medianIs)
            : 1.0m;

        return new CpcvResult(
            medianOos, probOverfit, degradation,
            oosDistribution, totalCombinations,
            isDistribution);
    }

    /// <summary>Validates CPCV options before execution.</summary>
    private static void ValidateOptions(CpcvOptions options)
    {
        if (options.NumPaths < 3)
            throw new InvalidOperationException(
                $"NumPaths must be at least 3, got {options.NumPaths}.");
        if (options.TestFolds < 1)
            throw new InvalidOperationException(
                $"TestFolds must be at least 1, got {options.TestFolds}.");
        if (options.TestFolds >= options.NumPaths)
            throw new InvalidOperationException(
                $"TestFolds ({options.TestFolds}) must be less than NumPaths ({options.NumPaths}).");
    }

    /// <summary>
    /// Generates all C(N, k) combinations of k indices from 0..N-1.
    /// </summary>
    public static List<int[]> GenerateCombinations(int n, int k)
    {
        var result = new List<int[]>();
        var current = new int[k];
        GenerateCombinationsRecursive(n, k, 0, 0, current, result);
        return result;
    }

    private static void GenerateCombinationsRecursive(
        int n, int k, int start, int depth, int[] current, List<int[]> result)
    {
        if (depth == k)
        {
            result.Add((int[])current.Clone());
            return;
        }

        for (int i = start; i <= n - k + depth; i++)
        {
            current[depth] = i;
            GenerateCombinationsRecursive(n, k, i + 1, depth + 1, current, result);
        }
    }

    /// <summary>
    /// Builds a ScenarioConfig with date range covering the specified fold indices.
    /// Uses the earliest start and latest end of the selected folds.
    /// </summary>
    private static ScenarioConfig BuildConfigForFolds(
        ScenarioConfig baseConfig,
        List<(DateTimeOffset Start, DateTimeOffset End)> folds,
        List<int> foldIndices)
    {
        var selectedFolds = foldIndices.Select(i => folds[i]).OrderBy(f => f.Start).ToList();
        var from = selectedFolds.First().Start;
        var to = selectedFolds.Last().End;

#pragma warning disable CS0618 // Legacy dictionary access for backward compatibility
        var newOpts = new Dictionary<string, object>(baseConfig.DataProviderOptions)
        {
            ["From"] = from,
            ["To"] = to
        };

        return baseConfig with { DataProviderOptions = newOpts };
#pragma warning restore CS0618
    }

    /// <summary>Computes the median of a list of decimal values.</summary>
    public static decimal Median(List<decimal> values)
    {
        if (values.Count == 0) return 0m;
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2m
            : sorted[mid];
    }
}
