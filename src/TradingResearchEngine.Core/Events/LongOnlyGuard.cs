namespace TradingResearchEngine.Core.Events;

/// <summary>
/// Runtime safety net for long-only V5 scope. Called explicitly at all known
/// <see cref="Direction"/> consumption points. Complements (but does not replace)
/// exhaustive switch expression handling — if/else chains and default cases require
/// this guard. Removal is a V6 task when short-selling is implemented.
/// </summary>
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
