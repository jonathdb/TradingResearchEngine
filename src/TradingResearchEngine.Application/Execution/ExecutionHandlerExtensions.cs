using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>Convenience extensions for <see cref="IExecutionHandler"/>.</summary>
public static class ExecutionHandlerExtensions
{
    /// <summary>
    /// Executes the order and returns just the <see cref="FillEvent"/>, or null if not filled.
    /// Convenience wrapper for callers that don't need the full <see cref="ExecutionResult"/>.
    /// </summary>
    public static FillEvent? ExecuteSimple(this IExecutionHandler handler, OrderEvent order, MarketDataEvent market)
    {
        var result = handler.Execute(order, market);
        return result.Outcome is ExecutionOutcome.Filled or ExecutionOutcome.PartiallyFilled
            ? result.Fill
            : null;
    }
}
