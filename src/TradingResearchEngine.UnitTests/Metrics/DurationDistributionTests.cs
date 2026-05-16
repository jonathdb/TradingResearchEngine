using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.Metrics;

public class DurationDistributionTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ClosedTrade MakeTrade(TimeSpan duration)
    {
        return new ClosedTrade(
            "SPY", T0, T0 + duration, 100m, 105m, 10m,
            Direction.Long, 50m, 5m, 45m, Anatomy: null);
    }

    [Fact]
    public void EmptyTrades_ReturnsNull()
    {
        var result = MetricsCalculator.ComputeDurationDistribution(Array.Empty<ClosedTrade>());
        Assert.Null(result);
    }

    [Fact]
    public void SingleTrade_MeanEqualsMedianEqualsMinEqualsMax()
    {
        var trades = new[] { MakeTrade(TimeSpan.FromHours(5)) };

        var result = MetricsCalculator.ComputeDurationDistribution(trades);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromHours(5), result.MeanDuration);
        Assert.Equal(TimeSpan.FromHours(5), result.MedianDuration);
        Assert.Equal(TimeSpan.FromHours(5), result.MinDuration);
        Assert.Equal(TimeSpan.FromHours(5), result.MaxDuration);
        Assert.Equal(1, result.TradeCount);
    }

    [Fact]
    public void MultipleTrades_ComputesCorrectStatistics()
    {
        var trades = new[]
        {
            MakeTrade(TimeSpan.FromHours(1)),
            MakeTrade(TimeSpan.FromHours(3)),
            MakeTrade(TimeSpan.FromHours(5)),
        };

        var result = MetricsCalculator.ComputeDurationDistribution(trades);

        Assert.NotNull(result);
        Assert.Equal(TimeSpan.FromHours(3), result.MeanDuration);
        Assert.Equal(TimeSpan.FromHours(3), result.MedianDuration);
        Assert.Equal(TimeSpan.FromHours(1), result.MinDuration);
        Assert.Equal(TimeSpan.FromHours(5), result.MaxDuration);
        Assert.Equal(3, result.TradeCount);
    }

    [Fact]
    public void EvenCount_MedianIsAverageOfMiddleTwo()
    {
        var trades = new[]
        {
            MakeTrade(TimeSpan.FromHours(1)),
            MakeTrade(TimeSpan.FromHours(2)),
            MakeTrade(TimeSpan.FromHours(4)),
            MakeTrade(TimeSpan.FromHours(8)),
        };

        var result = MetricsCalculator.ComputeDurationDistribution(trades);

        Assert.NotNull(result);
        // Median of [1h, 2h, 4h, 8h] = (2h + 4h) / 2 = 3h
        Assert.Equal(TimeSpan.FromHours(3), result.MedianDuration);
    }

    [Fact]
    public void UsesAnatomyDuration_WhenAvailable()
    {
        var anatomyDuration = TimeSpan.FromHours(10);
        var anatomy = new TradeAnatomy(-0.05m, 0.10m, anatomyDuration);

        // Trade entry/exit times differ from anatomy duration
        var trade = new ClosedTrade(
            "SPY", T0, T0 + TimeSpan.FromHours(12), 100m, 105m, 10m,
            Direction.Long, 50m, 5m, 45m, Anatomy: anatomy);

        var result = MetricsCalculator.ComputeDurationDistribution(new[] { trade });

        Assert.NotNull(result);
        // Should use anatomy duration (10h), not entry/exit difference (12h)
        Assert.Equal(anatomyDuration, result.MeanDuration);
    }

    [Fact]
    public void FallsBackToEntryExitDifference_WhenAnatomyIsNull()
    {
        var expectedDuration = TimeSpan.FromHours(6);
        var trade = new ClosedTrade(
            "SPY", T0, T0 + expectedDuration, 100m, 105m, 10m,
            Direction.Long, 50m, 5m, 45m, Anatomy: null);

        var result = MetricsCalculator.ComputeDurationDistribution(new[] { trade });

        Assert.NotNull(result);
        Assert.Equal(expectedDuration, result.MeanDuration);
    }
}
