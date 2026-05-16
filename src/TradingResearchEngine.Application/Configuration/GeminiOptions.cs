namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Configuration for the Google Gemini AI strategy assistant.
/// Bound from application configuration via the <c>IOptions&lt;GeminiOptions&gt;</c> pattern.
/// </summary>
public sealed record GeminiOptions
{
    /// <summary>
    /// Gemini API key. Never logged, serialised to responses, or exposed in any API output.
    /// When null or empty, AI assistant features are disabled gracefully.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Model name to use for generation and refinement requests.
    /// Default: <c>gemini-2.5-flash</c>.
    /// </summary>
    public string ModelName { get; init; } = "gemini-2.5-flash";

    /// <summary>
    /// Maximum retry attempts for transient failures or invalid responses.
    /// Provides sufficient retry budget to outlast typical Gemini rate limit windows.
    /// Default: 5 (6 total attempts including the initial request).
    /// </summary>
    public int MaxRetries { get; init; } = 5;

    /// <summary>
    /// Path to the system prompt file loaded at call time.
    /// Default: <c>Prompts/strategy-assistant-system.md</c>.
    /// </summary>
    public string SystemPromptFilePath { get; init; } = "Prompts/strategy-assistant-system.md";

    /// <summary>
    /// Base delay in seconds for the adaptive exponential backoff strategy.
    /// Used as the starting delay for retry calculations when the server does not provide
    /// a <c>Retry-After</c> header, or when the computed backoff exceeds the header value.
    /// Default: 5.0 seconds.
    /// </summary>
    public double BaseRetryDelaySeconds { get; init; } = 5.0;

    /// <summary>
    /// Number of consecutive HTTP 429 responses required to open the circuit breaker.
    /// Once the threshold is reached, subsequent requests fail immediately with a
    /// <c>RateLimitExceededException</c> until the circuit resets.
    /// Default: 3.
    /// </summary>
    public int CircuitBreakerThreshold { get; init; } = 3;

    /// <summary>
    /// Duration in seconds that the circuit breaker remains open after being tripped.
    /// After this period elapses, the circuit transitions to half-open and allows a
    /// single probe request to determine if the rate limit has lifted.
    /// Default: 60.0 seconds.
    /// </summary>
    public double CircuitBreakerDurationSeconds { get; init; } = 60.0;
}
