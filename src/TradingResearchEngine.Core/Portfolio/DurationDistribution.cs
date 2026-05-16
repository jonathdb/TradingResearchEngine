namespace TradingResearchEngine.Core.Portfolio;

/// <summary>
/// Summary statistics for trade duration distribution across a set of closed trades.
/// </summary>
/// <param name="MeanDuration">Average trade duration.</param>
/// <param name="MedianDuration">Median trade duration.</param>
/// <param name="MinDuration">Shortest trade duration.</param>
/// <param name="MaxDuration">Longest trade duration.</param>
/// <param name="StandardDeviation">Standard deviation of trade durations.</param>
/// <param name="TradeCount">Number of trades included in the distribution.</param>
public sealed record DurationDistribution(
    TimeSpan MeanDuration,
    TimeSpan MedianDuration,
    TimeSpan MinDuration,
    TimeSpan MaxDuration,
    TimeSpan StandardDeviation,
    int TradeCount);
