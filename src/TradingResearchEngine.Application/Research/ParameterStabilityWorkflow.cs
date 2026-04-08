using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Evaluates the neighbourhood around a target parameter set from sweep results.
/// Produces a FragilityScore: 0 = robust (performs well across neighbours), 1 = fragile (narrow island).
/// </summary>
public sealed class ParameterStabilityWorkflow
{
    /// <summary>
    /// Analyses parameter stability from a sweep result and a target parameter set.
    /// </summary>
    public ParameterStabilityResult Analyse(
        SweepResult sweepResult,
        Dictionary<string, object> targetParams,
        ParameterStabilityOptions options)
    {
        var neighbours = FindNeighbours(sweepResult.Results, targetParams, options.NeighbourhoodPercent);

        if (neighbours.Count == 0)
            return new ParameterStabilityResult(0m, 0m, 0m, 1.0m);

        var sharpes = neighbours
            .Where(r => r.SharpeRatio.HasValue)
            .Select(r => r.SharpeRatio!.Value)
            .OrderBy(s => s)
            .ToList();

        if (sharpes.Count == 0)
            return new ParameterStabilityResult(0m, 0m, 0m, 1.0m);

        decimal median = sharpes[sharpes.Count / 2];
        decimal worst = sharpes[0];
        decimal profitable = (decimal)neighbours.Count(r => r.SharpeRatio > 0m) / neighbours.Count;
        decimal fragility = 1m - profitable;

        return new ParameterStabilityResult(median, worst, profitable, fragility);
    }

    private static List<BacktestResult> FindNeighbours(
        IReadOnlyList<BacktestResult> results,
        Dictionary<string, object> target,
        decimal neighbourhoodPercent)
    {
        var neighbours = new List<BacktestResult>();
        decimal fraction = neighbourhoodPercent / 100m;

        foreach (var result in results)
        {
            bool isNeighbour = true;
            foreach (var (key, targetVal) in target)
            {
                if (!result.ScenarioConfig.StrategyParameters.TryGetValue(key, out var resultVal))
                {
                    isNeighbour = false;
                    break;
                }

                if (TryGetDecimal(targetVal, out var tv) && TryGetDecimal(resultVal, out var rv))
                {
                    decimal range = Math.Abs(tv) * fraction;
                    if (range == 0m) range = fraction; // handle zero target
                    if (Math.Abs(rv - tv) > range)
                    {
                        isNeighbour = false;
                        break;
                    }
                }
            }
            if (isNeighbour) neighbours.Add(result);
        }
        return neighbours;
    }

    private static bool TryGetDecimal(object? value, out decimal result)
    {
        result = 0m;
        if (value is decimal d) { result = d; return true; }
        if (value is int i) { result = i; return true; }
        if (value is double dbl) { result = (decimal)dbl; return true; }
        if (value is long l) { result = l; return true; }
        return decimal.TryParse(value?.ToString(), out result);
    }
}
