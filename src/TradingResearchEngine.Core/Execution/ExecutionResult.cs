using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Execution;

/// <summary>
/// Canonical return type for <see cref="IExecutionHandler"/>.
/// <para>
/// Invariant: <see cref="Fill"/> is never null when <see cref="Outcome"/> is
/// <see cref="ExecutionOutcome.Filled"/> or <see cref="ExecutionOutcome.PartiallyFilled"/>.
/// </para>
/// </summary>
public sealed record ExecutionResult(
    ExecutionOutcome Outcome,
    FillEvent? Fill,
    decimal RemainingQuantity = 0m,
    string? RejectionReason = null);
