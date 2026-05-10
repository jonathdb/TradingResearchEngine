using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using TradingResearchEngine.Application.Configuration;

namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Concrete implementation of <see cref="IGeminiClient"/> using the Mscc.GenerativeAI library.
/// Uses structured JSON output mode for reliable parsing.
/// Includes exponential backoff retry for transient failures and rate limiting (HTTP 429).
/// </summary>
public sealed class GeminiClient : IGeminiClient
{
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiClient> _logger;

    /// <summary>Base delay for exponential backoff on rate-limited or transient failures.</summary>
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(2);

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

        // Retry with exponential backoff for transient/rate-limit failures
        var maxAttempts = _options.MaxRetries + 1;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var response = await model.GenerateContent(request);

                ct.ThrowIfCancellationRequested();

                var text = response.Text
                    ?? throw new InvalidOperationException("Gemini API returned an empty response.");

                _logger.LogDebug("Gemini API response received ({Length} chars).", text.Length);
                return text;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsTransientOrRateLimited(ex))
            {
                var delay = BaseRetryDelay * Math.Pow(2, attempt - 1);
                _logger.LogWarning(
                    "Gemini API request failed (attempt {Attempt}/{MaxAttempts}): {Message}. Retrying in {Delay}s.",
                    attempt, maxAttempts, ex.Message, delay.TotalSeconds);
                await Task.Delay(delay, ct);
            }
        }

        // Should not reach here, but just in case
        throw new InvalidOperationException("Gemini API request failed after all retry attempts.");
    }

    private static bool IsTransientOrRateLimited(HttpRequestException ex)
    {
        // HTTP 429 Too Many Requests or 5xx server errors
        return ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            || (ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamGenerateAsync(
        string systemPrompt, string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var googleAi = new GoogleAI(apiKey: _options.ApiKey);
        var systemInstruction = new Content(systemPrompt);
        var model = googleAi.GenerativeModel(
            model: _options.ModelName,
            systemInstruction: systemInstruction);

        var request = new GenerateContentRequest(userMessage);

        ct.ThrowIfCancellationRequested();

        // Use streaming API
        var response = model.GenerateContentStream(request);
        await foreach (var chunk in response.WithCancellation(ct))
        {
            var text = chunk?.Text;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }
}
