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
            // Create a per-attempt timeout token linked with the caller's cancellation token.
            // Each retry attempt gets its own fresh timeout window.
            using var timeoutCts = new CancellationTokenSource(_options.CallTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                // The Mscc.GenerativeAI library does not accept CancellationToken directly.
                // Wrap the call to observe the linked token for timeout/cancellation.
                var responseTask = model.GenerateContent(request);
                var completedTask = await Task.WhenAny(
                    responseTask,
                    Task.Delay(Timeout.Infinite, linkedCts.Token));

                // If the delay task completed (was cancelled), the token fired
                if (completedTask != responseTask)
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                }

                var response = await responseTask;

                var text = response.Text
                    ?? throw new InvalidOperationException("Gemini API returned an empty response.");

                _logger.LogDebug("Gemini API response received ({Length} chars).", text.Length);
                return text;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Gemini API call timed out after {TimeoutSeconds}s (attempt {Attempt}/{MaxAttempts}).",
                    _options.CallTimeout.TotalSeconds, attempt, maxAttempts);

                throw new TimeoutException(
                    $"AI call exceeded configured timeout of {_options.CallTimeout.TotalSeconds}s.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Caller requested cancellation — propagate without wrapping
                throw;
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

        // Create a per-call timeout token linked with the caller's cancellation token
        using var timeoutCts = new CancellationTokenSource(_options.CallTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var linkedToken = linkedCts.Token;

        var googleAi = new GoogleAI(apiKey: _options.ApiKey);
        var systemInstruction = new Content(systemPrompt);
        var model = googleAi.GenerativeModel(
            model: _options.ModelName,
            systemInstruction: systemInstruction);

        var request = new GenerateContentRequest(userMessage);

        linkedToken.ThrowIfCancellationRequested();

        try
        {
            // Use streaming API
            var response = model.GenerateContentStream(request);
            await foreach (var chunk in response.WithCancellation(linkedToken))
            {
                var text = chunk?.Text;
                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
        }
        finally
        {
            // Check if timeout fired vs caller cancellation for logging purposes
            if (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Gemini streaming API call timed out after {TimeoutSeconds}s.",
                    _options.CallTimeout.TotalSeconds);
            }
        }
    }
}
