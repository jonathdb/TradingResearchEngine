using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using TradingResearchEngine.Application.Configuration;

namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Concrete implementation of <see cref="IGeminiClient"/> using the Mscc.GenerativeAI library.
/// Uses structured JSON output mode for reliable parsing.
/// </summary>
public sealed class GeminiClient : IGeminiClient
{
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiClient> _logger;

    public GeminiClient(IOptions<GeminiOptions> options, ILogger<GeminiClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> GenerateJsonAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var googleAi = new GoogleAI(apiKey: _options.ApiKey);
        var systemInstruction = new Content(systemPrompt);
        var model = googleAi.GenerativeModel(
            model: _options.ModelName,
            systemInstruction: systemInstruction);

        var generationConfig = new GenerationConfig
        {
            ResponseMimeType = "application/json"
        };

        var request = new GenerateContentRequest(userMessage)
        {
            GenerationConfig = generationConfig
        };

        ct.ThrowIfCancellationRequested();

        var response = await model.GenerateContent(request);

        ct.ThrowIfCancellationRequested();

        var text = response.Text
            ?? throw new InvalidOperationException("Gemini API returned an empty response.");

        _logger.LogDebug("Gemini API response received ({Length} chars).", text.Length);
        return text;
    }
}
