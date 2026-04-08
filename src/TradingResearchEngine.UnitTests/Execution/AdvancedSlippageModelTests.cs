using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.Sessions;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.Execution;

public class AdvancedSlippageModelTests
{
    private static readonly DateTimeOffset T0 = new(2024, 6, 3, 14, 0, 0, TimeSpan.Zero); // Monday 14:00 UTC
    private static OrderEvent MakeOrder() => new("TEST", Direction.Long, 10m, OrderType.Market, null, T0, true);

    [Fact]
    public void PercentOfPrice_ReturnsCorrectBps()
    {
        var model = new PercentOfPriceSlippageModel(10m); // 10 bps
        var bar = new BarEvent("TEST", "1D", 100m, 105m, 99m, 100m, 1000m, T0);
        decimal slip = model.ComputeAdjustment(MakeOrder(), bar);
        Assert.Equal(0.10m, slip); // 100 * 10 / 10000
    }

    [Fact]
    public void PercentOfPrice_Deterministic()
    {
        var model = new PercentOfPriceSlippageModel(5m);
        var bar = new BarEvent("TEST", "1D", 200m, 210m, 195m, 200m, 1000m, T0);
        decimal s1 = model.ComputeAdjustment(MakeOrder(), bar);
        decimal s2 = model.ComputeAdjustment(MakeOrder(), bar);
        Assert.Equal(s1, s2);
    }

    [Fact]
    public void AtrScaled_WarmupReturnsNonZero()
    {
        var model = new AtrScaledSlippageModel(3, 0.5m);
        // Feed 3 bars to warm up ATR
        for (int i = 0; i < 3; i++)
        {
            var bar = new BarEvent("TEST", "1D", 100m + i, 105m + i, 98m + i, 102m + i, 1000m, T0.AddDays(i));
            model.ComputeAdjustment(MakeOrder(), bar);
        }
        var bar4 = new BarEvent("TEST", "1D", 103m, 108m, 101m, 105m, 1000m, T0.AddDays(3));
        decimal slip = model.ComputeAdjustment(MakeOrder(), bar4);
        Assert.True(slip > 0m);
    }

    [Fact]
    public void SessionAware_CoreSession_UsesBaseMultiplier()
    {
        var baseModel = new FixedSpreadSlippageModel(1.0m);
        var calendar = new ForexSessionCalendar();
        var model = new SessionAwareSlippageModel(baseModel, calendar, 1.0m, 3.0m);

        // Monday 14:00 UTC = Overlap (core session)
        var bar = new BarEvent("TEST", "1D", 100m, 105m, 99m, 100m, 1000m, T0);
        decimal slip = model.ComputeAdjustment(MakeOrder(), bar);
        Assert.Equal(1.0m, slip); // base * 1.0
    }

    [Fact]
    public void SessionAware_OffHours_WidensSlippage()
    {
        var baseModel = new FixedSpreadSlippageModel(1.0m);
        var calendar = new ForexSessionCalendar();
        var model = new SessionAwareSlippageModel(baseModel, calendar, 1.0m, 3.0m);

        // Monday 22:00 UTC = AfterHours (off-hours)
        var offHours = new DateTimeOffset(2024, 6, 3, 22, 0, 0, TimeSpan.Zero);
        var bar = new BarEvent("TEST", "1D", 100m, 105m, 99m, 100m, 1000m, offHours);
        var order = new OrderEvent("TEST", Direction.Long, 10m, OrderType.Market, null, offHours, true);
        decimal slip = model.ComputeAdjustment(order, bar);
        Assert.Equal(3.0m, slip); // base * 3.0
    }

    [Fact]
    public void VolatilityBucket_LowVol_ReturnsLowSlippage()
    {
        var model = new VolatilityBucketSlippageModel(5, 0.10m, 0.30m, 0.01m, 0.05m, 0.15m);
        // Feed 6 bars with very small price changes (low vol)
        for (int i = 0; i < 6; i++)
        {
            decimal close = 100m + i * 0.01m;
            var bar = new BarEvent("TEST", "1D", close, close + 0.01m, close - 0.01m, close, 1000m, T0.AddDays(i));
            model.ComputeAdjustment(MakeOrder(), bar);
        }
        var lastBar = new BarEvent("TEST", "1D", 100.06m, 100.07m, 100.05m, 100.06m, 1000m, T0.AddDays(6));
        decimal slip = model.ComputeAdjustment(MakeOrder(), lastBar);
        Assert.Equal(0.01m, slip); // low vol bucket
    }
}
