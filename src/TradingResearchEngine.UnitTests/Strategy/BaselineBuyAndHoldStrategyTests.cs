using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

public class BaselineBuyAndHoldStrategyTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BarEvent MakeBar(int dayOffset, decimal close)
        => new("SPY", "1D", close, close + 1m, close - 1m, close, 1000m, T0.AddDays(dayOffset));

    [Fact]
    public void EmitsExactlyOneLongSignal()
    {
        var strategy = new BaselineBuyAndHoldStrategy(warmupBars: 1);

        var allSignals = new List<SignalEvent>();
        for (int i = 0; i < 100; i++)
        {
            var signals = strategy.OnMarketData(MakeBar(i, 100m + i));
            allSignals.AddRange(signals.OfType<SignalEvent>());
        }

        Assert.Single(allSignals);
        Assert.Equal(Direction.Long, allSignals[0].Direction);
    }

    [Fact]
    public void NeverEmitsFlatSignal()
    {
        var strategy = new BaselineBuyAndHoldStrategy(warmupBars: 1);

        var flatSignals = new List<SignalEvent>();
        for (int i = 0; i < 100; i++)
        {
            var signals = strategy.OnMarketData(MakeBar(i, 100m + i));
            flatSignals.AddRange(signals.OfType<SignalEvent>().Where(s => s.Direction == Direction.Flat));
        }

        Assert.Empty(flatSignals);
    }

    [Fact]
    public void StrengthIsOne()
    {
        var strategy = new BaselineBuyAndHoldStrategy(warmupBars: 1);

        var signals = strategy.OnMarketData(MakeBar(0, 150m));
        var entry = signals.OfType<SignalEvent>().Single();

        Assert.Equal(1.0m, entry.Strength);
    }

    [Fact]
    public void RespectsWarmupPeriod()
    {
        var strategy = new BaselineBuyAndHoldStrategy(warmupBars: 5);

        // Bars 1-4: no signal
        for (int i = 0; i < 4; i++)
        {
            var signals = strategy.OnMarketData(MakeBar(i, 100m));
            Assert.Empty(signals);
        }

        // Bar 5: entry
        var entrySignals = strategy.OnMarketData(MakeBar(4, 100m));
        Assert.Single(entrySignals.OfType<SignalEvent>());
    }

    [Fact]
    public void NoFurtherSignals_AfterEntry()
    {
        var strategy = new BaselineBuyAndHoldStrategy(warmupBars: 1);

        // First bar: entry
        strategy.OnMarketData(MakeBar(0, 100m));

        // Subsequent bars: no signals regardless of price movement
        for (int i = 1; i < 50; i++)
        {
            decimal price = i % 2 == 0 ? 50m : 200m; // wild swings
            var signals = strategy.OnMarketData(MakeBar(i, price));
            Assert.Empty(signals);
        }
    }
}
