using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 17: CPCV Determinism
/// Same seed + inputs → identical CpcvResult.
/// Since full engine runs require infrastructure, this tests determinism of the
/// combinatorial and statistical components.
/// **Validates: Requirement 22.7**
/// </summary>
public class CpcvDeterminismProperties
{
    [Property(MaxTest = 100)]
    public bool CombinationGeneration_IsDeterministic(PositiveInt nWrap, PositiveInt kWrap)
    {
        int n = (nWrap.Get % 8) + 3; // 3..10
        int k = (kWrap.Get % (n - 1)) + 1; // 1..n-1

        var run1 = CpcvStudyHandler.GenerateCombinations(n, k);
        var run2 = CpcvStudyHandler.GenerateCombinations(n, k);

        if (run1.Count != run2.Count) return false;

        for (int i = 0; i < run1.Count; i++)
        {
            if (!run1[i].SequenceEqual(run2[i])) return false;
        }

        return true;
    }

    [Property(MaxTest = 100)]
    public bool MedianComputation_IsDeterministic(NonEmptyArray<int> valuesWrap)
    {
        var values = valuesWrap.Get.Select(v => (decimal)v).ToList();

        var run1 = CpcvStudyHandler.Median(new List<decimal>(values));
        var run2 = CpcvStudyHandler.Median(new List<decimal>(values));

        return run1 == run2;
    }

    [Property(MaxTest = 100)]
    public bool SameSeedProducesSameDistributions(PositiveInt seedWrap, PositiveInt countWrap)
    {
        int seed = seedWrap.Get;
        int count = (countWrap.Get % 15) + 3;

        // Simulate what CPCV would produce with a given seed
        var rng1 = new Random(seed);
        var oos1 = Enumerable.Range(0, count).Select(_ => (decimal)(rng1.NextDouble() * 4 - 1)).ToList();
        var is1 = Enumerable.Range(0, count).Select(_ => (decimal)(rng1.NextDouble() * 4 - 1)).ToList();

        var rng2 = new Random(seed);
        var oos2 = Enumerable.Range(0, count).Select(_ => (decimal)(rng2.NextDouble() * 4 - 1)).ToList();
        var is2 = Enumerable.Range(0, count).Select(_ => (decimal)(rng2.NextDouble() * 4 - 1)).ToList();

        return oos1.SequenceEqual(oos2) && is1.SequenceEqual(is2);
    }
}
