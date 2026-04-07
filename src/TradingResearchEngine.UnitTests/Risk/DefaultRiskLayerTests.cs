using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.UnitTests.Risk;

public class DefaultRiskLayerTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DefaultRiskLayer CreateLayer(decimal maxExposure = 10m) =>
        new(Options.Create(new RiskOptions { MaxExposurePercent = maxExposure }),
            NullLoggerFactory.Instance.CreateLogger<DefaultRiskLayer>());

    private static PortfolioSnapshot EmptySnapshot(decimal equity = 100_000m) =>
        new(new Dictionary<string, Position>(), equity, equity);

    [Fact]
    public void EvaluateOrder_ZeroQuantity_ReturnsNull_RiskRejection()
    {
        var layer = CreateLayer();
        var order = new OrderEvent("AAPL", Direction.Long, 0m, OrderType.Market, null, T0);

        Assert.Null(layer.EvaluateOrder(order, EmptySnapshot()));
    }

    [Fact]
    public void EvaluateOrder_WithinExposure_ReturnsOrder()
    {
        var layer = CreateLayer(50m); // 50% max exposure
        var order = new OrderEvent("AAPL", Direction.Long, 10m, OrderType.Market, null, T0);

        var result = layer.EvaluateOrder(order, EmptySnapshot());
        Assert.NotNull(result);
    }

    [Fact]
    public void EvaluateOrder_AtMaxExposure_ReturnsNull()
    {
        var layer = CreateLayer(10m); // 10% = $10k max
        var positions = new Dictionary<string, Position>
        {
            ["AAPL"] = new("AAPL", 100m, 100m, 0m, 0m) // 100 * 100 = $10k exposure
        };
        var snapshot = new PortfolioSnapshot(positions, 90_000m, 100_000m);

        var order = new OrderEvent("MSFT", Direction.Long, 10m, OrderType.Market, null, T0);
        Assert.Null(layer.EvaluateOrder(order, snapshot));
    }

    [Fact]
    public void ConvertSignal_FlatDirection_ReturnsNull()
    {
        var layer = CreateLayer();
        var signal = new SignalEvent("AAPL", Direction.Flat, null, T0);

        Assert.Null(layer.ConvertSignal(signal, EmptySnapshot()));
    }

    [Fact]
    public void ConvertSignal_WithStrengthAsPrice_ComputesQuantity()
    {
        var layer = CreateLayer(100m); // generous exposure
        // equity 100k, 2% fraction = $2000 budget, price hint 50 → qty = 40
        var signal = new SignalEvent("AAPL", Direction.Long, 50m, T0);

        var order = layer.ConvertSignal(signal, EmptySnapshot());
        Assert.NotNull(order);
        Assert.Equal(40m, order!.Quantity);
    }
}
