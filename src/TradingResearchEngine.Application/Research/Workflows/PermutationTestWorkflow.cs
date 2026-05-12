using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research.Workflows;

/// <summary>
/// Shuffles trade entry/exit timing N times and computes the target metric for each permutation.
/// The p-value is the proportion of permuted results >= the original strategy's metric.
/// Accepts an explicit seed for deterministic reproducibility.
/// </summary>
public sealed class PermutationTestWorkflow
{
    /// <summary>
    /// Runs a permutation test on the given backtest result.
    /// </summary>
    /// <param name="originalResult">The original backtest result to test.</param>
    /// <param name="permutationCount">Number of permutations to run (default 1000).</param>
    /// <param name="seed">Explicit seed for deterministic reproducibility. Null for random.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Permutation test result with p-value and distribution.</returns>
    public Task<PermutationTestResult> RunAsync(
        BacktestResult originalResult,
        int permutationCount = 1000,
        int? seed = null,
        CancellationToken ct = default)
    {
        var effectiveSeed = seed ?? Random.Shared.Next();
        var rng = new Random(effectiveSeed);

        // Original metric: use Sharpe ratio as the default target metric
        var originalMetric = originalResult.SharpeRatio ?? 0m;

        // Compute permuted metrics by shuffling trade returns
        var tradeReturns = originalResult.Trades
            .Select(t => t.ReturnOnRisk)
            .ToArray();

        var permutedMetrics = new List<decimal>(permutationCount);

        for (int i = 0; i < permutationCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Fisher-Yates shuffle of trade returns
            var shuffled = (decimal[])tradeReturns.Clone();
            for (int j = shuffled.Length - 1; j > 0; j--)
            {
                int k = rng.Next(j + 1);
                (shuffled[j], shuffled[k]) = (shuffled[k], shuffled[j]);
            }

            // Compute Sharpe-like metric from shuffled returns
            if (shuffled.Length > 1)
            {
                var mean = shuffled.Average();
                var variance = shuffled.Sum(r => (r - mean) * (r - mean)) / (shuffled.Length - 1);
                var stdDev = (decimal)Math.Sqrt((double)variance);
                var permutedSharpe = stdDev > 0 ? mean / stdDev : 0m;
                permutedMetrics.Add(permutedSharpe);
            }
            else
            {
                permutedMetrics.Add(0m);
            }
        }

        // P-value: proportion of permuted results >= original
        var exceedCount = permutedMetrics.Count(p => p >= originalMetric);
        var pValue = (decimal)exceedCount / permutationCount;

        return Task.FromResult(new PermutationTestResult(
            originalMetric,
            permutedMetrics.AsReadOnly(),
            pValue,
            permutationCount,
            effectiveSeed));
    }
}
