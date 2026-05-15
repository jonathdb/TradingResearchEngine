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
    /// Default: <c>gemini-2.0-flash</c>.
    /// </summary>
    public string ModelName { get; init; } = "gemini-2.0-flash";

    /// <summary>
    /// Maximum retry attempts for transient failures or invalid responses.
    /// Default: 2.
    /// </summary>
    public int MaxRetries { get; init; } = 2;

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
}
