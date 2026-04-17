using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingResearchEngine.Application.Execution;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 15: SlippageDirectionSymmetry
/// Short fill: fillPrice = basePrice - slippageAmount; Long fill: fillPrice = basePrice + slippageAmount.
/// **Validates: Requirements 2.1, 2.2**
/// </summary>
public class SlippageDirectionSymmetryProperties
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Property(MaxTest = 100)]
    public bool SlippageDirection_LongAddsShortSubtracts(PositiveInt priceWrap, PositiveInt slipWrap)
    {
        decimal basePrice = (decimal)priceWrap.Get / 100m + 0.01m;
        decimal slippageAmount = (decimal)(slipWrap.Get % 100) / 100m; // keep slippage reasonable

        var slippage = new Mock<ISlippageModel>();
        slippage.Setup(s => s.ComputeAdjustment(It.IsAny<OrderEvent>(), It.IsAny<MarketDataEvent>()))
            .Returns(slippageAmount);
        var commission = new Mock<ICommissionModel>();
        commission.Setup(c => c.ComputeCommission(It.IsAny<OrderEvent>(), It.IsAny<decimal>(), It.IsAny<decimal>()))
            .Returns(0m);
        var logger = NullLoggerFactory.Instance.CreateLogger<SimulatedExecutionHandler>();

        var handler = new SimulatedExecutionHandler(slippage.Object, commission.Object, logger);
        var bar = new BarEvent("TEST", "1D", basePrice, basePrice, basePrice, basePrice, 1000m, T0);

        var longOrder = new OrderEvent("TEST", Direction.Long, 10m, OrderType.Market, null, T0);
        var shortOrder = new OrderEvent("TEST", Direction.Short, 10m, OrderType.Market, null, T0);

        var longResult = handler.Execute(longOrder, bar);
        var shortResult = handler.Execute(shortOrder, bar);

        var longFillPrice = longResult.Fill!.FillPrice;
        var shortFillPrice = shortResult.Fill!.FillPrice;

        // Long: base + slippage, Short: base - slippage
        return longFillPrice == basePrice + slippageAmount
            && shortFillPrice == basePrice - slippageAmount;
    }
}
