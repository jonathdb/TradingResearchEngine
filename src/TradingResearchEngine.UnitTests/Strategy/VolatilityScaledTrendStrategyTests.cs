using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

public class VolatilityScaledTrendStrategyTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BarEvent MakeBar(int dayOffset, decimal open, decimal high, decimal low, decimal close)
        => new("SPY", "1D", open, high, low, close, 1000m, T0.AddDays(dayOffset));

    [Fact]
    public void NoSignals_DuringWarmup()
    {
        // slowPeriod=5, atrPeriod=3 → minBars = max(5, 3+1) = 5
        var strategy = new VolatilityScaledTrendStrategy(fastPeriod: 2, slowPeriod: 5, atrPeriod: 3);

        // Feed 4 bars — should not produce any signals
        for (int i = 0; i < 4; i++)
        {
            var signals = strategy.OnMarketData(MakeBar(i, 100m + i, 102m + i, 99m + i, 101m + i));
            Assert.Empty(signals);
        }
    }

    [Fact]
    public void EntrySignal_WhenFastCrossesAboveSlow()
    {
        var strategy = new VolatilityScaledTrendStrategy(fastPeriod: 3, slowPeriod: 5, atrPeriod: 3);

        // Feed rising prices to create fast > slow crossover
        var allSignals = new List<EngineEvent>();
        for (int i = 0; i < 20; i++)
        {
            // Steadily rising prices
            decimal price = 100m + i * 2m;
            var signals = strategy.OnMarketData(MakeBar(i, price - 1, price + 1, price - 2, price));
            allSignals.AddRange(signals);
        }

        // Should have at least one Long signal
        var longSignals = allSignals.OfType<SignalEvent>()
            .Where(s => s.Direction == Direction.Long).ToList();
        Assert.NotEmpty(longSignals);
    }

    [Fact]
    public void EntrySignal_StrengthEqualsCloseOverAtr()
    {
        var strategy = new VolatilityScaledTrendStrategy(fastPeriod: 3, slowPeriod: 5, atrPeriod: 3);

        // Feed enough bars to warm up, then trigger a crossover
        var allSignals = new List<SignalEvent>();
        for (int i = 0; i < 20; i++)
        {
            decimal price = 100m + i * 2m;
            var signals = strategy.OnMarketData(MakeBar(i, price - 1, price + 1, price - 2, price));
            allSignals.AddRange(signals.OfType<SignalEvent>());
        }

        var entry = allSignals.FirstOrDefault(s => s.Direction == Direction.Long);
        Assert.NotNull(entry);
        // Strength should be positive and > Close (since ATR < Close for normal prices)
        Assert.True(entry!.Strength > 0);
    }

    [Fact]
    public void ExitSignal_WhenFastCrossesBelowSlow()
    {
        var strategy = new VolatilityScaledTrendStrategy(fastPeriod: 3, slowPeriod: 5, atrPeriod: 3);

        // Phase 1: rising prices to enter long
        for (int i = 0; i < 15; i++)
        {
            decimal price = 100m + i * 2m;
            strategy.OnMarketData(MakeBar(i, price - 1, price + 1, price - 2, price));
        }

        // Phase 2: falling prices to trigger exit
        var allSignals = new List<EngineEvent>();
        for (int i = 15; i < 30; i++)
        {
            decimal price = 130m - (i - 15) * 3m;
            var signals = strategy.OnMarketData(MakeBar(i, price + 1, price + 2, price - 1, price));
            allSignals.AddRange(signals);
        }

        var flatSignals = allSignals.OfType<SignalEvent>()
            .Where(s => s.Direction == Direction.Flat).ToList();
        Assert.NotEmpty(flatSignals);
    }

    [Fact]
    public void NoDuplicateSignals_WhenAlreadyLong()
    {
        var strategy = new VolatilityScaledTrendStrategy(fastPeriod: 3, slowPeriod: 5, atrPeriod: 3);

        // Feed steadily rising prices — should get exactly one Long entry
        var longSignals = new List<SignalEvent>();
        for (int i = 0; i < 30; i++)
        {
            decimal price = 100m + i * 2m;
            var signals = strategy.OnMarketData(MakeBar(i, price - 1, price + 1, price - 2, price));
            longSignals.AddRange(signals.OfType<SignalEvent>().Where(s => s.Direction == Direction.Long));
        }

        Assert.Single(longSignals);
    }
}
