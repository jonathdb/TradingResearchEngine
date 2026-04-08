using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.UnitTests.Risk;

public class PortfolioConstraintTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ILogger<DefaultRiskLayer> Logger =
        new NullLoggerFactory().CreateLogger<DefaultRiskLayer>();

    private static DefaultRiskLayer CreateRiskLayer(PortfolioConstraints? constraints = null)
    {
        var options = Options.Create(new RiskOptions { MaxExposurePercent = 100m });
        return new DefaultRiskLayer(options, Logger, new FixedQuantitySizingPolicy(10m), constraints);
    }

    private static PortfolioSnapshot MakeSnapshot(
        decimal equity = 100_000m,
        Dictionary<string, Position>? positions = null) =>
        new(positions ?? new Dictionary<string, Position>(), equity, equity);

    [Fact]
    public void MaxConcurrentPositions_AtLimit_RejectsOrder()
    {
        var constraints = new PortfolioConstraints { MaxConcurrentPositions = 2 };
        var rl = CreateRiskLayer(constraints);

        var positions = new Dictionary<string, Position>
        {
            ["AAPL"] = new("AAPL", 10m, 150m, 0m, 0m),
            ["MSFT"] = new("MSFT", 10m, 300m, 0m, 0m),
        };
        var snapshot = MakeSnapshot(positions: positions);
        var order = new OrderEvent("GOOG", Direction.Long, 5m, OrderType.Market, null, T0, false);

        var result = rl.EvaluateOrder(order, snapshot);
        Assert.Null(result); // rejected
    }

    [Fact]
    public void MaxConcurrentPositions_BelowLimit_Allows()
    {
        var constraints = new PortfolioConstraints { MaxConcurrentPositions = 3 };
        var rl = CreateRiskLayer(constraints);

        var positions = new Dictionary<string, Position>
        {
            ["AAPL"] = new("AAPL", 10m, 150m, 0m, 0m),
        };
        var snapshot = MakeSnapshot(positions: positions);
        var order = new OrderEvent("GOOG", Direction.Long, 5m, OrderType.Market, null, T0, false);

        var result = rl.EvaluateOrder(order, snapshot);
        Assert.NotNull(result);
    }

    [Fact]
    public void NoConstraints_AllowsOrder()
    {
        var rl = CreateRiskLayer(null);
        var order = new OrderEvent("AAPL", Direction.Long, 10m, OrderType.Market, null, T0, false);
        var result = rl.EvaluateOrder(order, MakeSnapshot());
        Assert.NotNull(result);
    }
}
