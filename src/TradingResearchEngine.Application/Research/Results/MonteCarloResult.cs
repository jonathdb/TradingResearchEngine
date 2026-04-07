namespace TradingResearchEngine.Application.Research.Results;

/// <summary>Result of a Monte Carlo simulation workflow.</summary>
public sealed record MonteCarloResult(
    decimal P10EndEquity,
    decimal P50EndEquity,
    decimal P90EndEquity,
    decimal RuinProbability,
    decimal MedianMaxDrawdown,
    IReadOnlyList<decimal> EndEquityDistribution,
    int P90MaxConsecutiveLosses,
    int P90MaxConsecutiveWins,
    IReadOnlyList<MonteCarloPath> SampledPaths,
    IReadOnlyList<MonteCarloPercentileBand> PercentileBands);

/// <summary>A single sampled equity path from the Monte Carlo simulation.</summary>
public sealed record MonteCarloPath(IReadOnlyList<decimal> EquityValues);

/// <summary>P10/P50/P90 equity values at a specific trade step.</summary>
public sealed record MonteCarloPercentileBand(int Step, decimal P10, decimal P50, decimal P90);
