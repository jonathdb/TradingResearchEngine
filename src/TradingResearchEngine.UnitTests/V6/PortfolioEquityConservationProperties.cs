using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 10: PortfolioEquityConservationWithShorts
/// TotalEquity == Cash + longUnrealised + shortUnrealised at all times.
/// **Validates: Requirements 1.3, 1.4, 1.5, 1.6**
/// </summary>
public class PortfolioEquityConservationProperties
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ILogger<Core.Portfolio.Portfolio> Logger =
        NullLoggerFactory.Instance.CreateLogger<Core.Portfolio.Portfolio>();

    [Property(MaxTest = 100)]
    public bool PortfolioEquity_IncludesShortUnrealisedPnl(
        PositiveInt longPriceWrap, PositiveInt shortPriceWrap, PositiveInt currentPriceWrap)
    {
        decimal longEntryPrice = (decimal)longPriceWrap.Get / 100m + 0.01m;
        decimal shortEntryPrice = (decimal)shortPriceWrap.Get / 100m + 0.01m;
        decimal currentPrice = (decimal)currentPriceWrap.Get / 100m + 0.01m;
        decimal qty = 10m;
        decimal initialCash = 1_000_000m;

        var portfolio = new Core.Portfolio.Portfolio(initialCash, Logger);

        // Open a long position
        portfolio.Update(new FillEvent("LONG", Direction.Long, qty, longEntryPrice, 0m, 0m, T0));
        // Open a short position
        portfolio.Update(new FillEvent("SHORT", Direction.Short, qty, shortEntryPrice, 0m, 0m, T0));

        // Mark both to market at current price
        portfolio.MarkToMarket("LONG", currentPrice, T0.AddHours(1));
        portfolio.MarkToMarket("SHORT", currentPrice, T0.AddHours(2));

        // Compute expected values
        decimal expectedCash = initialCash - (longEntryPrice * qty) + (shortEntryPrice * qty);
        decimal longUnrealised = (currentPrice - longEntryPrice) * qty;
        decimal shortUnrealised = (shortEntryPrice - currentPrice) * qty;
        decimal expectedEquity = expectedCash + longUnrealised + shortUnrealised;

        return portfolio.CashBalance == expectedCash
            && Math.Abs(portfolio.TotalEquity - expectedEquity) < 0.01m;
    }
}
