using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 11: CPCV Combination Count
/// For valid N ≥ 3 and 1 ≤ k &lt; N, produces exactly C(N,k) combinations.
/// **Validates: Requirements 22.1, 22.2**
/// </summary>
public class CpcvCombinationCountProperties
{
    [Property(MaxTest = 100)]
    public bool CpcvCombinationCount_MatchesFormula(PositiveInt nWrap, PositiveInt kWrap)
    {
        // Constrain N to [3, 10] and k to [1, N-1]
        int n = (nWrap.Get % 8) + 3; // 3..10
        int k = (kWrap.Get % (n - 1)) + 1; // 1..n-1

        var combos = CpcvStudyHandler.GenerateCombinations(n, k);
        long expected = Factorial(n) / (Factorial(k) * Factorial(n - k));

        return combos.Count == expected
            && combos.All(c => c.Length == k)
            && combos.All(c => c.All(i => i >= 0 && i < n));
    }

    private static long Factorial(int n)
    {
        long result = 1;
        for (int i = 2; i <= n; i++)
            result *= i;
        return result;
    }
}
