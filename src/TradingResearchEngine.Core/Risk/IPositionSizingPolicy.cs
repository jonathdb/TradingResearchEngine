using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Risk;

/// <summary>
/// Computes position size given a signal, portfolio state, and market data.
/// Strategies must not contain sizing logic — sizing is handled by the policy.
/// </summary>
public interface IPositionSizingPolicy
{
    /// <summary>Returns the quantity to trade. Returns 0 to skip the trade.</summary>
    decimal ComputeSize(SignalEvent signal, PortfolioSnapshot snapshot, MarketDataEvent market);
}
