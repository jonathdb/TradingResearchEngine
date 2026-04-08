using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Application.Risk;

/// <summary>Risks a percentage of current equity per trade. Quantity = (equity * percent) / price.</summary>
public sealed class PercentEquitySizingPolicy : IPositionSizingPolicy
{
    private readonly decimal _percent;

    /// <param name="percent">Percentage of equity to risk per trade (default 0.02 = 2%).</param>
    public PercentEquitySizingPolicy(decimal percent = 0.02m) => _percent = percent;

    /// <inheritdoc/>
    public decimal ComputeSize(SignalEvent signal, PortfolioSnapshot snapshot, MarketDataEvent market)
    {
        decimal price = signal.Strength ?? GetPrice(market);
        if (price <= 0m) return 0m;
        decimal riskBudget = snapshot.TotalEquity * _percent;
        return Math.Floor(riskBudget / price);
    }

    private static decimal GetPrice(MarketDataEvent market) => market switch
    {
        BarEvent bar => bar.Close,
        TickEvent tick => tick.LastTrade.Price,
        _ => 0m
    };
}
