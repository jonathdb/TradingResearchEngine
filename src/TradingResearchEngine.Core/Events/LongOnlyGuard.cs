namespace TradingResearchEngine.Core.Events;

/// <summary>
/// Obsolete V5 guard that blocked <see cref="Direction.Short"/> at runtime.
/// V6+ supports full bidirectional execution (long and short) via
/// <see cref="Direction"/> enum handling in <c>SimulatedExecutionHandler</c>
/// and <c>DefaultRiskLayer</c>. Retained for backward compatibility only.
/// </summary>
[Obsolete("V6+ supports bidirectional execution. Use Direction enum with exhaustive switch handling. See SimulatedExecutionHandler for short fill logic.")]
public static class LongOnlyGuard
{
    /// <summary>
    /// Throws <see cref="NotSupportedException"/> when <paramref name="direction"/>
    /// is <see cref="Direction.Short"/>.
    /// </summary>
    public static void EnsureLongOnly(Direction direction)
    {
        if (direction == Direction.Short)
            throw new NotSupportedException("Short selling is not yet supported.");
    }
}
