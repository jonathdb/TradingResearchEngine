# Gemini Rate Limit Fix — Bugfix Design

## Overview

The GeminiClient's retry logic fails to handle HTTP 429 (Too Many Requests) responses effectively during normal single-user usage. The current implementation uses a fixed exponential backoff starting at 2 seconds with only 3 total attempts, ignores the server's `Retry-After` header, and sends requests without any serialization or circuit-breaking. This causes permanent failures when the Gemini free-tier rate limit (15 RPM) is hit, even though the server indicates exactly how long to wait.

The fix introduces adaptive backoff that respects `Retry-After`, increases the retry budget to at least 5 attempts, serializes requests through a concurrency limiter, and adds a circuit breaker for sustained rate limiting — all while preserving existing behavior for successful responses, 5xx retries, streaming, API key validation, and strategy-type correction retries.

## Glossary

- **Bug_Condition (C)**: The condition that triggers the bug — when the Gemini API returns HTTP 429 and the current retry logic exhausts attempts before the rate limit window expires
- **Property (P)**: The desired behavior — the system waits at least the server-specified `Retry-After` duration and retries with sufficient budget to outlast the rate limit window
- **Preservation**: Existing behavior for successful responses, 5xx retries, streaming, API key validation, and strategy-type correction retries that must remain unchanged
- **GeminiClient**: The class in `Infrastructure/AI/GeminiClient.cs` that wraps the Mscc.GenerativeAI library and implements retry logic
- **GeminiOptions**: Configuration record in `Application/Configuration/GeminiOptions.cs` with `MaxRetries`, `ApiKey`, `ModelName`, and `SystemPromptFilePath`
- **Retry-After**: HTTP response header indicating how many seconds the client should wait before retrying a rate-limited request
- **Circuit Breaker**: A pattern that stops sending requests after repeated failures, allowing the system to recover before resuming

## Bug Details

### Bug Condition

The bug manifests when the Gemini API returns HTTP 429 with a `Retry-After` header value exceeding the total retry window of the current implementation (2s + 4s + 8s = 14s across 3 attempts). The `GeminiClient.GenerateJsonAsync` method ignores the `Retry-After` header, uses fixed exponential backoff with a 2-second base delay, and exhausts all 3 attempts before the rate limit window expires.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type GeminiApiResponse
  OUTPUT: boolean
  
  RETURN input.statusCode == 429
         AND input.retryAfterSeconds > 0
         AND (totalRetryWindow(BaseDelay=2s, MaxAttempts=3) < input.retryAfterSeconds
              OR concurrentRequestsExceedLimit(input.context)
              OR sustainedRateLimitingActive(input.context))
