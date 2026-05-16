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
    /// Maximum time allowed for a single outbound AI API call before it is cancelled.
    /// The timeout applies per-call (each retry attempt gets its own timeout window).
    /// Default: 60 seconds.
    /// </summary>
    public TimeSpan CallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum combined character length of the system prompt and user message
    /// allowed before an API call is made. When exceeded, a descriptive error is
    /// returned rather than an opaque API failure.
    /// Default: 30000 characters.
    /// </summary>
    public int MaxPromptLength { get; init; } = 30_000;

    /// <summary>
    /// Number of consecutive rate limit (HTTP 429) failures before the circuit breaker opens
    /// and subsequent calls fail fast without hitting the API.
    /// Default: 3.
    /// </summary>
    public int CircuitBreakerThreshold { get; init; } = 3;

    /// <summary>
    /// Duration in seconds that the circuit breaker remains open after being tripped.
    /// During this period, all calls fail immediately with <see cref="Infrastructure.AI.RateLimitExceededException"/>.
    /// Default: 60 seconds.
    /// </summary>
    public double CircuitBreakerDurationSeconds { get; init; } = 60;

    /// <summary>
    /// Base delay in seconds for the first retry attempt. Subsequent retries use exponential
    /// backoff (base × 2^attempt). Used to configure the Mscc.GenerativeAI library's built-in
    /// retry mechanism.
    /// Default: 2 seconds.
    /// </summary>
    public double BaseRetryDelaySeconds { get; init; } = 2;
}
