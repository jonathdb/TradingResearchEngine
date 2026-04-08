using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Application.Risk;

/// <summary>Risks a fixed dollar amount per trade. Quantity = dollarRisk / referencePrice.</summary>
public sealed class FixedDollarRiskSizingPolicy : IPositionSizingPolicy
{
    private readonly decimal _dollarRisk;

    /// <param name="dollarRisk">Fixed dollar risk per trade (default 1000).</param>
    public FixedDollarRiskSizingPolicy(decimal dollarRisk = 1000m) => _dollarRisk = dollarRisk;

    /// <inheritdoc/>
    public decimal ComputeSize(SignalEvent signal, PortfolioSnapshot snapshot, MarketDataEvent market)
    {
        decimal price = signal.Strength ?? GetPrice(market);
        return price > 0m ? Math.Floor(_dollarRisk / price) : 0m;
    }

    private static decimal GetPrice(MarketDataEvent market) => market switch
    {
        BarEvent bar => bar.Close,
        TickEvent tick => tick.LastTrade.Price,
        _ => 0m
    };
}
