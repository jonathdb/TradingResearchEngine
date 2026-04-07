using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Risk;

/// <summary>
/// Mandatory pipeline component. Every order must pass through the RiskLayer
/// before reaching the ExecutionHandler.
/// </summary>
public interface IRiskLayer
{
    /// <summary>
    /// Converts a <see cref="SignalEvent"/> into a sized <see cref="OrderEvent"/>,
    /// or returns <c>null</c> to discard the signal.
    /// </summary>
    OrderEvent? ConvertSignal(SignalEvent signal, PortfolioSnapshot snapshot);

    /// <summary>
    /// Validates and resizes an <see cref="OrderEvent"/> produced directly by a strategy,
    /// or returns <c>null</c> to discard the order.
    /// </summary>
    OrderEvent? EvaluateOrder(OrderEvent order, PortfolioSnapshot snapshot);
}
