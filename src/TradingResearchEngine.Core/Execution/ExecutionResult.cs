using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Execution;

/// <summary>
/// Canonical return type for <see cref="IExecutionHandler"/>.
/// <para>
/// Invariant: <see cref="Fill"/> is never null when <see cref="Outcome"/> is
/// <see cref="ExecutionOutcome.Filled"/> or <see cref="ExecutionOutcome.PartiallyFilled"/>.
/// </para>
/// <para>
/// When a stop-limit order triggers but the limit is not reached, <see cref="TriggeredOrder"/>
/// carries the order with <c>StopTriggered = true</c> so that re-queuing preserves the triggered state.
/// </para>
/// </summary>
public sealed record ExecutionResult(
    ExecutionOutcome Outcome,
    FillEvent? Fill,
    decimal RemainingQuantity = 0m,
    string? RejectionReason = null,
    OrderEvent? TriggeredOrder = null);
