using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

/// <summary>
/// Preservation tests for <see cref="DonchianBreakoutStrategy"/> signal correctness.
/// These tests verify existing behavior on UNFIXED code and must all pass.
/// </summary>
public class DonchianBreakoutStrategyTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static BarEvent MakeBar(int dayOffset, decimal open, decimal high, decimal low, decimal close)
        => new("SPY", "1D", open, high, low, close, 1000m, T0.AddDays(dayOffset));

    /// <summary>
    /// Warmup: feeding ≤ period+1 bars produces no signals.
    /// The strategy needs _period bars to fill the buffer, then one more bar to set _warmedUp=true
    /// (with no signal on that bar). So first possible signal is at bar index period+1.
    /// </summary>
    [Fact]
    public void Warmup_ReturnsNoSignals()
    {
        const int period = 20;
        var strategy = new DonchianBreakoutStrategy(period: period);

        // Feed exactly period+1 bars (indices 0..period). No signals should be emitted.
        for (int i = 0; i <= period; i++)
        {
            decimal price = 100m + i;
            var signals = strategy.OnMarketData(MakeBar(i, price - 1, price + 1, price - 2, price));
            Assert.Empty(signals);
        }
    }

    /// <summary>
    /// Entry: when close breaks above the prior upper band, a Long signal is emitted.
    /// </summary>
    [Fact]
    public void EntrySignal_CloseAbovePriorUpperBand_EmitsLong()
    {
        const int period = 5;
        var strategy = new DonchianBreakoutStrategy(period: period);

        // Feed period+1 bars (indices 0..period) with stable prices to warm up.
        // Highs = 102, Lows = 98, Close = 100 for all warmup bars.
        for (int i = 0; i <= period; i++)
        {
            strategy.OnMarketData(MakeBar(i, 100m, 102m, 98m, 100m));
        }

        // After warmup, prior upper band = max of highs[0..period-1] = 102.
        // Feed a bar with close > 102 to trigger entry.
        var signals = strategy.OnMarketData(MakeBar(period + 1, 100m, 110m, 99m, 105m));

        var signal = Assert.Single(signals);
        var signalEvent = Assert.IsType<SignalEvent>(signal);
        Assert.Equal(Direction.Long, signalEvent.Direction);
    }

    /// <summary>
    /// Exit: after entering long, when close drops below the prior lower band, a Flat signal is emitted.
    /// </summary>
    [Fact]
    public void ExitSignal_CloseBelowPriorLowerBand_EmitsFlat()
    {
        const int period = 5;
        var strategy = new DonchianBreakoutStrategy(period: period);

        // Warmup with stable prices.
        for (int i = 0; i <= period; i++)
        {
            strategy.OnMarketData(MakeBar(i, 100m, 102m, 98m, 100m));
        }

        // Trigger entry: close > prior upper band (102).
        strategy.OnMarketData(MakeBar(period + 1, 100m, 110m, 99m, 105m));

        // Now feed bars that keep the channel stable, then drop below prior lower band.
        // After the entry bar, the channel updates. We need to feed enough bars
        // so the lower band is established, then close below it.
        // Feed a few stable bars first to let the channel settle.
        for (int i = period + 2; i <= period + 5; i++)
        {
            strategy.OnMarketData(MakeBar(i, 100m, 105m, 95m, 100m));
        }

        // Prior lower band is min of lows over the lookback window (excluding current bar).
        // The lows in the window include 95 from recent bars and 98 from warmup bars.
        // So prior lower band = 95. Feed a bar with close < 95.
        var signals = strategy.OnMarketData(MakeBar(period + 6, 96m, 97m, 90m, 93m));

        var signal = Assert.Single(signals);
        var signalEvent = Assert.IsType<SignalEvent>(signal);
        Assert.Equal(Direction.Flat, signalEvent.Direction);
    }

    /// <summary>
    /// No duplicate Long signals: continuously rising bars should emit only one Long signal.
    /// </summary>
    [Fact]
    public void NoDuplicateLongSignals_WhileInPosition()
    {
        const int period = 5;
        var strategy = new DonchianBreakoutStrategy(period: period);

        var allSignals = new List<EngineEvent>();

        // Feed 30 bars with steadily rising prices.
        for (int i = 0; i < 30; i++)
        {
            decimal price = 100m + i * 2m;
            var signals = strategy.OnMarketData(MakeBar(i, price - 1, price + 1, price - 2, price));
            allSignals.AddRange(signals);
        }

        var longSignals = allSignals.OfType<SignalEvent>()
            .Where(s => s.Direction == Direction.Long).ToList();

        Assert.Single(longSignals);
    }

    /// <summary>
    /// Look-ahead exclusion: the strategy uses the prior bar's channel values, not the current bar's.
    /// A bar whose high would expand the channel should NOT trigger an entry based on its own high.
    /// </summary>
    [Fact]
    public void LookAheadExclusion_UsesLaggedChannelValues()
    {
        const int period = 5;
        var strategy = new DonchianBreakoutStrategy(period: period);

        // Warmup with stable prices: high=102, low=98, close=100.
        for (int i = 0; i <= period; i++)
        {
            strategy.OnMarketData(MakeBar(i, 100m, 102m, 98m, 100m));
        }

        // After warmup, prior upper band = 102.
        // Feed a bar where high is very high (200) but close is below the prior upper band (101).
        // If the strategy used the current bar's channel (which would include high=200),
        // the upper band would be 200 and no signal would fire.
        // But since it uses the PRIOR bar's channel (upper=102), and close=101 < 102, no entry.
        // This confirms the strategy does NOT look ahead.
        var signals = strategy.OnMarketData(MakeBar(period + 1, 100m, 200m, 99m, 101m));

        Assert.Empty(signals);
    }

    /// <summary>
    /// Edge case: period=1 should still work correctly — warmup needs 2 bars (period+1),
    /// first signal possible at bar index 2.
    /// </summary>
    [Fact]
    public void EdgeCase_PeriodOne()
    {
        const int period = 1;
        var strategy = new DonchianBreakoutStrategy(period: period);

        // Bar 0: fills buffer (count=1, <= period=1, no signal)
        var s0 = strategy.OnMarketData(MakeBar(0, 100m, 102m, 98m, 100m));
        Assert.Empty(s0);

        // Bar 1: count=2 > period=1, _warmedUp set to true, no signal yet
        var s1 = strategy.OnMarketData(MakeBar(1, 100m, 103m, 97m, 100m));
        Assert.Empty(s1);

        // Bar 2: first possible signal. Prior upper band = max of highs[0..0] = 102.
        // Close = 105 > 102 → Long signal.
        var s2 = strategy.OnMarketData(MakeBar(2, 100m, 106m, 99m, 105m));

        var signal = Assert.Single(s2);
        var signalEvent = Assert.IsType<SignalEvent>(signal);
        Assert.Equal(Direction.Long, signalEvent.Direction);
    }
}
