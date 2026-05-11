using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.Metrics;

// Feature: trading-engine-stories, Property 8: VaR Correctness for Sufficient Samples

/// <summary>
/// Property 8: VaR Correctness for Sufficient Samples.
/// For any equity curve with 30+ period returns and any confidence in (0,1),
/// ComputeHistoricalVaR returns a non-null value equal to the negated return at
/// floor((1 - confidence) * count) index of the sorted return series.
/// **Validates: Requirements 6.3**
/// </summary>
public class VaRCorrectnessProperties
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Generates a random equity curve with the specified number of points.
    /// All TotalEquity values are positive (non-zero) to ensure period returns are computed
    /// for every consecutive pair, giving exactly (count - 1) period returns.
    /// </summary>
    private static List<EquityCurvePoint> GenerateEquityCurve(int count, int seed)
    {
        var rng = new Random(seed);
        var curve = new List<EquityCurvePoint>(count);

        for (int i = 0; i < count; i++)
        {
            // Generate positive equity values between 1000 and 200000
            decimal equity = (decimal)(rng.NextDouble() * 199000 + 1000);
            curve.Add(new EquityCurvePoint(
                Timestamp: T0.AddDays(i),
                TotalEquity: equity,
                CashBalance: equity * 0.5m,
                UnrealisedPnl: equity * 0.3m,
                RealisedPnl: equity * 0.2m,
                OpenPositionCount: rng.Next(0, 5)));
        }

        return curve;
    }

    /// <summary>
    /// Computes period returns from an equity curve (same logic as MetricsCalculator).
    /// </summary>
    private static List<decimal> ComputePeriodReturns(List<EquityCurvePoint> curve)
    {
        var returns = new List<decimal>(curve.Count - 1);
        for (int i = 1; i < curve.Count; i++)
        {
            decimal prev = curve[i - 1].TotalEquity;
            if (prev != 0m)
                returns.Add((curve[i].TotalEquity - prev) / prev);
        }
        return returns;
    }

    /// <summary>
    /// For any equity curve with 30+ period returns (curve.Count >= 31) and any confidence
    /// in (0, 1), ComputeHistoricalVaR SHALL return a non-null value equal to the negated
    /// return at floor((1 - confidence) * count) index of the sorted return series.
    /// **Validates: Requirements 6.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool VaR_ReturnsCorrectValue_WhenSufficientSamples(PositiveInt curveCountWrap, PositiveInt seedWrap, PositiveInt confidenceWrap)
    {
        // Constrain curve count to [31, 200] — gives [30, 199] period returns (all >= 30)
        int curveCount = (curveCountWrap.Get % 170) + 31; // 31 to 200

        // Generate a confidence level in (0, 1)
        decimal confidence = ((decimal)(confidenceWrap.Get % 99) + 1m) / 100m; // 0.01 to 0.99

        var curve = GenerateEquityCurve(curveCount, seedWrap.Get);

        // Compute expected VaR using the same formula as MetricsCalculator
        var returns = ComputePeriodReturns(curve);
        var sortedReturns = returns.OrderBy(r => r).ToList();
        int idx = (int)Math.Floor((1 - confidence) * (decimal)sortedReturns.Count);
        decimal expectedVaR = -sortedReturns[Math.Max(0, idx)];

        // Compute actual VaR
        var actualVaR = MetricsCalculator.ComputeHistoricalVaR(curve, confidence);

        // VaR must be non-null and equal to expected value
        return actualVaR is not null && actualVaR.Value == expectedVaR;
    }
}
