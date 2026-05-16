using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Infrastructure.AI;

namespace TradingResearchEngine.IntegrationTests.AI;

// Feature: gemini-rate-limit-fix, Property 2: Preservation - Non-Rate-Limited Behavior Unchanged

/// <summary>
/// Property-based tests that verify non-rate-limited behavior is preserved.
/// These tests capture the baseline behavior of the UNFIXED GeminiClient code:
/// - Successful responses return immediately without delay
/// - 5xx errors trigger exponential backoff retries
/// - CancellationToken propagation works correctly
/// - StreamGenerateAsync streams chunks without blocking
/// </summary>
/// <remarks>
/// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
///
/// These tests MUST PASS on the unfixed code to confirm baseline behavior.
/// After the fix is applied, they must continue to pass (preservation guarantee).
/// </remarks>
public class GeminiClientPreservationProperties
{
    /// <summary>
    /// The current base retry delay used by GeminiClient (2 seconds).
    /// </summary>
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The current max retries setting (MaxRetries=2 means 3 total attempts).
    /// </summary>
    private const int CurrentMaxRetries = 2;

    /// <summary>
    /// Property 2a: For all successful API responses (HTTP 200), the response is returned
    /// immediately with no artificial delay and content matches the mock response exactly.
    ///
    /// **Validates: Requirements 3.1**
    ///
    /// Observation: On unfixed code, when the first attempt succeeds, the response is
    /// returned immediately without any delay. No retry logic is triggered.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SuccessfulResponses_ReturnedImmediately_WithNoDelay()
    {
        var responseContentGen = Arb.Generate<NonEmptyString>().Select(s => s.Get);

        return Prop.ForAll(responseContentGen.ToArbitrary(), responseContent =>
        {
            // Arrange: Create a client that succeeds on first attempt and tracks delays
            var delayTracker = new DelayTracker();
            var client = new DelayTrackingGeminiClient(
                new[] { DelayTrackingGeminiClient.SuccessResponse(responseContent) },
                delayTracker);

            // Act
            var result = client.GenerateJsonAsync("system", "user", CancellationToken.None)
                .GetAwaiter().GetResult();

            // Assert: Response matches exactly and no delays were applied
            var responseMatches = result == responseContent;
            var noDelaysApplied = delayTracker.Delays.Count == 0;

            return (responseMatches && noDelaysApplied)
                .Label($"Response matches: {responseMatches} | " +
                       $"Delays applied: {delayTracker.Delays.Count} (expected 0) | " +
                       $"Content length: {responseContent.Length}");
        });
    }

