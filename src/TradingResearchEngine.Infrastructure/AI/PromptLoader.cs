using TradingResearchEngine.Application.AI;

namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Loads prompt files from a configured directory at startup and caches them in memory.
/// Supports token interpolation by replacing <c>{key}</c> placeholders with provided values.
/// </summary>
public sealed class PromptLoader : IPromptLoader
{
    private readonly IReadOnlyDictionary<string, string> _prompts;

    /// <summary>
    /// Initialises the prompt loader by reading all <c>.md</c> files from the specified directory.
    /// </summary>
    /// <param name="promptsDirectory">
    /// The path to the directory containing prompt template files.
    /// If the directory does not exist, no prompts are loaded.
    /// </param>
    public PromptLoader(string promptsDirectory)
    {
        var prompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(promptsDirectory))
        {
            foreach (var file in Directory.GetFiles(promptsDirectory, "*.md"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                prompts[name] = File.ReadAllText(file);
            }
        }
        _prompts = prompts;
    }

    /// <inheritdoc />
    public string GetPrompt(string promptName)
    {
        if (_prompts.TryGetValue(promptName, out var content))
            return content;

        throw new InvalidOperationException(
            $"Prompt '{promptName}' not found. Available prompts: {string.Join(", ", _prompts.Keys)}");
    }

    /// <inheritdoc />
    public string GetPrompt(string promptName, Dictionary<string, string> tokens)
    {
        var template = GetPrompt(promptName);
        foreach (var (key, value) in tokens)
        {
            template = template.Replace($"{{{key}}}", value);
        }
        return template;
    }
}
