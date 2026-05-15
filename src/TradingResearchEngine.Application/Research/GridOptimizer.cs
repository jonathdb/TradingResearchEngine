using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Evaluates a set of backtest result candidates and selects the one producing
/// the highest value of the configured <see cref="OptimizationObjective"/>.
/// Candidates whose objective metric is undefined are excluded with a structured explanation.
/// </summary>
public sealed class GridOptimizer
{
    /// <summary>
    /// Selects the best parameter combination from <paramref name="candidates"/> based on
    /// the configured <paramref name="objective"/>. Candidates with undefined objective
    /// metrics are excluded — the optimizer never falls through to a different objective.
    /// </summary>
    /// <param name="candidates">The backtest results to evaluate.</param>
    /// <param name="objective">The metric used to rank candidates (Sharpe, TotalReturn, or MAR).</param>
    /// <returns>
    /// A <see cref="GridOptimizationResult"/> containing the best parameters, objective value,
    /// and any excluded candidates with explanations.
    /// </returns>
    public GridOptimizationResult Optimize(
        IReadOnlyList<BacktestResult> candidates,
        OptimizationObjective objective)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var excluded = new List<ExcludedCandidate>();
        BacktestResult? bestCandidate = null;
        decimal bestValue = decimal.MinValue;

        foreach (var candidate in candidates)
        {
            var metricValue = ExtractObjectiveValue(candidate, objective);

            if (metricValue is null)
            {
                var parameters = ExtractParameters(candidate);
                var reason = BuildExclusionReason(objective, candidate);
                excluded.Add(new ExcludedCandidate(parameters, reason));
                continue;
            }

            if (metricValue.Value > bestValue)
            {
                bestValue = metricValue.Value;
                bestCandidate = candidate;
            }
        }

        if (bestCandidate is null)
        {
            // All candidates were excluded — return empty result
            return new GridOptimizationResult(
                BestParameters: new Dictionary<string, object>(),
                ObjectiveValue: 0m,
                Excluded: excluded);
        }

        return new GridOptimizationResult(
            BestParameters: ExtractParameters(bestCandidate),
            ObjectiveValue: bestValue,
            Excluded: excluded);
    }

    /// <summary>
    /// Extracts the objective metric value from a candidate result.
    /// Returns <c>null</c> when the metric is undefined for the candidate.
    /// Never falls through to a different objective.
    /// </summary>
    private static decimal? ExtractObjectiveValue(BacktestResult candidate, OptimizationObjective objective)
    {
        return objective switch
        {
            OptimizationObjective.Sharpe => candidate.SharpeRatio,
            OptimizationObjective.TotalReturn => ComputeTotalReturn(candidate),
            OptimizationObjective.MAR => candidate.CalmarRatio,
            _ => null
        };
    }

    /// <summary>
    /// Computes total return as (EndEquity − StartEquity) / StartEquity. Returns <c>null</c> when
    /// <see cref="BacktestResult.StartEquity"/> is zero or negative.
    /// </summary>
    private static decimal? ComputeTotalReturn(BacktestResult candidate)
    {
        if (candidate.StartEquity <= 0m)
            return null;

        return (candidate.EndEquity - candidate.StartEquity) / candidate.StartEquity;
    }

    /// <summary>
    /// Builds a human-readable exclusion reason for a candidate whose objective metric is undefined.
    /// </summary>
    private static string BuildExclusionReason(OptimizationObjective objective, BacktestResult candidate)
    {
        return objective switch
        {
            OptimizationObjective.Sharpe =>
                $"SharpeRatio is undefined (null) for candidate with {candidate.TotalTrades} trades. " +
                "Candidate excluded from ranking without fallthrough to alternative objective.",
            OptimizationObjective.TotalReturn =>
                $"TotalReturn is undefined — StartEquity is {candidate.StartEquity}. " +
                "Candidate excluded from ranking without fallthrough to alternative objective.",
            OptimizationObjective.MAR =>
                $"MAR ratio (CalmarRatio) is undefined (null) for candidate with MaxDrawdown={candidate.MaxDrawdown}. " +
                "Candidate excluded from ranking without fallthrough to alternative objective.",
            _ =>
                $"Unknown objective '{objective}' is undefined. " +
                "Candidate excluded from ranking without fallthrough to alternative objective."
        };
    }

    /// <summary>
    /// Extracts the strategy parameters from a candidate's scenario configuration.
    /// </summary>
    private static Dictionary<string, object> ExtractParameters(BacktestResult candidate)
    {
        return new Dictionary<string, object>(candidate.ScenarioConfig.StrategyParameters);
    }
}

/// <summary>
/// The result of a grid optimization pass, containing the best parameter combination,
/// its objective value, and any candidates that were excluded from ranking.
/// </summary>
/// <param name="BestParameters">
/// The strategy parameters of the winning candidate. Empty when all candidates are excluded.
/// </param>
/// <param name="ObjectiveValue">
/// The objective metric value achieved by the best candidate. Zero when all candidates are excluded.
/// </param>
/// <param name="Excluded">
/// Candidates excluded from ranking because their objective metric was undefined,
/// each with a structured explanation of why they were excluded.
/// </param>
public sealed record GridOptimizationResult(
    Dictionary<string, object> BestParameters,
    decimal ObjectiveValue,
    IReadOnlyList<ExcludedCandidate> Excluded);

/// <summary>
/// A candidate that was excluded from grid optimization ranking because its
/// configured objective metric was undefined.
/// </summary>
/// <param name="Parameters">The strategy parameters of the excluded candidate.</param>
/// <param name="Reason">A structured explanation of why the candidate was excluded.</param>
public sealed record ExcludedCandidate(
    Dictionary<string, object> Parameters,
    string Reason);
