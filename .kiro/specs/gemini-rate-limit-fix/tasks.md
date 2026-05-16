# Implementation Plan

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Rate-Limited Requests Exhaust Retries Prematurely
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate the bug exists
  - **Scoped PBT Approach**: Scope the property to concrete failing cases: HTTP 429 responses with Retry-After values exceeding the current 14s total retry window (2s + 4s + 8s across 3 attempts)
  - Create test class `GeminiClientRateLimitProperties` in `src/TradingResearchEngine.IntegrationTests/` (needs Infrastructure reference for `GeminiClient`)
  - Mock the underlying HTTP layer to return 429 with `Retry-After: 30` header, then succeed on later attempts
  - Property: For all Retry-After values in range [15..60], the client should eventually succeed (wait >= Retry-After seconds and have sufficient retry budget)
  - Bug Condition from design: `isBugCondition(input)` where `input.statusCode == 429 AND input.retryAfterSeconds > 0 AND totalRetryWindow(BaseDelay=2s, MaxAttempts=3) < input.retryAfterSeconds`
  - Expected Behavior: `retryDelayUsed >= retryAfterHeader` AND `totalAttempts <= MaxRetries + 1` (at least 5 retries)
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (this is correct - it proves the bug exists: the client exhausts 3 attempts in ~14s and throws before the rate limit window expires)
  - Document counterexamples found (e.g., "With Retry-After: 30, client fails after 3 attempts totaling ~14s instead of waiting 30s and retrying")
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 2.1, 2.2_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Non-Rate-Limited Behavior Unchanged
  - **IMPORTANT**: Follow observation-first methodology
  - Create test class `GeminiClientPreservationProperties` in `src/TradingResearchEngine.IntegrationTests/`
  - Observe on UNFIXED code: successful first-attempt responses are returned immediately without delay
  - Observe on UNFIXED code: 5xx errors trigger exponential backoff retries (2s, 4s, 8s delays)
  - Observe on UNFIXED code: CancellationToken cancellation propagates as OperationCanceledException
  - Observe on UNFIXED code: StreamGenerateAsync streams chunks without blocking
  - Write property-based tests using FsCheck.Xunit:
    - Property 2a: For all successful API responses (HTTP 200), the response is returned immediately with no artificial delay and content matches the mock response exactly
    - Property 2b: For all 5xx server errors that eventually succeed, retry delays follow exponential backoff pattern (BaseDelay * 2^(attempt-1))
    - Property 2c: For all cancellation scenarios, OperationCanceledException is thrown without completing the request
    - Property 2d: StreamGenerateAsync yields all chunks in order without being blocked by any concurrency mechanism
  - Verify all tests PASS on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 3. Implement rate limit handling fix

  - [x] 3.1 Add new configuration properties to GeminiOptions
    - Add `BaseRetryDelaySeconds` property with default `5.0`
    - Add `CircuitBreakerThreshold` property with default `3` (consecutive 429s before opening)
    - Add `CircuitBreakerDurationSeconds` property with default `60.0`
    - Change `MaxRetries` default from `2` to `5` (6 total attempts)
    - All new properties are `IOptions<T>`-bound configuration fields (no magic numbers)
    - _Bug_Condition: isBugCondition(input) where totalRetryWindow(BaseDelay=2s, MaxAttempts=3) < input.retryAfterSeconds_
    - _Expected_Behavior: sufficient retry budget (at least 5 attempts) with configurable base delay_
    - _Preservation: existing ApiKey, ModelName, SystemPromptFilePath, MaxRetries semantics unchanged_
    - _Requirements: 2.1, 2.2, 2.4_

  - [x] 3.2 Create RateLimitExceededException custom exception
    - Create `src/TradingResearchEngine.Infrastructure/AI/RateLimitExceededException.cs`
    - Inherit from `InvalidOperationException`
    - Include descriptive message about rate limiting and circuit breaker state
    - Include `RetryAfterSeconds` property for caller inspection
    - _Requirements: 2.4_

  - [x] 3.3 Implement Polly-based retry pipeline with adaptive backoff in GeminiClient
    - Replace the manual for-loop retry in `GenerateJsonAsync` with a Polly `ResiliencePipeline`
    - Configure `RetryStrategyOptions` with `MaxRetryAttempts` from `GeminiOptions.MaxRetries` (default 5)
    - Implement `DelayGenerator` that inspects `HttpRequestException.Data["RetryAfterSeconds"]` and uses `max(retryAfterValue, exponentialBackoff)` as the delay
    - Fallback: if Retry-After header is not available (Mscc library limitation), use adaptive exponential backoff with `BaseRetryDelaySeconds` (default 5s) as base
    - Add jitter to backoff delays to avoid thundering herd
    - Handle filter: retry on `HttpRequestException` where `StatusCode == 429` OR `StatusCode >= 500`
    - Build the pipeline once in the constructor (or lazily) and reuse across calls
    - _Bug_Condition: isBugCondition(input) where input.statusCode == 429 AND retryAfterSeconds > 0_
    - _Expected_Behavior: retryDelayUsed >= max(retryAfterHeader, adaptiveBackoff) for 429 responses_
    - _Preservation: 5xx errors still retry with exponential backoff; successful responses pass through immediately_
    - _Requirements: 1.1, 1.2, 2.1, 2.2, 3.1, 3.2_

  - [x] 3.4 Add SemaphoreSlim concurrency limiter for GenerateJsonAsync
    - Add `private readonly SemaphoreSlim _concurrencyLimiter = new(1, 1)` as class-level field
    - Wrap `GenerateJsonAsync` body with `await _concurrencyLimiter.WaitAsync(ct)` / `finally { _concurrencyLimiter.Release() }`
    - Do NOT apply the semaphore to `StreamGenerateAsync` (per requirement 3.5)
    - This serializes sequential calls from GeminiStrategyAssistant (generate + correction retry)
    - _Bug_Condition: concurrentRequestsExceedLimit(input.context) — multiple calls fire without throttling_
    - _Expected_Behavior: requests are serialized, preventing concurrent 429 compounding_
    - _Preservation: StreamGenerateAsync is not blocked by the concurrency limiter_
    - _Requirements: 1.3, 2.3, 3.5_

  - [x] 3.5 Add Polly circuit breaker for sustained rate limiting
    - Add `CircuitBreakerStrategyOptions` to the Polly pipeline
    - Configure to open after `GeminiOptions.CircuitBreakerThreshold` (default 3) consecutive 429 responses
    - Break duration: `GeminiOptions.CircuitBreakerDurationSeconds` (default 60s)
    - When circuit is open, throw `RateLimitExceededException` immediately without hitting the API
    - Circuit resets to half-open after break duration, allowing a probe request
    - _Bug_Condition: sustainedRateLimitingActive(input.context) — repeated 429s with no cooldown_
    - _Expected_Behavior: circuit opens after threshold, returns descriptive error, resets after duration_
    - _Preservation: circuit breaker only triggers on 429 responses, not on 5xx or other errors_
    - _Requirements: 1.4, 2.4_

  - [x] 3.6 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Rate-Limited Requests Eventually Succeed
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior (wait >= Retry-After, sufficient retry budget)
    - When this test passes, it confirms the expected behavior is satisfied
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed — client now waits appropriately and has enough retries)
    - _Requirements: 2.1, 2.2_

  - [x] 3.7 Verify preservation tests still pass
    - **Property 2: Preservation** - Non-Rate-Limited Behavior Unchanged
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm successful responses still return immediately, 5xx retries still use exponential backoff, cancellation still propagates, streaming still works unblocked

- [x] 4. Checkpoint - Ensure all tests pass
  - Run full test suite to confirm no regressions across the solution
  - Verify bug condition exploration test passes (Property 1)
  - Verify preservation property tests pass (Property 2)
  - Verify existing unit tests and integration tests still pass
  - Ensure all tests pass, ask the user if questions arise
