using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mscc.GenerativeAI;
using Mscc.GenerativeAI.Types;
using TradingResearchEngine.Application.Configuration;

namespace TradingResearchEngine.Infrastructure.AI;

/// <summary>
/// Concrete implementation of <see cref="IGeminiClient"/> using the Mscc.GenerativeAI library.
/// Uses structured JSON output mode for reliable parsing.
/// Configures the library's built-in retry mechanism with appropriate settings for
/// rate limiting (HTTP 429) — the Mscc.GenerativeAI library already handles 429 responses
/// internally by reading RetryInfo from the response body and adjusting delays.
/// A SemaphoreSlim serializes GenerateJsonAsync calls to prevent concurrent requests
/// from compounding rate limit exhaustion.
/// </summary>
public sealed class GeminiClient : IGeminiClient
{
    private readonly GeminiOptions _options;
    private readonly ILogger<GeminiClient> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter = new(1, 1);
    private readonly RequestOptions _requestOptions;
    private int _consecutiveRateLimitFailures;

    public GeminiClient(IOptions<GeminiOptions> options, ILogger<GeminiClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _requestOptions = BuildRequestOptions();
    }

    /// <inheritdoc/>
    public async Task<string> GenerateJsonAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        Console.WriteLine("[GeminiClient] GenerateJsonAsync called.");
        _logger.LogInformation("GeminiClient.GenerateJsonAsync called. Waiting for concurrency limiter...");
        await _concurrencyLimiter.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();

            // Circuit breaker: if we've had too many consecutive rate limit failures, fail fast
            if (_consecutiveRateLimitFailures >= _options.CircuitBreakerThreshold)
            {
                _logger.LogWarning(
                    "Gemini API circuit breaker is open after {Threshold} consecutive rate limit failures. " +
                    "Failing fast. Retry after {Duration:F0}s.",
                    _options.CircuitBreakerThreshold,
                    _options.CircuitBreakerDurationSeconds);
                throw new RateLimitExceededException(_options.CircuitBreakerDurationSeconds);
            }

            _logger.LogInformation(
                "Creating GoogleAI client. Model: {Model}, ApiKey present: {HasKey}, " +
                "Retry config: Initial={Initial}s, Max={Max} attempts, Timeout={Timeout}s",
                _options.ModelName,
                !string.IsNullOrEmpty(_options.ApiKey),
                _requestOptions.Retry.Initial,
                _requestOptions.Retry.Maximum,
                _requestOptions.Retry.Timeout?.TotalSeconds ?? 0);

            var googleAi = new GoogleAI(apiKey: _options.ApiKey);
            var systemInstruction = new Content(systemPrompt);
            var model = googleAi.GenerativeModel(
                model: _options.ModelName,
                systemInstruction: systemInstruction);

            // RequestOptions are passed directly to GenerateContent below

            var generationConfig = new GenerationConfig
            {
                ResponseMimeType = "application/json"
            };

            var request = new GenerateContentRequest(userMessage)
            {
                GenerationConfig = generationConfig
            };

            ct.ThrowIfCancellationRequested();

            _logger.LogInformation("Calling model.GenerateContent with RequestOptions...");

