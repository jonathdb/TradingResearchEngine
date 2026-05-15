namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Determines whether the sealed test set has already been consumed for a given strategy version.
/// Used by <see cref="FinalValidationUseCase"/> to prevent repeated final validation runs.
/// </summary>
public interface ITestSetGuard
{
    /// <summary>
    /// Returns <c>true</c> if the test set has already been consumed (final validation already run)
    /// for the specified strategy version.
    /// </summary>
    /// <param name="strategyVersionId">The strategy version identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> when the test set is consumed; <c>false</c> otherwise.</returns>
    Task<bool> IsConsumedAsync(string strategyVersionId, CancellationToken ct = default);

    /// <summary>
    /// Marks the test set as consumed for the specified strategy version.
    /// Called after a successful final validation run.
    /// </summary>
    /// <param name="strategyVersionId">The strategy version identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task MarkConsumedAsync(string strategyVersionId, CancellationToken ct = default);
}
