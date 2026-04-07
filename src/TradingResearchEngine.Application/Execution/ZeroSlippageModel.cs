using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>Slippage model that applies zero price adjustment. Used as the default fallback.</summary>
public sealed class ZeroSlippageModel : ISlippageModel
{
    /// <inheritdoc/>
    public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market) => 0m;
}
