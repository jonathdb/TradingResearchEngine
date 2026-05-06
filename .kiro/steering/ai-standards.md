# AI Assistant Standards

## Structured JSON Output Mode

The AI Strategy Assistant uses Gemini's structured JSON output mode (response schema) for all requests.
This guarantees machine-parseable responses without free-text extraction or regex-based parsing.

Both generation and refinement calls enforce the same structured output schema, ensuring
`AIStrategyDraft` is always deserializable directly from the model response.

## Retry Semantics

When the returned `StrategyType` is not present in `StrategyRegistry.KnownNames`:

1. Retry exactly once with a correction prompt that includes the full list of `KnownNames`.
2. If the retry also returns an unknown `StrategyType`, return the draft with a Caveat
   indicating the strategy type is unrecognised — do not retry further.

`GeminiOptions.MaxRetries` (default 2) governs the total retry budget for transient failures.
The unknown-strategy-type retry is a single dedicated retry independent of the general retry count.

## System Prompt

- Default path: `Prompts/strategy-assistant-system.md`
- Configurable via `GeminiOptions.SystemPromptFilePath`
- The system prompt file is loaded at call time from the configured path
- If the file is missing, the assistant should throw a descriptive error at invocation time

## API Key Handling

- Read from `GeminiOptions.ApiKey` via `IOptions<GeminiOptions>`
- Never hardcoded in source code
- Never logged, serialised to responses, or exposed in any API output
- If `ApiKey` is null or empty at startup, log a warning and disable AI assistant features
  gracefully without crashing the application

## SourceType Tagging

All machine-generated strategy drafts are tagged with `SourceType.AIGenerated`.
This tag is set automatically by the assistant — callers do not need to specify it.

The `SourceType.AIGenerated` tag enables:
- UI display of the "Refine with AI" button on backtest result pages
- Distinguishing human-authored strategies from AI-generated drafts
- Audit trail for strategy provenance
