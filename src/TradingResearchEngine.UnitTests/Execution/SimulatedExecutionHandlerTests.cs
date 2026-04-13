using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.Execution;

public class SimulatedExecutionHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Execute_AppliesSlippageAndCommission_ToFillPrice()
    {
        var slippage = new FixedSpreadSlippageModel(0.10m);
        var commission = new PerTradeCommissionModel(5m);
        var handler = new SimulatedExecutionHandler(slippage, commission,
            NullLogger<SimulatedExecutionHandler>.Instance);

        var order = new OrderEvent("AAPL", Direction.Long, 100m, OrderType.Market, null, T0, true);
        var bar = new BarEvent("AAPL", "1D", 100m, 105m, 99m, 102m, 1000m, T0);

        var result = handler.Execute(order, bar);

        Assert.Equal(ExecutionOutcome.Filled, result.Outcome);
        Assert.NotNull(result.Fill);
        Assert.Equal(102.10m, result.Fill.FillPrice); // close 102 + slippage 0.10
        Assert.Equal(5m, result.Fill.Commission);
        Assert.Equal(0.10m, result.Fill.SlippageAmount);
        Assert.Equal(100m, result.Fill.Quantity);
    }

    [Fact]
    public void Execute_FlatOrder_SubtractsSlippage()
    {
        var slippage = new FixedSpreadSlippageModel(0.05m);
        var commission = new ZeroCommissionModel();
        var handler = new SimulatedExecutionHandler(slippage, commission,
            NullLogger<SimulatedExecutionHandler>.Instance);

        var order = new OrderEvent("AAPL", Direction.Flat, 50m, OrderType.Market, null, T0, true);
        var bar = new BarEvent("AAPL", "1D", 100m, 105m, 99m, 102m, 1000m, T0);

        var result = handler.Execute(order, bar);

        Assert.NotNull(result.Fill);
        Assert.Equal(101.95m, result.Fill.FillPrice); // close 102 - slippage 0.05
    }

    [Fact]
    public void Execute_TickEvent_UsesLastTradePrice()
    {
        var slippage = new ZeroSlippageModel();
        var commission = new ZeroCommissionModel();
        var handler = new SimulatedExecutionHandler(slippage, commission,
            NullLogger<SimulatedExecutionHandler>.Instance);

        var order = new OrderEvent("AAPL", Direction.Long, 10m, OrderType.Market, null, T0, true);
        var tick = new TickEvent("AAPL",
            new[] { new BidLevel(101m, 100m) },
            new[] { new AskLevel(102m, 100m) },
            new LastTrade(101.50m, 50m, T0), T0);

        var result = handler.Execute(order, tick);

        Assert.NotNull(result.Fill);
        Assert.Equal(101.50m, result.Fill.FillPrice);
    }
}
