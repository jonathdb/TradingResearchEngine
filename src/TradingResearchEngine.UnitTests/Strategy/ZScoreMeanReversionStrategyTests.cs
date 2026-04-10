using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

public class ZScoreMeanReversionStrategyTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BarEvent MakeBar(int dayOffset, decimal close)
        => new("SPY", "1D", close, close + 1m, close - 1m, close, 1000m, T0.AddDays(dayOffset));

    [Fact]
    public void NoSignals_DuringWarmup()
    {
        var strategy = new ZScoreMeanReversionStrategy(lookback: 10);

        for (int i = 0; i < 9; i++)
        {
            var signals = strategy.OnMarketData(MakeBar(i, 100m));
            Assert.Empty(signals);
        }
    }

    [Fact]
    public void EntrySignal_WhenZScoreBelowNegativeThreshold()
    {
        var strategy = new ZScoreMeanReversionStrategy(lookback: 10, entryThreshold: 2.0m, exitThreshold: 0.0m);

        // Feed stable prices to build a mean
        for (int i = 0; i < 10; i++)
        {
            strategy.OnMarketData(MakeBar(i, 100m));
        }

        // Drop price sharply — z-score should go well below -2
        var signals = strategy.OnMarketData(MakeBar(10, 80m));
        var longSignals = signals.OfType<SignalEvent>()
            .Where(s => s.Direction == Direction.Long).ToList();
        Assert.Single(longSignals);
    }

    [Fact]
    public void ExitSignal_WhenZScoreAboveExitThreshold()
    {
        var strategy = new ZScoreMeanReversionStrategy(lookback: 10, entryThreshold: 2.0m, exitThreshold: 0.0m);

        // Build mean at 100
        for (int i = 0; i < 10; i++)
        {
            strategy.OnMarketData(MakeBar(i, 100m));
        }

        // Enter: sharp drop
        strategy.OnMarketData(MakeBar(10, 80m));

        // Revert back to mean — z-score should cross above 0
        var signals = strategy.OnMarketData(MakeBar(11, 100m));
        var flatSignals = signals.OfType<SignalEvent>()
            .Where(s => s.Direction == Direction.Flat).ToList();
        Assert.Single(flatSignals);
    }

    [Fact]
    public void NoDuplicateEntry_WhenAlreadyLong()
    {
        var strategy = new ZScoreMeanReversionStrategy(lookback: 10, entryThreshold: 2.0m, exitThreshold: 0.0m);

        for (int i = 0; i < 10; i++)
        {
            strategy.OnMarketData(MakeBar(i, 100m));
        }

        // First drop — should enter
        var s1 = strategy.OnMarketData(MakeBar(10, 80m));
        Assert.Single(s1.OfType<SignalEvent>().Where(s => s.Direction == Direction.Long));

        // Second drop — should NOT enter again
        var s2 = strategy.OnMarketData(MakeBar(11, 75m));
        Assert.Empty(s2.OfType<SignalEvent>().Where(s => s.Direction == Direction.Long));
    }

    [Fact]
    public void NoExitSignal_WhenNotLong()
    {
        var strategy = new ZScoreMeanReversionStrategy(lookback: 10, entryThreshold: 2.0m, exitThreshold: 0.0m);

        // Build mean and stay flat — z-score above 0 should not produce Flat signal
        for (int i = 0; i < 10; i++)
        {
            strategy.OnMarketData(MakeBar(i, 100m));
        }

        var signals = strategy.OnMarketData(MakeBar(10, 105m));
        Assert.Empty(signals.OfType<SignalEvent>().Where(s => s.Direction == Direction.Flat));
    }
}
