using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.Portfolio;

public class PortfolioTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ILogger<Core.Portfolio.Portfolio> Logger =
        NullLoggerFactory.Instance.CreateLogger<Core.Portfolio.Portfolio>();

    private static Core.Portfolio.Portfolio CreatePortfolio(decimal cash = 100_000m) => new(cash, Logger);

    [Fact]
    public void BuyFill_ReducesCash()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 150m, 5m, 0m, T0));

        // cost = 150 * 10 + 5 = 1505
        Assert.Equal(100_000m - 1505m, p.CashBalance);
    }

    [Fact]
    public void SellFill_IncreasesCash()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 150m, 0m, 0m, T0));
        var cashAfterBuy = p.CashBalance;

        p.Update(new FillEvent("AAPL", Direction.Flat, 10m, 160m, 5m, 0m, T0.AddHours(1)));

        // proceeds = 160 * 10 - 5 = 1595
        Assert.Equal(cashAfterBuy + 1595m, p.CashBalance);
    }

    [Fact]
    public void MarginBreach_ClampsCashToZero()
    {
        var p = CreatePortfolio(100m);
        // cost = 200 * 10 + 0 = 2000, way more than 100
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 200m, 0m, 0m, T0));

        Assert.Equal(0m, p.CashBalance);
    }

    [Fact]
    public void RealisedPnl_ComputedOnPositionClose()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 100m, 0m, 0m, T0));
        p.Update(new FillEvent("AAPL", Direction.Flat, 10m, 110m, 0m, 0m, T0.AddHours(1)));

        Assert.Single(p.ClosedTrades);
        Assert.Equal(100m, p.ClosedTrades[0].NetPnl); // (110-100)*10
    }

    [Fact]
    public void EquityCurve_AppendedByMarkToMarket()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Long, 5m, 100m, 0m, 0m, T0));

        // Fills no longer append to equity curve — MarkToMarket does
        Assert.Empty(p.EquityCurve);

        // MarkToMarket appends one point per call
        p.MarkToMarket("AAPL", 105m, T0.AddHours(1));
        p.MarkToMarket("AAPL", 110m, T0.AddHours(2));

        Assert.Equal(2, p.EquityCurve.Count);
        Assert.True(p.EquityCurve[1].TotalEquity > p.EquityCurve[0].TotalEquity);
    }

    [Fact]
    public void TakeSnapshot_ReturnsCurrentState()
    {
        var p = CreatePortfolio();
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 100m, 0m, 0m, T0));

        var snap = p.TakeSnapshot();
        Assert.True(snap.Positions.ContainsKey("AAPL"));
        Assert.Equal(p.CashBalance, snap.CashBalance);
    }

    [Fact]
    public void UnrealisedPnl_UpdatedAfterFill()
    {
        var p = CreatePortfolio();
        // Buy at 100
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 100m, 0m, 0m, T0));
        // Buy more at 120 — mark-to-market at 120
        p.Update(new FillEvent("AAPL", Direction.Long, 5m, 120m, 0m, 0m, T0.AddHours(1)));

        var pos = p.Positions["AAPL"];
        // After second fill, unrealised PnL should reflect mark-to-market at 120
        // avg entry = (100*10 + 120*5) / 15 = 1600/15 ≈ 106.67
        // unrealised = (120 - 106.67) * 15 ≈ 200
        Assert.True(pos.UnrealisedPnl > 0m);
    }
}
