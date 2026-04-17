using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.UnitTests.V6;

public class ShortSellingTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ILogger<Core.Portfolio.Portfolio> PortfolioLogger =
        NullLoggerFactory.Instance.CreateLogger<Core.Portfolio.Portfolio>();

    private static Core.Portfolio.Portfolio CreatePortfolio(decimal cash = 100_000m) => new(cash, PortfolioLogger);

    [Fact]
    public void ShortOpen_IncreasesCashByProceeds()
    {
        var p = CreatePortfolio();
        // Short 10 shares at 150, commission 5
        p.Update(new FillEvent("AAPL", Direction.Short, 10m, 150m, 5m, 0m, T0));

        // proceeds = 150 * 10 - 5 = 1495
        Assert.Equal(100_000m + 1495m, p.CashBalance);
        Assert.True(p.ShortPositions.ContainsKey("AAPL"));
        Assert.Equal(10m, p.ShortPositions["AAPL"].Quantity);
    }

    [Fact]
    public void ShortClose_CorrectPnl()
    {
        var p = CreatePortfolio();
        // Short at 150
        p.Update(new FillEvent("AAPL", Direction.Short, 10m, 150m, 0m, 0m, T0));
        // Cover at 140 → profit
        p.Update(new FillEvent("AAPL", Direction.Flat, 10m, 140m, 0m, 0m, T0.AddHours(1)));

        Assert.Single(p.ClosedTrades);
        var trade = p.ClosedTrades[0];
        // PnL = (entry - exit) × qty = (150 - 140) × 10 = 100
        Assert.Equal(100m, trade.GrossPnl);
        Assert.Equal(Direction.Short, trade.Direction);
        Assert.False(p.ShortPositions.ContainsKey("AAPL"));
    }

    [Fact]
    public void ShortClose_LossScenario()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Short, 10m, 100m, 0m, 0m, T0));
        // Cover at 110 → loss
        p.Update(new FillEvent("AAPL", Direction.Flat, 10m, 110m, 0m, 0m, T0.AddHours(1)));

        var trade = p.ClosedTrades[0];
        // PnL = (100 - 110) × 10 = -100
        Assert.Equal(-100m, trade.GrossPnl);
    }

    [Fact]
    public void ShortMarkToMarket_TotalEquityMovesInverselyToPrice()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Short, 10m, 100m, 0m, 0m, T0));

        // Price goes up → short loses → equity decreases
        p.MarkToMarket("AAPL", 110m, T0.AddHours(1));
        var equityAfterUp = p.TotalEquity;

        // Price goes down → short gains → equity increases
        p.MarkToMarket("AAPL", 90m, T0.AddHours(2));
        var equityAfterDown = p.TotalEquity;

        Assert.True(equityAfterDown > equityAfterUp);
    }

    [Fact]
    public void ShortPosition_UnrealisedPnl_Correct()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Short, 10m, 100m, 0m, 0m, T0));
        p.MarkToMarket("AAPL", 90m, T0.AddHours(1));

        // Short unrealised PnL = (entry - current) × qty = (100 - 90) × 10 = 100
        var shortPos = p.ShortPositions["AAPL"];
        Assert.Equal(100m, shortPos.UnrealisedPnl);
    }

    [Fact]
    public void OpenPositionCount_IncludesBothLongAndShort()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 100m, 0m, 0m, T0));
        p.Update(new FillEvent("MSFT", Direction.Short, 5m, 200m, 0m, 0m, T0));

        Assert.Equal(2, p.OpenPositionCount);
    }

    [Fact]
    public void GetExposureBySymbol_IncludesBothLongAndShort()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 100m, 0m, 0m, T0));
        p.Update(new FillEvent("MSFT", Direction.Short, 5m, 200m, 0m, 0m, T0));

        var exposure = p.GetExposureBySymbol();
        Assert.True(exposure.ContainsKey("AAPL"));
        Assert.True(exposure.ContainsKey("MSFT"));
        Assert.True(exposure["AAPL"] > 0m);
        Assert.True(exposure["MSFT"] > 0m);
    }

    [Fact]
    public void TakeSnapshot_IncludesShortPositions()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Short, 10m, 100m, 0m, 0m, T0));

        var snap = p.TakeSnapshot();
        Assert.NotNull(snap.ShortPositions);
        Assert.True(snap.ShortPositions!.ContainsKey("AAPL"));
    }

    [Fact]
    public void LongOnlyGuard_DirectionShort_NoLongerThrowsInExecution()
    {
        // Verify SimulatedExecutionHandler no longer throws on Direction.Short
        var slippage = new Mock<ISlippageModel>();
        slippage.Setup(s => s.ComputeAdjustment(It.IsAny<OrderEvent>(), It.IsAny<MarketDataEvent>()))
            .Returns(0m);
        var commission = new Mock<ICommissionModel>();
        commission.Setup(c => c.ComputeCommission(It.IsAny<OrderEvent>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(0m);
        var logger = NullLoggerFactory.Instance.CreateLogger<SimulatedExecutionHandler>();

        var handler = new SimulatedExecutionHandler(slippage.Object, commission.Object, logger);
        var order = new OrderEvent("AAPL", Direction.Short, 10m, OrderType.Market, null, T0);
        var bar = new BarEvent("AAPL", "1D", 100m, 105m, 95m, 100m, 1000m, T0);

        var result = handler.Execute(order, bar);
        Assert.Equal(ExecutionOutcome.Filled, result.Outcome);
        Assert.NotNull(result.Fill);
    }

    [Fact]
    public void ShortFill_PriceIsBasePriceMinusSlippage()
    {
        var slippage = new Mock<ISlippageModel>();
        slippage.Setup(s => s.ComputeAdjustment(It.IsAny<OrderEvent>(), It.IsAny<MarketDataEvent>()))
            .Returns(0.50m);
        var commission = new Mock<ICommissionModel>();
        commission.Setup(c => c.ComputeCommission(It.IsAny<OrderEvent>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(0m);
        var logger = NullLoggerFactory.Instance.CreateLogger<SimulatedExecutionHandler>();

        var handler = new SimulatedExecutionHandler(slippage.Object, commission.Object, logger);
        var order = new OrderEvent("AAPL", Direction.Short, 10m, OrderType.Market, null, T0);
        var bar = new BarEvent("AAPL", "1D", 100m, 105m, 95m, 100m, 1000m, T0);

        var result = handler.Execute(order, bar);
        // Short fill: basePrice - slippage = 100 - 0.50 = 99.50
        Assert.Equal(99.50m, result.Fill!.FillPrice);
    }

    [Fact]
    public void DefaultRiskLayer_ShortSignal_CreatesShortOrder()
    {
        var options = Options.Create(new RiskOptions { MaxExposurePercent = 100m });
        var logger = NullLoggerFactory.Instance.CreateLogger<DefaultRiskLayer>();
        var sizing = new FixedQuantitySizingPolicy(10m);
        var riskLayer = new DefaultRiskLayer(options, logger, sizing);

        var snapshot = new PortfolioSnapshot(
            new Dictionary<string, Position>(),
            100_000m,
            100_000m,
            new Dictionary<string, Position>());

        var signal = new SignalEvent("AAPL", Direction.Short, 100m, T0);
        var order = riskLayer.ConvertSignal(signal, snapshot);

        Assert.NotNull(order);
        Assert.Equal(Direction.Short, order!.Direction);
        Assert.Equal(10m, order.Quantity);
    }

    [Fact]
    public void DefaultRiskLayer_FlatSignal_ClosesShortPosition()
    {
        var options = Options.Create(new RiskOptions { MaxExposurePercent = 100m });
        var logger = NullLoggerFactory.Instance.CreateLogger<DefaultRiskLayer>();
        var sizing = new FixedQuantitySizingPolicy(10m);
        var riskLayer = new DefaultRiskLayer(options, logger, sizing);

        var shortPositions = new Dictionary<string, Position>
        {
            ["AAPL"] = new("AAPL", 10m, 100m, 0m, 0m)
        };
        var snapshot = new PortfolioSnapshot(
            new Dictionary<string, Position>(),
            100_000m,
            100_000m,
            shortPositions);

        var signal = new SignalEvent("AAPL", Direction.Flat, 100m, T0);
        var order = riskLayer.ConvertSignal(signal, snapshot);

        Assert.NotNull(order);
        Assert.Equal(Direction.Flat, order!.Direction);
        Assert.Equal(10m, order.Quantity);
    }
}
