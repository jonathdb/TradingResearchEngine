# Bugfix Requirements Document

## Introduction

The GeminiClient's retry logic fails to handle Gemini API rate limiting (HTTP 429) effectively during normal single-user usage. The current implementation uses a fixed exponential backoff with too-short delays and too few attempts, ignores the server's `Retry-After` header, and lacks request serialization. This causes AI Strategy Assistant calls to fail when the Gemini free-tier rate limit (typically 15 RPM) is hit, even though the server is indicating exactly how long to wait before retrying.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN the Gemini API returns HTTP 429 with a `Retry-After` header THEN the system ignores the header and uses a fixed 2s/4s/8s exponential backoff that is shorter than the server's recommended wait time

1.2 WHEN the Gemini API returns HTTP 429 and the rate limit window exceeds 14 seconds THEN the system exhausts all 3 attempts (initial + 2 retries) before the rate limit window expires and the request fails permanently

1.3 WHEN the GeminiStrategyAssistant issues multiple sequential API calls (e.g., generate + strategy-type correction retry) THEN the system sends them without any inter-request throttling, compounding the rate limit exhaustion

1.4 WHEN the Gemini API returns HTTP 429 repeatedly across multiple user requests THEN the system continues sending requests immediately on each new user action with no circuit-breaking or cooldown, further exhausting the quota

### Expected Behavior (Correct)

2.1 WHEN the Gemini API returns HTTP 429 with a `Retry-After` header THEN the system SHALL parse the header and wait at least the server-specified duration before retrying

2.2 WHEN the Gemini API returns HTTP 429 and the rate limit window is longer than the base backoff THEN the system SHALL use an adaptive backoff with sufficient total retry budget (at least 5 attempts with longer base delays) to outlast typical Gemini rate limit windows

2.3 WHEN the GeminiStrategyAssistant issues multiple sequential API calls THEN the system SHALL serialize requests through a concurrency limiter to avoid sending concurrent requests that compound rate limit exhaustion

2.4 WHEN the Gemini API returns HTTP 429 repeatedly THEN the system SHALL activate a circuit breaker that temporarily stops sending requests and returns a descriptive failure, preventing further quota exhaustion during sustained rate limiting

### Unchanged Behavior (Regression Prevention)

3.1 WHEN the Gemini API returns a successful response on the first attempt THEN the system SHALL CONTINUE TO return the response immediately without any artificial delay

3.2 WHEN the Gemini API returns a transient 5xx server error THEN the system SHALL CONTINUE TO retry with exponential backoff as it does today

3.3 WHEN the GeminiOptions.ApiKey is null or empty THEN the system SHALL CONTINUE TO disable AI assistant features gracefully without crashing

3.4 WHEN the GeminiStrategyAssistant receives an unknown StrategyType THEN the system SHALL CONTINUE TO perform the dedicated single correction retry independent of the rate-limit retry logic

3.5 WHEN streaming via StreamGenerateAsync THEN the system SHALL CONTINUE TO stream responses without being blocked by the request serialization mechanism (streaming calls are not rate-limited the same way)