END FUNCTION
```

### Examples

- **Example 1**: API returns 429 with `Retry-After: 60`. Current behavior: retries at 2s, 4s, 8s then fails permanently after 14s total. Expected: waits 60s then retries successfully.
- **Example 2**: GeminiStrategyAssistant calls `GenerateJsonAsync` twice in quick succession (generate + correction retry). Current behavior: both requests fire immediately, compounding rate limit. Expected: second request waits for first to complete via concurrency limiter.
- **Example 3**: User triggers 5 strategy generations in 1 minute on free tier (15 RPM limit). Current behavior: later requests fail permanently. Expected: circuit breaker activates, returns descriptive failure without exhausting quota further.
- **Edge case**: API returns 429 with no `Retry-After` header. Expected: falls back to adaptive exponential backoff with longer base delay.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- Successful API responses on first attempt are returned immediately without artificial delay
- Transient 5xx server errors continue to retry with exponential backoff
- `GeminiOptions.ApiKey` null/empty continues to disable AI features gracefully without crashing
- Strategy-type correction retry in `GeminiStrategyAssistant` continues to work independently of rate-limit retry logic
- `StreamGenerateAsync` continues to stream responses without being blocked by the request serialization mechanism
- `CompositeConfig` validation retry in `GeminiStrategyAssistant` continues to work independently

**Scope:**
All inputs that do NOT involve HTTP 429 responses should be completely unaffected by this fix. This includes:
- Successful first-attempt responses (HTTP 200)
- Transient server errors (HTTP 5xx)
- Invalid API key scenarios
- Cancellation token propagation
- Streaming API calls

## Hypothesized Root Cause

Based on the bug description and code analysis, the issues are:

1. **Ignored Retry-After Header**: The `catch (HttpRequestException ex)` block in `GenerateJsonAsync` never inspects the response headers. The `Retry-After` value from the 429 response is discarded, and a fixed `BaseRetryDelay * Math.Pow(2, attempt - 1)` is used instead. The Mscc.GenerativeAI library throws `HttpRequestException` which does not carry response headers, so the header must be extracted at a different layer or via a delegating handler.

2. **Insufficient Retry Budget**: `GeminiOptions.MaxRetries = 2` yields only 3 total attempts with delays of 2s, 4s, 8s (14s total window). Gemini free-tier rate limits typically require 30-60s waits, so all attempts are exhausted before the window expires.

3. **No Request Serialization**: Multiple calls from `GeminiStrategyAssistant` (initial generate + correction retry) fire concurrently or in rapid succession with no inter-request throttling, compounding rate limit exhaustion.

4. **No Circuit Breaking**: When rate limiting is sustained across multiple user requests, each new request immediately attempts the API with no cooldown, further exhausting the quota and generating cascading failures.

## Correctness Properties

Property 1: Bug Condition - Rate-Limited Requests Eventually Succeed

_For any_ API request that receives an HTTP 429 response with a `Retry-After` header, the fixed `GenerateJsonAsync` function SHALL wait at least the server-specified duration before retrying, and SHALL have sufficient retry budget (at least 5 attempts) to outlast typical Gemini rate limit windows (up to 60 seconds).

**Validates: Requirements 2.1, 2.2**

Property 2: Preservation - Non-Rate-Limited Behavior Unchanged

_For any_ API request that does NOT receive an HTTP 429 response (successful responses, 5xx errors, cancellation, streaming), the fixed code SHALL produce exactly the same behavior as the original code, preserving immediate response delivery for successes, exponential backoff for 5xx errors, graceful API key handling, and unblocked streaming.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `src/TradingResearchEngine.Application/Configuration/GeminiOptions.cs`

**Changes**:
1. **Increase MaxRetries default**: Change from `2` to `5` (6 total attempts)
2. **Add BaseRetryDelaySeconds**: New property with default `5.0` for configurable base delay
3. **Add CircuitBreakerThreshold**: New property with default `3` consecutive 429s before opening
4. **Add CircuitBreakerDurationSeconds**: New property with default `60.0` for circuit breaker open duration

**File**: `src/TradingResearchEngine.Infrastructure/AI/GeminiClient.cs`

**Function**: `GenerateJsonAsync`

**Specific Changes**:
1. **Extract Retry-After Header**: Add a custom `DelegatingHandler` (or wrap the Mscc library call) that captures the `Retry-After` header from 429 responses and attaches it to the exception (via `Exception.Data` or a custom exception type) so the retry logic can read it.

2. **Adaptive Backoff with Retry-After**: Replace the fixed `BaseRetryDelay * Math.Pow(2, attempt - 1)` with logic that uses `max(retryAfterHeader, exponentialBackoff)` as the delay. Use Polly's `RetryStrategyOptions` with a `DelayGenerator` that inspects the exception for the `Retry-After` value.

3. **Increase Retry Budget**: Use `GeminiOptions.MaxRetries` (now defaulting to 5) for the Polly retry pipeline, giving 6 total attempts with longer base delays.

4. **Add Concurrency Limiter**: Introduce a `SemaphoreSlim(1, 1)` (or Polly `ConcurrencyLimiter`) as a class-level field to serialize `GenerateJsonAsync` calls. This prevents concurrent requests from compounding rate limit exhaustion. `StreamGenerateAsync` is excluded from this limiter per requirement 3.5.

5. **Add Circuit Breaker**: Use Polly's `CircuitBreakerStrategyOptions` configured to open after N consecutive 429 responses, with a configurable break duration. When open, immediately throw a descriptive `RateLimitExceededException` without hitting the API.

**New File**: `src/TradingResearchEngine.Infrastructure/AI/RateLimitExceededException.cs`

**Purpose**: Custom exception thrown when the circuit breaker is open, providing a user-friendly message about rate limiting.

**New File**: `src/TradingResearchEngine.Infrastructure/AI/RetryAfterDelegatingHandler.cs`

**Purpose**: A `DelegatingHandler` that intercepts 429 responses, extracts the `Retry-After` header, and throws an `HttpRequestException` with the retry-after value attached via `Exception.Data["RetryAfterSeconds"]`.

**Approach Note**: Since the Mscc.GenerativeAI library manages its own `HttpClient` internally, the delegating handler approach may need to be replaced with a post-exception inspection pattern. If the library does not expose the `Retry-After` header through its exceptions, the fallback is to use a longer adaptive backoff (e.g., 10s base with jitter) that is sufficient for typical Gemini rate limit windows without needing the exact header value.

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write tests that mock `IGeminiClient` to throw `HttpRequestException` with `StatusCode = 429` and verify the retry behavior. Run these tests against the UNFIXED code to observe that retries exhaust too quickly and the `Retry-After` header is ignored.

**Test Cases**:
1. **Short Retry Window Test**: Mock 429 with `Retry-After: 30`. Verify unfixed code fails after ~14s total retry window (will fail on unfixed code)
2. **Concurrent Request Test**: Issue two `GenerateJsonAsync` calls simultaneously. Verify unfixed code sends both immediately without serialization (will fail on unfixed code)
3. **Sustained Rate Limiting Test**: Mock 5 consecutive 429 responses. Verify unfixed code has no circuit-breaking behavior (will fail on unfixed code)
4. **Retry-After Ignored Test**: Mock 429 with `Retry-After: 10`. Measure actual delay used. Verify unfixed code uses 2s base delay instead of 10s (will fail on unfixed code)

**Expected Counterexamples**:
- Requests fail permanently after 3 attempts totaling ~14s when rate limit window is 30-60s
- Possible causes: fixed 2s base delay, only 3 attempts, no header parsing

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := GenerateJsonAsync_fixed(input)
  ASSERT retryDelayUsed >= retryAfterHeader OR retryDelayUsed >= adaptiveBackoff
  ASSERT totalAttempts <= MaxRetries + 1
  ASSERT (result is success after wait) OR (circuitBreaker opened with descriptive error)
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT GenerateJsonAsync_original(input) = GenerateJsonAsync_fixed(input)
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many test cases automatically across the input domain (various success responses, 5xx errors, cancellation scenarios)
- It catches edge cases that manual unit tests might miss
- It provides strong guarantees that behavior is unchanged for all non-429 inputs

**Test Plan**: Observe behavior on UNFIXED code first for successful responses and 5xx retries, then write property-based tests capturing that behavior.

**Test Cases**:
1. **Success Response Preservation**: Verify successful API responses are returned immediately without delay on both unfixed and fixed code
2. **5xx Retry Preservation**: Verify 5xx errors still trigger exponential backoff retries identically
3. **Cancellation Preservation**: Verify `CancellationToken` cancellation propagates identically
4. **Streaming Preservation**: Verify `StreamGenerateAsync` is not affected by concurrency limiter

### Unit Tests

- Test that 429 with `Retry-After` header results in delay >= header value
- Test that 429 without `Retry-After` header uses adaptive exponential backoff
- Test that retry budget allows at least 5 retries (6 total attempts)
- Test that concurrency limiter serializes `GenerateJsonAsync` calls
- Test that `StreamGenerateAsync` bypasses the concurrency limiter
- Test that circuit breaker opens after configured threshold of consecutive 429s
- Test that circuit breaker throws `RateLimitExceededException` when open
- Test that circuit breaker resets after configured duration
- Test that successful responses pass through without delay
- Test that 5xx errors still retry with exponential backoff

### Property-Based Tests

- Generate random sequences of API responses (200, 429, 5xx) and verify the retry pipeline handles each correctly according to its type
- Generate random `Retry-After` header values (1-120 seconds) and verify the delay used is always >= the header value for 429 responses
- Generate random non-429 error scenarios and verify behavior matches the original implementation exactly (preservation property)

### Integration Tests

- Test full `GeminiStrategyAssistant.GenerateStrategyAsync` flow with mocked `IGeminiClient` returning 429 then success
- Test that strategy-type correction retry works correctly when rate limiting occurs between initial call and correction call
- Test that `CompositeConfig` validation retry works correctly under rate limiting
- Test circuit breaker integration: sustained 429s → circuit opens → descriptive error returned to caller