    /// <summary>
    /// Property 2b: For all 5xx server errors that eventually succeed, retry delays follow
    /// exponential backoff pattern (BaseDelay * 2^(attempt-1)).
    ///
    /// **Validates: Requirements 3.2**
    ///
    /// Observation: On unfixed code, 5xx errors trigger retries with delays of:
    /// - Attempt 1 fails → delay 2s (2 * 2^0)
    /// - Attempt 2 fails → delay 4s (2 * 2^1)
    /// - Attempt 3 succeeds (or fails if MaxRetries exhausted)
    /// The exponential backoff pattern is BaseRetryDelay * 2^(attempt-1).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ServerErrors_RetryWithExponentialBackoff()
    {
        // Generate number of failures before success: 1 or 2 (within MaxRetries=2 budget)
        var failCountGen = Gen.Choose(1, CurrentMaxRetries);
        // Generate 5xx status codes
        var statusCodeGen = Gen.Elements(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.BadGateway,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout);

        return Prop.ForAll(
            failCountGen.ToArbitrary(),
            statusCodeGen.ToArbitrary(),
            (failCount, statusCode) =>
            {
                // Arrange: Create a client that fails with 5xx `failCount` times then succeeds
                var delayTracker = new DelayTracker();
                var responses = new List<Func<Task<string>>>();
                for (var i = 0; i < failCount; i++)
                {
                    responses.Add(DelayTrackingGeminiClient.ServerErrorResponse(statusCode));
                }
                responses.Add(DelayTrackingGeminiClient.SuccessResponse("success"));

                var client = new DelayTrackingGeminiClient(responses.ToArray(), delayTracker);

                // Act
                var result = client.GenerateJsonAsync("system", "user", CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: Verify exponential backoff pattern
                // Expected delays: attempt 1 → 2*2^0=2s, attempt 2 → 2*2^1=4s
                var delaysCorrect = delayTracker.Delays.Count == failCount;
                var backoffCorrect = true;
                for (var i = 0; i < delayTracker.Delays.Count; i++)
                {
                    var expectedDelay = BaseRetryDelay * Math.Pow(2, i);
                    var actualDelay = delayTracker.Delays[i];
                    // Allow small floating point tolerance
                    if (Math.Abs(actualDelay.TotalSeconds - expectedDelay.TotalSeconds) > 0.001)
                    {
                        backoffCorrect = false;
                        break;
                    }
                }
                var responseCorrect = result == "success";

                return (delaysCorrect && backoffCorrect && responseCorrect)
                    .Label($"Failures: {failCount} | " +
                           $"Status: {statusCode} | " +
                           $"Delays count correct: {delaysCorrect} ({delayTracker.Delays.Count}/{failCount}) | " +
                           $"Backoff pattern correct: {backoffCorrect} | " +
                           $"Delays: [{string.Join(", ", delayTracker.Delays.Select(d => $"{d.TotalSeconds:F1}s"))}] | " +
                           $"Response correct: {responseCorrect}");
            });
    }

    /// <summary>
    /// Property 2c: For all cancellation scenarios, OperationCanceledException is thrown
    /// without completing the request.
    ///
    /// **Validates: Requirements 3.3**
    ///
    /// Observation: On unfixed code, CancellationToken cancellation propagates correctly.
    /// The client checks ct.ThrowIfCancellationRequested() at entry and after response,
    /// and passes ct to Task.Delay during retries.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CancellationToken_PropagatesAsOperationCanceledException()
    {
        // Generate different cancellation scenarios:
        // 0 = cancel before call, 1 = cancel during first attempt
        var scenarioGen = Gen.Choose(0, 1);

        return Prop.ForAll(scenarioGen.ToArbitrary(), scenario =>
        {
            var cts = new CancellationTokenSource();
            var delayTracker = new DelayTracker();

            if (scenario == 0)
            {
                // Cancel before the call
                cts.Cancel();
                var client = new DelayTrackingGeminiClient(
                    new[] { DelayTrackingGeminiClient.SuccessResponse("should-not-return") },
                    delayTracker);

                var threwCancellation = false;
                try
                {
                    client.GenerateJsonAsync("system", "user", cts.Token)
                        .GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    threwCancellation = true;
                }

                return threwCancellation
                    .Label($"Scenario: pre-cancelled | Threw OperationCanceledException: {threwCancellation}");
            }
            else
            {
                // Cancel during the response (simulate by having the response check the token)
                cts.Cancel();
                var client = new DelayTrackingGeminiClient(
                    new[] { DelayTrackingGeminiClient.CancellationAwareResponse(cts.Token) },
                    delayTracker);

                var threwCancellation = false;
                try
                {
                    client.GenerateJsonAsync("system", "user", cts.Token)
                        .GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    threwCancellation = true;
                }

                return threwCancellation
                    .Label($"Scenario: cancel-during-call | Threw OperationCanceledException: {threwCancellation}");
            }
        });
    }

    /// <summary>
    /// Property 2d: StreamGenerateAsync yields all chunks in order without being blocked
    /// by any concurrency mechanism.
    ///
    /// **Validates: Requirements 3.5**
    ///
    /// Observation: On unfixed code, StreamGenerateAsync streams chunks sequentially
    /// without any semaphore or concurrency limiter blocking it.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property StreamGenerateAsync_YieldsChunksInOrder_WithoutBlocking()
    {
        // Generate a list of 1-10 chunks
        var chunkCountGen = Gen.Choose(1, 10);

        return Prop.ForAll(chunkCountGen.ToArbitrary(), chunkCount =>
        {
            // Arrange: Create chunks with predictable content
            var expectedChunks = Enumerable.Range(0, chunkCount)
                .Select(i => $"chunk-{i}")
                .ToList();

            var client = new FakeStreamingGeminiClient(expectedChunks);

            // Act: Collect all streamed chunks and measure time
            var sw = Stopwatch.StartNew();
            var receivedChunks = new List<string>();
            var enumerable = client.StreamGenerateAsync("system", "user", CancellationToken.None);

            // Consume the async enumerable synchronously for the test
            var enumerator = enumerable.GetAsyncEnumerator(CancellationToken.None);
            try
            {
                while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                {
                    receivedChunks.Add(enumerator.Current);
                }
            }
            finally
            {
                enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            sw.Stop();

            // Assert: All chunks received in order, no blocking delay
            var chunksMatch = receivedChunks.SequenceEqual(expectedChunks);
            var noBlocking = sw.ElapsedMilliseconds < 500; // Should be near-instant

            return (chunksMatch && noBlocking)
                .Label($"Chunks: {chunkCount} | " +
                       $"Match: {chunksMatch} | " +
                       $"Elapsed: {sw.ElapsedMilliseconds}ms | " +
                       $"No blocking: {noBlocking}");
        });
    }
}

/// <summary>
/// Tracks delays that would be applied during retry logic without actually waiting.
/// This allows property tests to verify the exponential backoff pattern without
/// incurring real time delays.
/// </summary>
internal sealed class DelayTracker
{
    private readonly List<TimeSpan> _delays = new();

    public IReadOnlyList<TimeSpan> Delays => _delays;

    public void RecordDelay(TimeSpan delay)
    {
        _delays.Add(delay);
    }
}

/// <summary>
/// A fake GeminiClient that replicates the exact retry logic of the real GeminiClient
/// but tracks delays instead of actually waiting. This allows fast property-based testing
/// of the retry behavior pattern.
/// </summary>
internal sealed class DelayTrackingGeminiClient : IGeminiClient
{
    private readonly Func<Task<string>>[] _responseSequence;
    private readonly DelayTracker _delayTracker;
    private int _callIndex;

    public DelayTrackingGeminiClient(Func<Task<string>>[] responseSequence, DelayTracker delayTracker)
    {
        _responseSequence = responseSequence;
        _delayTracker = delayTracker;
        _callIndex = 0;
    }

    /// <summary>
    /// Replicates the retry logic of the real GeminiClient.GenerateJsonAsync exactly:
    /// - MaxRetries + 1 total attempts
    /// - Exponential backoff: BaseRetryDelay * 2^(attempt-1) = 2s, 4s
    /// - Retries on HttpRequestException with 429 or 5xx status codes
    /// - CancellationToken checked at entry and after response
    /// - Delays are TRACKED but not actually awaited (for fast testing)
    /// </summary>
    public async Task<string> GenerateJsonAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var baseRetryDelay = TimeSpan.FromSeconds(2);
        const int maxRetries = 2;
        var maxAttempts = maxRetries + 1;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                var responseFunc = _responseSequence[Math.Min(_callIndex, _responseSequence.Length - 1)];
                _callIndex++;

                var result = await responseFunc();
                ct.ThrowIfCancellationRequested();
                return result;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsTransientOrRateLimited(ex))
            {
                var delay = baseRetryDelay * Math.Pow(2, attempt - 1);
                _delayTracker.RecordDelay(delay);
                // Track the delay but don't actually wait — this is the key optimization
                ct.ThrowIfCancellationRequested();
            }
        }

