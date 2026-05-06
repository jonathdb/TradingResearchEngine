namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Abstraction over the Gemini API client for testability.
/// Implementations use structured JSON output mode.
/// </summary>
public interface IGeminiClient
{
    /// <summary>
    /// Generates a JSON response from the Gemini model using structured output mode.
    /// </summary>
    /// <param name="systemPrompt">System prompt to guide model behaviour.</param>
    /// <param name="userMessage">User message/prompt to generate content for.</param>
    /// <param name="ct">Cancellation token propagated to the API call.</param>
    /// <returns>Raw JSON string from the model response.</returns>
    Task<string> GenerateJsonAsync(string systemPrompt, string userMessage, CancellationToken ct);
}
