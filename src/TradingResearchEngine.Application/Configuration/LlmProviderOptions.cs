namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Configuration options for LLM-based strategy idea translation.
/// Bound from the <c>LlmProvider</c> configuration section.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="EnableAIStrategyAssist"/> is <c>false</c>, no HTTP calls are made
/// to any LLM provider and the translator returns a disabled result immediately.
/// </para>
/// <para>
/// API keys should be sourced from environment variables or a secrets manager —
/// never hardcoded in source or committed configuration files.
/// </para>
/// </remarks>
public sealed class LlmProviderOptions
{
    /// <summary>Master toggle for AI-assisted strategy creation. Default <c>false</c>.</summary>
    public bool EnableAIStrategyAssist { get; set; }

    /// <summary>Primary LLM provider name (e.g. "GoogleAIStudio", "Groq", "Ollama").</summary>
    public string Provider { get; set; } = "GoogleAIStudio";

    /// <summary>Base URL for the primary provider's API endpoint.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Model identifier for the primary provider.</summary>
    public string Model { get; set; } = "gemini-2.5-flash";

    /// <summary>API key for the primary provider. Sourced from environment or secrets.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Ordered list of fallback provider names attempted when the primary provider fails.
    /// Default chain: Groq → Ollama.
    /// </summary>
    public string[] FallbackProviders { get; set; } = ["Groq", "Ollama"];

    /// <summary>Base URL for the Ollama local inference server.</summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model identifier for Ollama.</summary>
    public string OllamaModel { get; set; } = "llama3";

    /// <summary>Base URL for the Groq API.</summary>
    public string GroqBaseUrl { get; set; } = "https://api.groq.com/openai/v1/";

    /// <summary>API key for Groq. Sourced from environment or secrets.</summary>
    public string GroqApiKey { get; set; } = "";

    /// <summary>Model identifier for Groq.</summary>
    public string GroqModel { get; set; } = "llama-3.3-70b-versatile";
}
