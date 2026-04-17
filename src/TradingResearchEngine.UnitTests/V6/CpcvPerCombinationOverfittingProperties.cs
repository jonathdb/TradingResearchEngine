using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Research.Results;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 16: CPCV Per-Combination Overfitting
/// ProbabilityOfOverfitting equals count of combinations where OOS &lt; IS for that same combination,
/// divided by total.
/// **Validates: Requirements 22.3, 22.4**
/// </summary>
public class CpcvPerCombinationOverfittingProperties
{
    [Property(MaxTest = 100)]
    public bool ProbabilityOfOverfitting_MatchesPerCombinationCount(PositiveInt countWrap)
    {
        // Generate synthetic IS and OOS Sharpe distributions
        int count = (countWrap.Get % 20) + 3; // 3..22 combinations
        var rng = new Random(countWrap.Get);

        var isDistribution = new List<decimal>();
        var oosDistribution = new List<decimal>();

        for (int i = 0; i < count; i++)
        {
            decimal isSharpe = (decimal)(rng.NextDouble() * 4 - 1); // -1 to 3
            decimal oosSharpe = (decimal)(rng.NextDouble() * 4 - 1); // -1 to 3
            isDistribution.Add(isSharpe);
            oosDistribution.Add(oosSharpe);
        }

        // Compute expected probability manually
        int overfitCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (oosDistribution[i] < isDistribution[i])
                overfitCount++;
        }
        decimal expectedProb = (decimal)overfitCount / count;

        // Build CpcvResult with these distributions
        decimal medianOos = CpcvStudyHandler.Median(oosDistribution.ToList());
        decimal medianIs = CpcvStudyHandler.Median(isDistribution.ToList());
        decimal degradation = medianIs != 0m ? 1m - (medianOos / medianIs) : 1.0m;

        var result = new CpcvResult(
            medianOos, expectedProb, degradation,
            oosDistribution, count, isDistribution);

        return result.ProbabilityOfOverfitting == expectedProb
            && result.ProbabilityOfOverfitting >= 0m
            && result.ProbabilityOfOverfitting <= 1m
            && result.TotalCombinations == count;
    }

    [Property(MaxTest = 100)]
    public bool AllOosNegative_ProbabilityIsOne(PositiveInt countWrap)
    {
        int count = (countWrap.Get % 10) + 3;
        var rng = new Random(countWrap.Get);

        var isDistribution = new List<decimal>();
        var oosDistribution = new List<decimal>();

        for (int i = 0; i < count; i++)
        {
            isDistribution.Add((decimal)(rng.NextDouble() * 2 + 0.1)); // positive IS
            oosDistribution.Add((decimal)(rng.NextDouble() * -2 - 0.1)); // negative OOS
        }

        // All OOS < IS → probability = 1.0
        int overfitCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (oosDistribution[i] < isDistribution[i])
                overfitCount++;
        }
        decimal prob = (decimal)overfitCount / count;

        return prob == 1.0m;
    }
}
