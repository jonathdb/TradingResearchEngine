using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Execution;

/// <summary>
/// Computes a fill-price adjustment for an order given current market state.
/// A positive value represents adverse slippage.
/// </summary>
public interface ISlippageModel
{
    /// <summary>Returns the decimal price adjustment to apply to the fill price.</summary>
    decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market);
}
