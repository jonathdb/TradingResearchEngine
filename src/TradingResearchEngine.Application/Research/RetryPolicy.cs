namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Configurable retry policy for background job execution.
/// Defines maximum retry attempts, initial backoff duration, and exponential backoff multiplier.
/// </summary>
public sealed class RetryPolicy
{
    /// <summary>Maximum number of retry attempts before transitioning to final failure. Default 3.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Initial backoff duration before the first retry. Default 2 seconds.</summary>
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Multiplier applied to the backoff duration after each retry. Default 2.0 (exponential).</summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Determines whether the given exception represents a transient failure
    /// that is eligible for retry.
    /// </summary>
    /// <param name="ex">The exception to classify.</param>
    /// <returns><c>true</c> if the failure is transient and retryable; <c>false</c> if terminal.</returns>
    public bool IsTransient(Exception ex) => ex is
        HttpRequestException or TimeoutException or IOException;

    /// <summary>
    /// Computes the backoff delay for the given retry attempt (zero-based).
    /// </summary>
    /// <param name="attempt">The zero-based retry attempt number.</param>
    /// <returns>The duration to wait before the next retry.</returns>
    public TimeSpan GetBackoffDelay(int attempt)
    {
        var multiplier = Math.Pow(BackoffMultiplier, attempt);
        return TimeSpan.FromMilliseconds(InitialBackoff.TotalMilliseconds * multiplier);
    }
}
