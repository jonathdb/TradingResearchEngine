using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Execution;

/// <summary>
/// Converts a risk-approved <see cref="OrderEvent"/> into an <see cref="ExecutionResult"/>.
/// <para>
/// Invariant: <see cref="ExecutionResult.Fill"/> is never null when
/// <see cref="ExecutionResult.Outcome"/> is <see cref="ExecutionOutcome.Filled"/>
/// or <see cref="ExecutionOutcome.PartiallyFilled"/>.
/// </para>
/// </summary>
public interface IExecutionHandler
{
    /// <summary>Executes the order against the current market state and returns an execution result.</summary>
    ExecutionResult Execute(OrderEvent order, MarketDataEvent currentBar);
}
