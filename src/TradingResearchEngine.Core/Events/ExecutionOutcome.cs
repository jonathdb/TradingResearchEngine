namespace TradingResearchEngine.Core.Events;

/// <summary>Outcome of an order execution attempt.</summary>
public enum ExecutionOutcome
{
    /// <summary>Order fully filled.</summary>
    Filled,

    /// <summary>Order partially filled. Remaining quantity carried forward.</summary>
    PartiallyFilled,

    /// <summary>Order not filled this bar. Remains in pending queue.</summary>
    Unfilled,

    /// <summary>Order rejected (session closed, insufficient capital, invalid stop, etc.).</summary>
    Rejected,

    /// <summary>Order expired after exceeding MaxBarsPending.</summary>
    Expired
}
