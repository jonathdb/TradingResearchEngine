namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// A date range constraint used to define sealed held-out test sets.
/// The range is half-open: <see cref="Start"/> is inclusive, <see cref="End"/> is exclusive — [Start, End).
/// </summary>
/// <param name="Start">Inclusive start of the date range.</param>
/// <param name="End">Exclusive end of the date range.</param>
/// <param name="IsSealed">When true, this range is locked and cannot be used for training or optimisation studies.</param>
public readonly record struct DateRangeConstraint(
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsSealed)
{
    /// <summary>
    /// Returns true if the given <paramref name="timestamp"/> falls within [Start, End).
    /// </summary>
    public bool Contains(DateTimeOffset timestamp) => timestamp >= Start && timestamp < End;

    /// <summary>
    /// Returns true if this range overlaps with the range [<paramref name="otherStart"/>, <paramref name="otherEnd"/>).
    /// </summary>
    public bool Overlaps(DateTimeOffset otherStart, DateTimeOffset otherEnd) =>
        Start < otherEnd && otherStart < End;
}
