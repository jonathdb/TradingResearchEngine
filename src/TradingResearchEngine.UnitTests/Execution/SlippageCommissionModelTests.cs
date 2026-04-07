using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.Execution;

public class SlippageCommissionModelTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly OrderEvent SampleOrder = new("AAPL", Direction.Long, 100m, OrderType.Market, null, T0);
    private static readonly BarEvent SampleBar = new("AAPL", "1D", 100m, 105m, 99m, 102m, 1000m, T0);

    [Fact]
    public void ZeroSlippageModel_ReturnsZero()
    {
        var model = new ZeroSlippageModel();
        Assert.Equal(0m, model.ComputeAdjustment(SampleOrder, SampleBar));
    }

    [Fact]
    public void FixedSpreadSlippageModel_ReturnsConfiguredSpread()
    {
        var model = new FixedSpreadSlippageModel(0.05m);
        Assert.Equal(0.05m, model.ComputeAdjustment(SampleOrder, SampleBar));
    }

    [Fact]
    public void ZeroCommissionModel_ReturnsZero()
    {
        var model = new ZeroCommissionModel();
        Assert.Equal(0m, model.ComputeCommission(SampleOrder, 100m, 50m));
    }

    [Fact]
    public void PerTradeCommissionModel_ReturnsFlatFee()
    {
        var model = new PerTradeCommissionModel(9.99m);
        Assert.Equal(9.99m, model.ComputeCommission(SampleOrder, 100m, 50m));
        Assert.Equal(9.99m, model.ComputeCommission(SampleOrder, 200m, 1m));
    }

    [Fact]
    public void PerShareCommissionModel_ReturnsFeeTimesQuantity()
    {
        var model = new PerShareCommissionModel(0.01m);
        Assert.Equal(0.50m, model.ComputeCommission(SampleOrder, 100m, 50m));
        Assert.Equal(1.00m, model.ComputeCommission(SampleOrder, 100m, 100m));
    }
}
