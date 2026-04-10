namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Thrown when a study or run attempts to use a date range that overlaps
/// with the sealed held-out test set on a strategy version.
/// </summary>
public sealed class SealedTestSetViolationException : Exception
{
    /// <inheritdoc cref="SealedTestSetViolationException"/>
    public SealedTestSetViolationException(string message) : base(message) { }
}
