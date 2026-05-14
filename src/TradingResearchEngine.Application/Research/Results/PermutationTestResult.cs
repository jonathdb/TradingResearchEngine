namespace TradingResearchEngine.Application.Research.Results;

/// <summary>
/// Result of a permutation test measuring statistical significance.
/// The p-value represents the proportion of permuted results that equal or exceed the original metric.
/// </summary>
public sealed record PermutationTestResult(
    decimal OriginalMetric,
    IReadOnlyList<decimal> PermutedMetrics,
    decimal PValue,
    int PermutationCount,
    int Seed);
