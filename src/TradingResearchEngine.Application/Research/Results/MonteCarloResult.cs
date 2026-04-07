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
    int P90MaxConsecutiveWins);
