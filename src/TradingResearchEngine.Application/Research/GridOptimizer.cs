using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Strategies.Composite;
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
    /// <param name="objective">The metric used to rank candidates (Sharpe, TotalReturn, MAR, or TimeWeightedReturn).</param>
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
    /// Selects the best parameter combination from <paramref name="candidates"/> based on
    /// the configured <paramref name="objective"/>, with an optional <see cref="CompositeParameterGrid"/>
    /// for composite strategy sweep support.
    /// </summary>
    /// <param name="candidates">The backtest results to evaluate.</param>
    /// <param name="objective">The metric used to rank candidates (Sharpe, TotalReturn, MAR, or TimeWeightedReturn).</param>
    /// <param name="compositeGrid">
    /// Optional composite parameter grid for composite strategy sweeps.
    /// When provided, the grid is accepted for validation purposes; actual combination generation
    /// is handled by the WalkForwardWorkflow.
    /// </param>
    /// <returns>
    /// A <see cref="GridOptimizationResult"/> containing the best parameters, objective value,
    /// and any excluded candidates with explanations.
    /// </returns>
    public GridOptimizationResult Optimize(
        IReadOnlyList<BacktestResult> candidates,
        OptimizationObjective objective,
        CompositeParameterGrid? compositeGrid)
    {
        // The compositeGrid parameter is accepted for API completeness and validation purposes.
        // Actual combination generation from the grid is handled by WalkForwardWorkflow (task 1.5).
        return Optimize(candidates, objective);
    }

    /// <summary>
    /// Validates a <see cref="CompositeParameterGrid"/> against a <see cref="CompositeStrategyConfig"/>,
    /// ensuring all referenced indicator IDs exist, at least one range produces values,
    /// and the total combination count does not exceed the configured maximum.
    /// </summary>
    /// <param name="grid">The composite parameter grid to validate.</param>
    /// <param name="config">The composite strategy configuration containing indicator definitions.</param>
    /// <param name="options">Sweep guardrail options providing the maximum combination count.</param>
    /// <returns>A <see cref="GridValidationResult"/> indicating success or containing error messages.</returns>
    public static GridValidationResult ValidateCompositeGrid(
        CompositeParameterGrid grid,
        CompositeStrategyConfig config,
        IOptions<SweepGuardrailOptions> options)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        var knownIndicatorIds = new HashSet<string>(
            config.Indicators.Select(i => i.Id),
            StringComparer.Ordinal);

        // Validate each IndicatorId exists in the CompositeStrategyConfig
        foreach (var range in grid.Ranges)
        {
            if (!knownIndicatorIds.Contains(range.IndicatorId))
            {
                errors.Add(
                    $"Indicator ID '{range.IndicatorId}' not found in CompositeStrategyConfig. " +
                    $"Known indicator IDs: [{string.Join(", ", knownIndicatorIds)}].");
            }
        }

        if (errors.Count > 0)
        {
            return GridValidationResult.Failure(errors);
        }

        // Validate at least one range produces values (non-zero dimensions)
        long totalCombinations = 1;
        int validDimensions = 0;

        foreach (var range in grid.Ranges)
        {
            long count = ComputeRangeValueCount(range);
            if (count > 0)
            {
                validDimensions++;
                totalCombinations *= count;
            }
        }

        if (validDimensions == 0)
        {
            return GridValidationResult.Failure(
                "No sweep dimensions produce values. At least one parameter range must produce at least one value.");
        }

        // Validate total combination count does not exceed maximum
        var maxCombinations = options.Value.MaxCombinations;
        if (totalCombinations > maxCombinations)
        {
            return GridValidationResult.Failure(
                $"Total parameter combinations ({totalCombinations}) exceeds the configured maximum ({maxCombinations}).");
        }

        return GridValidationResult.Success();
    }

    /// <summary>
    /// Computes the number of values produced by a parameter range using inclusive-inclusive enumeration:
    /// floor((End - Start) / Step) + 1.
    /// Returns zero when the range is degenerate (Step &lt;= 0 or End &lt; Start).
    /// </summary>
    private static long ComputeRangeValueCount(CompositeParameterRange range)
    {
        if (range.Step <= 0m || range.End < range.Start)
            return 0;

        return (long)Math.Floor((range.End - range.Start) / range.Step) + 1;
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
            OptimizationObjective.TimeWeightedReturn => ComputeTimeWeightedReturn(candidate),
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
    /// Computes time-weighted (annualised) return as (EndEquity / StartEquity)^(BarsPerYear / windowBars) − 1.
    /// Uses <see cref="BacktestResult.EquityCurve"/> count as the deterministic <c>windowBars</c> value.
    /// Returns <c>null</c> when StartEquity is zero or negative, or when EquityCurve is empty.
    /// </summary>
    private static decimal? ComputeTimeWeightedReturn(BacktestResult candidate)
    {
        if (candidate.StartEquity <= 0m)
            return null;

        int windowBars = candidate.EquityCurve.Count;
        if (windowBars <= 0)
            return null;

        int barsPerYear = candidate.ScenarioConfig.BarsPerYear;
        double growthRatio = (double)(candidate.EndEquity / candidate.StartEquity);
        double exponent = (double)barsPerYear / windowBars;
        double annualised = Math.Pow(growthRatio, exponent) - 1.0;
        return (decimal)annualised;
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
            OptimizationObjective.TimeWeightedReturn =>
                $"TimeWeightedReturn is undefined — StartEquity is {candidate.StartEquity}, EquityCurve.Count is {candidate.EquityCurve.Count}. " +
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
