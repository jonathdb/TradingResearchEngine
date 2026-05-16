namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Exception thrown when the Gemini API rate limit circuit breaker is open,
/// indicating that repeated HTTP 429 responses have been received and the system
/// is temporarily refusing to send further requests to allow the rate limit window to expire.
/// </summary>
public sealed class RateLimitExceededException : InvalidOperationException
{
    /// <summary>
    /// Gets the number of seconds the caller should wait before retrying the request.
    /// </summary>
    public double RetryAfterSeconds { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="RateLimitExceededException"/> with the
    /// specified retry duration.
    /// </summary>
    /// <param name="retryAfterSeconds">
    /// The number of seconds the caller should wait before retrying. This corresponds to
    /// the circuit breaker break duration.
    /// </param>
    public RateLimitExceededException(double retryAfterSeconds)
        : base(BuildMessage(retryAfterSeconds))
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    /// <summary>
    /// Initialises a new instance of <see cref="RateLimitExceededException"/> with the
    /// specified retry duration and an inner exception that caused the circuit breaker to open.
    /// </summary>
    /// <param name="retryAfterSeconds">
    /// The number of seconds the caller should wait before retrying. This corresponds to
    /// the circuit breaker break duration.
    /// </param>
    /// <param name="innerException">The exception that triggered the circuit breaker.</param>
    public RateLimitExceededException(double retryAfterSeconds, Exception? innerException)
        : base(BuildMessage(retryAfterSeconds), innerException)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    private static string BuildMessage(double retryAfterSeconds) =>
        $"Gemini API rate limit exceeded. The circuit breaker is open due to repeated HTTP 429 responses. " +
        $"Please retry after {retryAfterSeconds:F0} seconds.";
}
