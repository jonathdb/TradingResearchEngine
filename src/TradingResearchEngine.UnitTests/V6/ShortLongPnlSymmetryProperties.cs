using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 9: ShortLongPnlSymmetry
/// A long trade and symmetric short trade on same price series produce equal-magnitude PnL.
/// **Validates: Requirements 1.2, 25.1**
/// </summary>
public class ShortLongPnlSymmetryProperties
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ILogger<Core.Portfolio.Portfolio> Logger =
        NullLoggerFactory.Instance.CreateLogger<Core.Portfolio.Portfolio>();

    [Property(MaxTest = 100)]
    public bool ShortLongPnlSymmetry_IdenticalMagnitude(PositiveInt entryWrap, PositiveInt exitWrap, PositiveInt qtyWrap)
    {
        decimal entryPrice = (decimal)entryWrap.Get / 100m + 0.01m; // ensure > 0
        decimal exitPrice = (decimal)exitWrap.Get / 100m + 0.01m;   // ensure > 0
        decimal qty = (decimal)(qtyWrap.Get % 1000) + 1m;           // ensure > 0, reasonable

        // Long trade: PnL = (exit - entry) × qty
        var longPortfolio = new Core.Portfolio.Portfolio(1_000_000m, Logger);
        longPortfolio.Update(new FillEvent("TEST", Direction.Long, qty, entryPrice, 0m, 0m, T0));
        longPortfolio.Update(new FillEvent("TEST", Direction.Flat, qty, exitPrice, 0m, 0m, T0.AddHours(1)));

        // Short trade: PnL = (entry - exit) × qty
        var shortPortfolio = new Core.Portfolio.Portfolio(1_000_000m, Logger);
        shortPortfolio.Update(new FillEvent("TEST", Direction.Short, qty, entryPrice, 0m, 0m, T0));
        shortPortfolio.Update(new FillEvent("TEST", Direction.Flat, qty, exitPrice, 0m, 0m, T0.AddHours(1)));

        var longPnl = longPortfolio.ClosedTrades[0].GrossPnl;
        var shortPnl = shortPortfolio.ClosedTrades[0].GrossPnl;

        // Equal magnitude, opposite sign
        return Math.Abs(longPnl) == Math.Abs(shortPnl) && longPnl == -shortPnl;
    }
}
