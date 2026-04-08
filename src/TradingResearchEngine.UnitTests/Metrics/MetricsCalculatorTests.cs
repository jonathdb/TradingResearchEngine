using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.Metrics;

public class MetricsCalculatorTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeMaxDrawdown_FlatCurve_ReturnsZero()
    {
        var curve = Enumerable.Range(0, 5)
            .Select(i => new EquityCurvePoint(T0.AddDays(i), 100_000m))
            .ToList();

        Assert.Equal(0m, MetricsCalculator.ComputeMaxDrawdown(curve));
    }

    [Fact]
    public void ComputeMaxDrawdown_RisingCurve_ReturnsZero()
    {
        var curve = new List<EquityCurvePoint>
        {
            new(T0, 100_000m),
            new(T0.AddDays(1), 110_000m),
            new(T0.AddDays(2), 120_000m),
        };

        Assert.Equal(0m, MetricsCalculator.ComputeMaxDrawdown(curve));
    }

    [Fact]
    public void ComputeMaxDrawdown_PeakToTrough_ReturnsCorrectFraction()
    {
        var curve = new List<EquityCurvePoint>
        {
            new(T0, 100_000m),
            new(T0.AddDays(1), 120_000m), // peak
            new(T0.AddDays(2), 90_000m),  // trough: (120k-90k)/120k = 0.25
            new(T0.AddDays(3), 110_000m),
        };

        Assert.Equal(0.25m, MetricsCalculator.ComputeMaxDrawdown(curve));
    }

    [Fact]
    public void ComputeMaxDrawdown_SinglePoint_ReturnsZero()
    {
        var curve = new List<EquityCurvePoint> { new(T0, 100_000m) };
        Assert.Equal(0m, MetricsCalculator.ComputeMaxDrawdown(curve));
    }

    [Fact]
    public void AllRatioMetrics_ZeroTrades_ReturnNull()
    {
        var empty = new List<ClosedTrade>();
        var emptyCurve = new List<EquityCurvePoint>();

        Assert.Null(MetricsCalculator.ComputeSharpeRatio(emptyCurve, 0.02m, 252));
        Assert.Null(MetricsCalculator.ComputeSortinoRatio(emptyCurve, 0.02m, 252));
        Assert.Null(MetricsCalculator.ComputeWinRate(empty));
        Assert.Null(MetricsCalculator.ComputeProfitFactor(empty));
        Assert.Null(MetricsCalculator.ComputeAverageWin(empty));
        Assert.Null(MetricsCalculator.ComputeAverageLoss(empty));
    }

    [Fact]
    public void ComputeWinRate_MixedTrades_ReturnsCorrectFraction()
    {
        var trades = new List<ClosedTrade>
        {
            MakeTrade(100m),  // win
            MakeTrade(-50m),  // loss
            MakeTrade(200m),  // win
            MakeTrade(-30m),  // loss
        };

        Assert.Equal(0.5m, MetricsCalculator.ComputeWinRate(trades));
    }

    [Fact]
    public void ComputeProfitFactor_MixedTrades_ReturnsGrossProfitOverGrossLoss()
    {
        var trades = new List<ClosedTrade>
        {
            MakeTrade(300m),
            MakeTrade(-100m),
        };

        Assert.Equal(3m, MetricsCalculator.ComputeProfitFactor(trades));
    }

    [Fact]
    public void ComputeSharpeRatio_RisingEquityCurve_ReturnsNonNull()
    {
        // Rising equity curve: 100k, 100.1k, 100.2k, ... (20 points)
        var curve = Enumerable.Range(0, 20)
            .Select(i => new EquityCurvePoint(T0.AddDays(i), 100_000m + i * 100m))
            .ToList();

        var sharpe = MetricsCalculator.ComputeSharpeRatio(curve, 0.02m, 252);
        Assert.NotNull(sharpe);
    }

    [Fact]
    public void ComputeSortinoRatio_VolatileEquityCurve_ReturnsNonNull()
    {
        // Volatile curve with some down periods
        var curve = new List<EquityCurvePoint>();
        decimal equity = 100_000m;
        for (int i = 0; i < 30; i++)
        {
            equity += (i % 3 == 0) ? -200m : 300m;
            curve.Add(new EquityCurvePoint(T0.AddDays(i), equity));
        }

        var sortino = MetricsCalculator.ComputeSortinoRatio(curve, 0.02m, 252);
        Assert.NotNull(sortino);
    }

    private static ClosedTrade MakeTrade(decimal netPnl) =>
        new("TEST", T0, T0.AddHours(1), 100m, 100m + netPnl, 1m,
            Core.Events.Direction.Long, netPnl, 0m, netPnl);
}

public class MetricsCalculatorExpectancyTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeExpectancy_ZeroTrades_ReturnsNull()
    {
        Assert.Null(MetricsCalculator.ComputeExpectancy(new List<ClosedTrade>()));
    }

    [Fact]
    public void ComputeExpectancy_MixedTrades_ReturnsCorrectValue()
    {
        // 2 wins at +100, 2 losses at -50
        // WinRate=0.5, AvgWin=100, LossRate=0.5, AvgLoss=-50
        // Expectancy = 0.5*100 + 0.5*(-50) = 25
        var trades = new List<ClosedTrade>
        {
            MakeTrade(100m), MakeTrade(100m), MakeTrade(-50m), MakeTrade(-50m)
        };
        Assert.Equal(25m, MetricsCalculator.ComputeExpectancy(trades));
    }

    [Fact]
    public void ComputeMaxConsecutiveLosses_ReturnsLongestStreak()
    {
        var trades = new List<ClosedTrade>
        {
            MakeTrade(10m), MakeTrade(-5m), MakeTrade(-3m), MakeTrade(-7m), MakeTrade(20m), MakeTrade(-1m)
        };
        Assert.Equal(3, MetricsCalculator.ComputeMaxConsecutiveLosses(trades));
    }

    [Fact]
    public void ComputeMaxConsecutiveWins_ReturnsLongestStreak()
    {
        var trades = new List<ClosedTrade>
        {
            MakeTrade(10m), MakeTrade(5m), MakeTrade(-3m), MakeTrade(20m), MakeTrade(15m), MakeTrade(8m)
        };
        Assert.Equal(3, MetricsCalculator.ComputeMaxConsecutiveWins(trades));
    }

    [Fact]
    public void ComputeMaxConsecutiveLosses_NoTrades_ReturnsZero()
    {
        Assert.Equal(0, MetricsCalculator.ComputeMaxConsecutiveLosses(new List<ClosedTrade>()));
    }

    private static ClosedTrade MakeTrade(decimal netPnl) =>
        new("TEST", T0, T0.AddHours(1), 100m, 100m + netPnl, 1m,
            Core.Events.Direction.Long, netPnl, 0m, netPnl);
}

public class MetricsCalculatorAdvancedTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ComputeCalmarRatio_ZeroDrawdown_ReturnsNull()
    {
        var curve = new List<EquityCurvePoint>
        {
            new(T0, 100_000m),
            new(T0.AddDays(252), 110_000m),
        };
        Assert.Null(MetricsCalculator.ComputeCalmarRatio(curve, 100_000m, 110_000m));
    }

    [Fact]
    public void ComputeCalmarRatio_WithDrawdown_ReturnsPositive()
    {
        var curve = new List<EquityCurvePoint>
        {
            new(T0, 100_000m),
            new(T0.AddDays(126), 120_000m),
            new(T0.AddDays(189), 108_000m),
            new(T0.AddDays(252), 115_000m),
        };
        var result = MetricsCalculator.ComputeCalmarRatio(curve, 100_000m, 115_000m);
        Assert.NotNull(result);
        Assert.True(result > 0m);
    }

    [Fact]
    public void ComputeReturnOnMaxDrawdown_WithDrawdown_ReturnsCorrect()
    {
        var curve = new List<EquityCurvePoint>
        {
            new(T0, 100_000m),
            new(T0.AddDays(1), 120_000m),
            new(T0.AddDays(2), 90_000m),
            new(T0.AddDays(3), 110_000m),
        };
        // return = 10%, maxDD = 25%, RoMaD = 0.10/0.25 = 0.4
        var result = MetricsCalculator.ComputeReturnOnMaxDrawdown(curve, 100_000m, 110_000m);
        Assert.NotNull(result);
        Assert.Equal(0.4m, result);
    }

    [Fact]
    public void ComputeAverageHoldingPeriod_ReturnsCorrectAverage()
    {
        var trades = new List<ClosedTrade>
        {
            new("A", T0, T0.AddHours(2), 100m, 110m, 1m, Direction.Long, 10m, 0m, 10m),
            new("A", T0, T0.AddHours(4), 100m, 105m, 1m, Direction.Long, 5m, 0m, 5m),
        };
        var result = MetricsCalculator.ComputeAverageHoldingPeriod(trades);
        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromHours(3), result);
    }

    [Fact]
    public void ComputeAverageHoldingPeriod_NoTrades_ReturnsNull()
    {
        Assert.Null(MetricsCalculator.ComputeAverageHoldingPeriod(new List<ClosedTrade>()));
    }

    [Fact]
    public void ComputeEquityCurveSmoothness_LinearRisingCurve_ReturnsPositiveKRatio()
    {
        var curve = Enumerable.Range(0, 50)
            .Select(i => new EquityCurvePoint(T0.AddDays(i), 100_000m + i * 100m))
            .ToList();
        var kRatio = MetricsCalculator.ComputeEquityCurveSmoothness(curve);
        Assert.NotNull(kRatio);
        // K-Ratio for a perfectly linear rising curve should be large and positive
        Assert.True(kRatio > 0m, $"Expected positive K-Ratio, got {kRatio}");
    }

    [Fact]
    public void ComputeEquityCurveSmoothness_TooFewPoints_ReturnsNull()
    {
        var curve = new List<EquityCurvePoint>
        {
            new(T0, 100_000m),
            new(T0.AddDays(1), 101_000m),
        };
        Assert.Null(MetricsCalculator.ComputeEquityCurveSmoothness(curve));
    }
}
