using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Combinatorial Purged Cross-Validation (De Prado, 2018).
/// Generates C(N,k) train/test combinations across N folds and computes
/// the Probability of Backtest Overfitting (PBO).
/// </summary>
public sealed class CpcvStudyHandler : IResearchWorkflow<CpcvOptions, CpcvResult>
{
    private readonly RunScenarioUseCase _runScenario;

    /// <inheritdoc cref="CpcvStudyHandler"/>
    public CpcvStudyHandler(RunScenarioUseCase runScenario)
    {
        _runScenario = runScenario;
    }

    /// <inheritdoc/>
    public async Task<CpcvResult> RunAsync(
        ScenarioConfig baseConfig, CpcvOptions options, CancellationToken ct = default)
    {
        ValidateOptions(options);

        // Parse data range from config
        var dataOpts = baseConfig.DataProviderOptions;
        var dataFrom = dataOpts.TryGetValue("From", out var f) && f is DateTimeOffset df
            ? df : throw new InvalidOperationException("CPCV requires a 'From' date in DataProviderOptions.");
        var dataTo = dataOpts.TryGetValue("To", out var t) && t is DateTimeOffset dt
            ? dt : throw new InvalidOperationException("CPCV requires a 'To' date in DataProviderOptions.");

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

        // Step 3: Run each combination
        var oosDistribution = new List<decimal>();
        var isDistribution = new List<decimal>();
        int overfitCount = 0;

        foreach (var testIndices in combinations)
        {
            ct.ThrowIfCancellationRequested();

            var trainIndices = Enumerable.Range(0, options.NumPaths)
                .Where(i => !testIndices.Contains(i))
                .ToList();

            // Train: run engine on concatenated training folds → this combination's IS Sharpe
            var trainConfig = BuildConfigForFolds(baseConfig, folds, trainIndices);
            var trainResult = await _runScenario.RunAsync(trainConfig, ct, autoSave: false);
            decimal comboIsSharpe = trainResult.Result?.SharpeRatio ?? 0m;

            // Test: run engine on concatenated test folds → this combination's OOS Sharpe
            var testConfig = BuildConfigForFolds(baseConfig, folds, testIndices.ToList());
            var testResult = await _runScenario.RunAsync(testConfig, ct, autoSave: false);
            decimal comboOosSharpe = testResult.Result?.SharpeRatio ?? 0m;

            isDistribution.Add(comboIsSharpe);
            oosDistribution.Add(comboOosSharpe);

            // Per-combination comparison: OOS vs that same combination's IS
            if (comboOosSharpe < comboIsSharpe)
                overfitCount++;
        }

        // Step 4: Compute summary statistics
        decimal medianOos = Median(oosDistribution);
        decimal medianIs = Median(isDistribution);
        decimal probOverfit = combinations.Count > 0
            ? (decimal)overfitCount / combinations.Count
            : 1.0m;
        decimal degradation = medianIs != 0m
            ? 1m - (medianOos / medianIs)
            : 1.0m;

        return new CpcvResult(
            medianOos, probOverfit, degradation,
            oosDistribution, combinations.Count,
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

        var newOpts = new Dictionary<string, object>(baseConfig.DataProviderOptions)
        {
            ["From"] = from,
            ["To"] = to
        };

        return baseConfig with { DataProviderOptions = newOpts };
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
