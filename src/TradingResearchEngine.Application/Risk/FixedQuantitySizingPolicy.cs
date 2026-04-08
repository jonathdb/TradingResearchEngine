using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Application.Risk;

/// <summary>Always returns a configured fixed quantity.</summary>
public sealed class FixedQuantitySizingPolicy : IPositionSizingPolicy
{
    private readonly decimal _quantity;

    /// <param name="quantity">Fixed quantity per trade (default 100).</param>
    public FixedQuantitySizingPolicy(decimal quantity = 100m) => _quantity = quantity;

    /// <inheritdoc/>
    public decimal ComputeSize(SignalEvent signal, PortfolioSnapshot snapshot, MarketDataEvent market)
        => _quantity;
}