            try
            {
                var response = await model.GenerateContent(request, _requestOptions);

                ct.ThrowIfCancellationRequested();

                var text = response.Text
                    ?? throw new InvalidOperationException("Gemini API returned an empty response.");

                // Success — reset circuit breaker counter
                Interlocked.Exchange(ref _consecutiveRateLimitFailures, 0);

                _logger.LogInformation("Gemini API response received successfully ({Length} chars).", text.Length);
                return text;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsRateLimitException(ex))
            {
                // The library exhausted its own retries on 429 — increment circuit breaker counter
                var failures = Interlocked.Increment(ref _consecutiveRateLimitFailures);
                _logger.LogError(ex,
                    "Gemini API RATE LIMIT failure after library retries. " +
                    "Consecutive failures: {Failures}/{Threshold}. ExceptionType: {ExType}, Message: {Message}",
                    failures,
                    _options.CircuitBreakerThreshold,
                    ex.GetType().FullName,
                    ex.Message);
                throw new RateLimitExceededException(_options.CircuitBreakerDurationSeconds, ex);
            }
            catch (Exception ex)
            {
                // Non-rate-limit failure — log full details for debugging
                _logger.LogError(ex,
                    "Gemini API call FAILED. ExceptionType: {ExType}, Message: {Message}, " +
                    "InnerException: {InnerType} - {InnerMessage}",
                    ex.GetType().FullName,
                    ex.Message,
                    ex.InnerException?.GetType().FullName ?? "none",
                    ex.InnerException?.Message ?? "none");
                throw;
            }
        }
        finally
        {
            _concurrencyLimiter.Release();
            _logger.LogDebug("Concurrency limiter released.");
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamGenerateAsync(
        string systemPrompt, string userMessage,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        Console.WriteLine($"[GeminiClient] StreamGenerateAsync called. Model={_options.ModelName}, ApiKey present={!string.IsNullOrEmpty(_options.ApiKey)}");
        _logger.LogInformation("StreamGenerateAsync called. Model: {Model}", _options.ModelName);

        ct.ThrowIfCancellationRequested();

        var googleAi = new GoogleAI(apiKey: _options.ApiKey);
        var systemInstruction = new Content(systemPrompt);
        var model = googleAi.GenerativeModel(
            model: _options.ModelName,
            systemInstruction: systemInstruction);

        var request = new GenerateContentRequest(userMessage);

        ct.ThrowIfCancellationRequested();

        Console.WriteLine("[GeminiClient] Calling model.GenerateContentStream...");

        // Use streaming API — no concurrency limiter applied per requirement 3.5
        IAsyncEnumerable<GenerateContentResponse> response;
        try
        {
            response = model.GenerateContentStream(request, _requestOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GeminiClient] StreamGenerateAsync FAILED immediately: {ex.GetType().Name}: {ex.Message}");
            _logger.LogError(ex, "StreamGenerateAsync failed: {ExType}: {Message}", ex.GetType().FullName, ex.Message);
            throw;
        }

        var chunkCount = 0;
        await foreach (var chunk in response.WithCancellation(ct))
        {
            var text = chunk?.Text;
            if (!string.IsNullOrEmpty(text))
            {
                chunkCount++;
                Console.WriteLine($"[GeminiClient] Got chunk #{chunkCount}: {text.Length} chars");
                yield return text;
            }
        }

        Console.WriteLine($"[GeminiClient] StreamGenerateAsync completed successfully. Total chunks: {chunkCount}");
    }

    /// <summary>
    /// Builds the <see cref="RequestOptions"/> that configure the Mscc.GenerativeAI library's
    /// built-in retry mechanism. The library already handles HTTP 429 responses by reading
    /// RetryInfo from the response body and adjusting delays accordingly.
    /// </summary>
    private RequestOptions BuildRequestOptions()
    {
        var initialDelay = Math.Max(1, (int)_options.BaseRetryDelaySeconds);

        // The library's Retry class handles:
        // - Initial delay before first retry (in seconds)
        // - Multiplier for exponential backoff between retries
        // - Maximum number of retry attempts
        // - Overall timeout for the retry logic
        // - HTTP status codes that trigger retries (includes 429 by default)
        // When a 429 is received, the library reads RetryInfo from the response body
        // and uses that as the delay instead of the computed backoff.
        var retry = new Retry
        {
            Initial = initialDelay,
            Multiplies = 2,
            Maximum = _options.MaxRetries + 1, // Library uses this as max loop iterations
            Timeout = TimeSpan.FromSeconds(
                _options.BaseRetryDelaySeconds * Math.Pow(2, _options.MaxRetries) + 30) // Generous timeout
        };

        return new RequestOptions(retry: retry);
    }

    /// <summary>
    /// Determines whether an exception indicates a rate limit failure (HTTP 429).
    /// The Mscc.GenerativeAI library throws its own exception types when retries are exhausted.
    /// </summary>
    private static bool IsRateLimitException(Exception ex)
    {
        // The library throws exceptions with messages containing rate limit indicators
        var message = ex.Message ?? string.Empty;
        return message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase)
            || message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase);
    }
}