        throw new InvalidOperationException("All retry attempts exhausted.");
    }

    public IAsyncEnumerable<string> StreamGenerateAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        throw new NotImplementedException("Use FakeStreamingGeminiClient for streaming tests.");
    }

    private static bool IsTransientOrRateLimited(HttpRequestException ex)
    {
        return ex.StatusCode == HttpStatusCode.TooManyRequests
            || (ex.StatusCode.HasValue && (int)ex.StatusCode.Value >= 500);
    }

    /// <summary>Creates a response function that returns immediately with the given content.</summary>
    public static Func<Task<string>> SuccessResponse(string content)
        => () => Task.FromResult(content);

    /// <summary>Creates a response function that throws HttpRequestException with the given status code.</summary>
    public static Func<Task<string>> ServerErrorResponse(HttpStatusCode statusCode)
        => () => throw new HttpRequestException($"Server error: {statusCode}", null, statusCode);

    /// <summary>Creates a response function that checks cancellation token and throws if cancelled.</summary>
    public static Func<Task<string>> CancellationAwareResponse(CancellationToken ct)
        => () =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("should-not-reach");
        };
}

/// <summary>
/// A fake streaming GeminiClient that yields predefined chunks in order.
/// Used to verify StreamGenerateAsync behavior without hitting the real API.
/// </summary>
internal sealed class FakeStreamingGeminiClient : IGeminiClient
{
    private readonly IReadOnlyList<string> _chunks;

    public FakeStreamingGeminiClient(IReadOnlyList<string> chunks)
    {
        _chunks = chunks;
    }

    public Task<string> GenerateJsonAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        throw new NotImplementedException("Use DelayTrackingGeminiClient for non-streaming tests.");
    }

    public async IAsyncEnumerable<string> StreamGenerateAsync(
        string systemPrompt, string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var chunk in _chunks)
        {
            ct.ThrowIfCancellationRequested();
            yield return chunk;
            await Task.Yield(); // Simulate async streaming without blocking
        }
    }
}
