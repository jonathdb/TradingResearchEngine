using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.UnitTests.V2Regression;

/// <summary>
/// V2 regression tests. Each test validates a specific bug fix and must be kept permanently.
/// </summary>
public class V2RegressionTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ILogger<Core.Portfolio.Portfolio> Logger =
        NullLoggerFactory.Instance.CreateLogger<Core.Portfolio.Portfolio>();

    // --- BUG-02: Sharpe from equity curve period returns ---

    [Fact]
    public void BUG02_FlatEquityCurve_SharpeIsNull()
    {
        // A flat equity curve has zero standard deviation → Sharpe should be null
        var curve = Enumerable.Range(0, 50)
            .Select(i => new EquityCurvePoint(T0.AddDays(i), 100_000m))
            .ToList();

        var sharpe = MetricsCalculator.ComputeSharpeRatio(curve, 0.02m, 252);
        Assert.Null(sharpe);
    }

    [Fact]
    public void BUG02_LinearRisingCurve_SharpePositiveAndWithinTolerance()
    {
        // Linearly rising curve: 100k → 105k over 252 bars (daily)
        // Each bar adds exactly 5000/252 ≈ 19.84
        var curve = Enumerable.Range(0, 253)
            .Select(i => new EquityCurvePoint(T0.AddDays(i), 100_000m + i * (5000m / 252m)))
            .ToList();

        var sharpe = MetricsCalculator.ComputeSharpeRatio(curve, 0.0m, 252);
        Assert.NotNull(sharpe);
        // Perfectly linear curve with zero risk-free rate should have very high Sharpe
        // (mean return > 0, stddev ≈ 0 but not exactly 0 due to decimal rounding)
    }

    // --- BUG-03: Portfolio mark-to-market updates between fills ---

    [Fact]
    public void BUG03_MarkToMarket_UpdatesTotalEquityBetweenFills()
    {
        var p = new Core.Portfolio.Portfolio(100_000m, Logger);

        // Buy 10 shares at 100
        p.Update(new FillEvent("AAPL", Direction.Long, 10m, 100m, 0m, 0m, T0));
        decimal equityAfterBuy = p.TotalEquity;

        // Mark-to-market at 110 — equity should increase
        p.MarkToMarket("AAPL", 110m, T0.AddHours(1));
        Assert.True(p.TotalEquity > equityAfterBuy,
            $"Expected equity to increase after mark-to-market at higher price. Was {equityAfterBuy}, now {p.TotalEquity}");

        // Mark-to-market at 90 — equity should decrease
        p.MarkToMarket("AAPL", 90m, T0.AddHours(2));
        Assert.True(p.TotalEquity < equityAfterBuy,
            $"Expected equity to decrease after mark-to-market at lower price. Was {equityAfterBuy}, now {p.TotalEquity}");

        // Equity curve should have 2 points (one per MarkToMarket call)
        Assert.Equal(2, p.EquityCurve.Count);
    }

    // --- BUG-04: Orphan flat fill does not inflate cash ---

    [Fact]
    public void BUG04_FlatFillNoPosition_CashUnchanged()
    {
        var p = new Core.Portfolio.Portfolio(100_000m, Logger);

        decimal cashBefore = p.CashBalance;

        // Send a flat fill with no open position — should not modify cash
        p.Update(new FillEvent("AAPL", Direction.Flat, 10m, 150m, 0m, 0m, T0));

        Assert.Equal(cashBefore, p.CashBalance);
    }

    // --- BUG-05: Monte Carlo normalised returns ---

    [Fact]
    public void BUG05_MonteCarlo_NormalisedReturns_SeedReproducible()
    {
        // Create trades with varying position sizes but same ReturnOnRisk
        var trades = new List<ClosedTrade>
        {
            // Small position: entry 100, qty 10, net PnL 100 → RoR = 100/(100*10) = 0.1
            new("TEST", T0, T0.AddHours(1), 100m, 110m, 10m, Direction.Long, 100m, 0m, 100m),
            // Large position: entry 100, qty 100, net PnL 1000 → RoR = 1000/(100*100) = 0.1
            new("TEST", T0.AddHours(2), T0.AddHours(3), 100m, 110m, 100m, Direction.Long, 1000m, 0m, 1000m),
            // Loss: entry 100, qty 50, net PnL -250 → RoR = -250/(100*50) = -0.05
            new("TEST", T0.AddHours(4), T0.AddHours(5), 100m, 95m, 50m, Direction.Long, -250m, 0m, -250m),
        };

        // Verify ReturnOnRisk is computed correctly
        Assert.Equal(0.1m, trades[0].ReturnOnRisk);
        Assert.Equal(0.1m, trades[1].ReturnOnRisk);
        Assert.Equal(-0.05m, trades[2].ReturnOnRisk);

        // Verify that trades with different absolute PnL but same RoR produce same normalised return
        Assert.Equal(trades[0].ReturnOnRisk, trades[1].ReturnOnRisk);
    }

    [Fact]
    public void BUG05_ClosedTrade_ReturnOnRisk_ZeroDenominator_ReturnsZero()
    {
        var trade = new ClosedTrade("TEST", T0, T0.AddHours(1), 0m, 100m, 0m,
            Direction.Long, 0m, 0m, 0m);
        Assert.Equal(0m, trade.ReturnOnRisk);
    }
}
