using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>Applies a fixed half-spread as adverse slippage on every fill.</summary>
public sealed class FixedSpreadSlippageModel : ISlippageModel
{
    private readonly decimal _halfSpread;

    /// <param name="halfSpread">The fixed price adjustment applied per fill (e.g. 0.01 for 1 cent).</param>
    public FixedSpreadSlippageModel(decimal halfSpread) => _halfSpread = halfSpread;

    /// <inheritdoc/>
    public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market) => _halfSpread;
}
