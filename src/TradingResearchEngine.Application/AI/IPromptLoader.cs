namespace TradingResearchEngine.Application.AI;

/// <summary>
/// Loads named prompt files and supports token interpolation for LLM system prompts.
/// </summary>
/// <remarks>
/// Implementations read prompt files from a configured directory at startup and cache
/// them in memory. Token interpolation replaces <c>{key}</c> placeholders with provided values.
/// </remarks>
public interface IPromptLoader
{
    /// <summary>
    /// Returns the content of a named prompt file.
    /// </summary>
    /// <param name="promptName">
    /// The name of the prompt (file name without extension, case-insensitive).
    /// </param>
    /// <returns>The full text content of the prompt file.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="promptName"/> does not match any loaded prompt file.
    /// </exception>
    string GetPrompt(string promptName);

    /// <summary>
    /// Returns the content of a named prompt file with token interpolation applied.
    /// </summary>
    /// <param name="promptName">
    /// The name of the prompt (file name without extension, case-insensitive).
    /// </param>
    /// <param name="tokens">
    /// A dictionary of token keys and replacement values. Each occurrence of <c>{key}</c>
    /// in the prompt template is replaced with the corresponding value.
    /// </param>
    /// <returns>The prompt content with all matching tokens replaced.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="promptName"/> does not match any loaded prompt file.
    /// </exception>
    string GetPrompt(string promptName, Dictionary<string, string> tokens);
}
