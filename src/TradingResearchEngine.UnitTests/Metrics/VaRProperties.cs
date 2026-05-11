using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.Metrics;

// Feature: trading-engine-stories, Property 7: VaR/CVaR Small-Sample Null Guard

/// <summary>
/// Property 7: VaR/CVaR Small-Sample Null Guard.
/// For any equity curve with fewer than 30 period returns and any confidence level,
/// both ComputeHistoricalVaR and ComputeHistoricalCVaR SHALL return null.
/// **Validates: Requirements 6.1, 6.2**
/// </summary>
public class VaRProperties
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
    /// For any equity curve with fewer than 30 period returns (curve.Count in [2, 30]),
    /// ComputeHistoricalVaR SHALL return null regardless of confidence level.
    /// **Validates: Requirements 6.1**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool VaR_ReturnsNull_WhenFewerThan30Returns(PositiveInt curveCountWrap, PositiveInt seedWrap, PositiveInt confidenceWrap)
    {
        // Constrain curve count to [2, 30] — gives [1, 29] period returns (all < 30)
        int curveCount = (curveCountWrap.Get % 29) + 2; // 2 to 30

        // Generate a confidence level in (0, 1)
        decimal confidence = ((decimal)(confidenceWrap.Get % 99) + 1m) / 100m; // 0.01 to 0.99

        var curve = GenerateEquityCurve(curveCount, seedWrap.Get);

        var result = MetricsCalculator.ComputeHistoricalVaR(curve, confidence);

        return result is null;
    }

    /// <summary>
    /// For any equity curve with fewer than 30 period returns (curve.Count in [2, 30]),
    /// ComputeHistoricalCVaR SHALL return null regardless of confidence level.
    /// **Validates: Requirements 6.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool CVaR_ReturnsNull_WhenFewerThan30Returns(PositiveInt curveCountWrap, PositiveInt seedWrap, PositiveInt confidenceWrap)
    {
        // Constrain curve count to [2, 30] — gives [1, 29] period returns (all < 30)
        int curveCount = (curveCountWrap.Get % 29) + 2; // 2 to 30

        // Generate a confidence level in (0, 1)
        decimal confidence = ((decimal)(confidenceWrap.Get % 99) + 1m) / 100m; // 0.01 to 0.99

        var curve = GenerateEquityCurve(curveCount, seedWrap.Get);

        var result = MetricsCalculator.ComputeHistoricalCVaR(curve, confidence);

        return result is null;
    }
}
