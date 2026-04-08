using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.UnitTests.Risk;

public class PositionSizingPolicyTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static PortfolioSnapshot MakeSnapshot(decimal equity = 100_000m) =>
        new(new Dictionary<string, Position>(), equity, equity);

    private static SignalEvent MakeSignal(decimal? strength = 100m) =>
        new("TEST", Direction.Long, strength, T0);

    private static BarEvent MakeBar(decimal close = 100m) =>
        new("TEST", "1D", close, close + 5m, close - 5m, close, 1000m, T0);

    [Fact]
    public void FixedQuantity_ReturnsConfiguredQuantity()
    {
        var policy = new FixedQuantitySizingPolicy(50m);
        decimal qty = policy.ComputeSize(MakeSignal(), MakeSnapshot(), MakeBar());
        Assert.Equal(50m, qty);
    }

    [Fact]
    public void FixedDollarRisk_ComputesCorrectQuantity()
    {
        var policy = new FixedDollarRiskSizingPolicy(2000m);
        // price = 100, dollarRisk = 2000 → qty = floor(2000/100) = 20
        decimal qty = policy.ComputeSize(MakeSignal(100m), MakeSnapshot(), MakeBar(100m));
        Assert.Equal(20m, qty);
    }

    [Fact]
    public void PercentEquity_ComputesCorrectQuantity()
    {
        var policy = new PercentEquitySizingPolicy(0.02m);
        // equity = 100k, 2% = 2000, price = 100 → qty = floor(2000/100) = 20
        decimal qty = policy.ComputeSize(MakeSignal(100m), MakeSnapshot(100_000m), MakeBar(100m));
        Assert.Equal(20m, qty);
    }

    [Fact]
    public void PercentEquity_ZeroPrice_ReturnsZero()
    {
        var policy = new PercentEquitySizingPolicy(0.02m);
        decimal qty = policy.ComputeSize(MakeSignal(0m), MakeSnapshot(), MakeBar(0m));
        Assert.Equal(0m, qty);
    }

    [Fact]
    public void VolatilityTarget_ReturnsNonZeroAfterWarmup()
    {
        var policy = new VolatilityTargetSizingPolicy(0.10m, 3);
        // Feed 4 bars to warm up ATR
        for (int i = 0; i < 4; i++)
        {
            var bar = new BarEvent("TEST", "1D", 100m + i, 105m + i, 98m + i, 102m + i, 1000m, T0.AddDays(i));
            policy.ComputeSize(MakeSignal(), MakeSnapshot(), bar);
        }
        var lastBar = new BarEvent("TEST", "1D", 104m, 109m, 102m, 106m, 1000m, T0.AddDays(4));
        decimal qty = policy.ComputeSize(MakeSignal(), MakeSnapshot(), lastBar);
        Assert.True(qty > 0m);
    }
}
