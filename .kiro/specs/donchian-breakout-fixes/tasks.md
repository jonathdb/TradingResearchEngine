# Implementation Plan: Donchian Breakout Fixes

## Overview

Three related fixes for the Donchian Breakout strategy: (1) change template `RecommendedTimeframe` from "Daily" to "Any", (2) add `ValidateWarnings` method to `ConfigDraftValidator`, (3) add unit tests for `DonchianBreakoutStrategy` and `ValidateWarnings`. Exploratory tests run BEFORE the fix to confirm bugs exist; preservation tests verify unchanged behavior; implementation applies the fix and re-validates.

## Tasks

- [x] 1. Write bug condition exploration tests
  - **Property 1: Bug Condition** - Donchian Template Timeframe and Missing ValidateWarnings
  - **CRITICAL**: These tests MUST FAIL on unfixed code — failure confirms the bugs exist
  - **DO NOT attempt to fix the tests or the code when they fail**
  - **NOTE**: These tests encode the expected behavior — they will validate the fix when they pass after implementation
  - **GOAL**: Surface counterexamples that demonstrate the bugs exist
  - **Scoped Approach**: Two concrete failing cases:
    1. Assert `DefaultStrategyTemplates.All.First(t => t.TemplateId == "tpl-donchian-breakout").RecommendedTimeframe == "Any"` — will fail (returns `"Daily"`)
    2. Assert `ConfigDraftValidator.ValidateWarnings(draft, templates)` exists and returns a non-empty list for mismatched timeframes — will fail (method does not exist, compilation error)
  - For test 1: create `ConfigDraftValidatorWarningTests.cs` in `src/TradingResearchEngine.UnitTests/Strategy/`
  - Test method: `DonchianTemplate_RecommendedTimeframe_IsAny` — asserts `RecommendedTimeframe == "Any"` for `tpl-donchian-breakout`
  - For test 2: cannot compile until `ValidateWarnings` is added — document this as a known counterexample
  - Run test 1 on UNFIXED code
  - **EXPECTED OUTCOME**: Test 1 FAILS (`RecommendedTimeframe` is `"Daily"` instead of `"Any"`). Test 2 cannot compile (method missing).
  - Document counterexamples found
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 2.1, 2.2_

- [x] 2. Write preservation tests (BEFORE implementing fix)
  - **Property 2: Preservation** - ValidateStep Unchanged, Other Templates Unchanged, Strategy Signals Correct
  - **IMPORTANT**: Follow observation-first methodology — observe behavior on UNFIXED code, then write tests
  - **Part A — ValidateStep Preservation** (in `ConfigDraftValidatorWarningTests.cs`):
    - Observe: `ValidateStep` returns empty list for valid drafts, returns specific errors for missing fields
    - Test: `ValidateStep_ValidDraftAtStep2_ReturnsNoErrors`
    - Test: `ValidateStep_MissingStrategyType_ReturnsError`
  - **Part B — Other Template Preservation** (in `ConfigDraftValidatorWarningTests.cs`):
    - Observe: `tpl-vol-trend`, `tpl-zscore-mr`, `tpl-stationary-mr`, `tpl-regime-rotation`, `tpl-buy-hold` all have `RecommendedTimeframe = "Daily"`
    - Test: `OtherTemplates_RecommendedTimeframe_RemainDaily`
  - **Part C — DonchianBreakoutStrategy Signal Correctness** (new `DonchianBreakoutStrategyTests.cs` in `src/TradingResearchEngine.UnitTests/Strategy/`):
    - Observe: warmup returns no signals, entry on close > prior upper band, exit on close < prior lower band, no duplicate Long signals, look-ahead exclusion, period=1 edge case
    - Test: `Warmup_ReturnsNoSignals`
    - Test: `EntrySignal_CloseAbovePriorUpperBand_EmitsLong`
    - Test: `ExitSignal_CloseBelowPriorLowerBand_EmitsFlat`
    - Test: `NoDuplicateLongSignals_WhileInPosition`
    - Test: `LookAheadExclusion_UsesLaggedChannelValues`
    - Test: `EdgeCase_PeriodOne`
  - Run all preservation tests on UNFIXED code
  - **EXPECTED OUTCOME**: All tests PASS (confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 2.3, 3.1, 3.2, 3.3_

- [x] 3. Fix for Donchian template timeframe and missing ValidateWarnings

  - [x] 3.1 Change RecommendedTimeframe from "Daily" to "Any" for tpl-donchian-breakout
    - Modify `src/TradingResearchEngine.Application/Strategy/StrategyTemplate.cs`
    - In `DefaultStrategyTemplates.All`, change the `tpl-donchian-breakout` entry's `RecommendedTimeframe` from `"Daily"` to `"Any"`
    - Single-line change
    - _Bug_Condition: isBugCondition_TemplateTimeframe(input) where input.TemplateId == "tpl-donchian-breakout" AND input.RecommendedTimeframe == "Daily"_
    - _Expected_Behavior: RecommendedTimeframe == "Any" for tpl-donchian-breakout_
    - _Preservation: All other templates retain RecommendedTimeframe = "Daily"_
    - _Requirements: 2.1, 3.1_

  - [x] 3.2 Add ValidateWarnings method to ConfigDraftValidator
    - Modify `src/TradingResearchEngine.Application/Strategy/ConfigDraftValidator.cs`
    - Add `public static IReadOnlyList<string> ValidateWarnings(ConfigDraft draft, IReadOnlyList<StrategyTemplate> templates)`
    - Return empty list if `draft.CurrentStep < 2`, `draft.DataConfig` is null, or `draft.TemplateId` is null
    - Find matching template by `TemplateId`; return empty list if not found
    - Return empty list if either `DataConfig.Timeframe` or `template.RecommendedTimeframe` is null or `"Any"` (using `StringComparison.OrdinalIgnoreCase`)
    - Return single warning: `"Data timeframe '{DataConfig.Timeframe}' does not match the template's recommended timeframe '{template.RecommendedTimeframe}'. Results may be unexpected."`
    - Add XML doc comment on the method
    - _Bug_Condition: isBugCondition_MissingWarning — method does not exist_
    - _Expected_Behavior: ValidateWarnings returns non-empty list when timeframes mismatch_
    - _Preservation: ValidateStep signature and behavior unchanged_
    - _Requirements: 2.2, 2.4, 3.2_

  - [x] 3.3 Add ValidateWarnings unit tests to ConfigDraftValidatorWarningTests
    - Add tests to `src/TradingResearchEngine.UnitTests/Strategy/ConfigDraftValidatorWarningTests.cs`
    - Test: `ValidateWarnings_RecommendedTimeframeIsAny_ReturnsNoWarning`
    - Test: `ValidateWarnings_RecommendedTimeframeIsNull_ReturnsNoWarning`
    - Test: `ValidateWarnings_DataTimeframeIsNull_ReturnsNoWarning`
    - Test: `ValidateWarnings_MismatchedTimeframes_ReturnsWarning`
    - Test: `ValidateWarnings_MatchingTimeframes_ReturnsNoWarning`
    - Test: `ValidateWarnings_CurrentStepBelow2_ReturnsNoWarning`
    - Test: `ValidateWarnings_TemplateIdIsNull_ReturnsNoWarning`
    - _Requirements: 2.2, 2.4_

  - [x] 3.4 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Donchian Template Timeframe Is "Any"
    - **IMPORTANT**: Re-run the SAME test from task 1 — do NOT write a new test
    - The test from task 1 encodes the expected behavior
    - When this test passes, it confirms the expected behavior is satisfied
    - Run `DonchianTemplate_RecommendedTimeframe_IsAny` from task 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed)
    - _Requirements: 2.1_

  - [x] 3.5 Verify preservation tests still pass
    - **Property 2: Preservation** - ValidateStep, Other Templates, Strategy Signals
    - **IMPORTANT**: Re-run the SAME tests from task 2 — do NOT write new tests
    - Run all preservation tests from task 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm all tests still pass after fix (no regressions)

- [x] 4. Checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- UnitTests references Core and Application only — never Infrastructure, Cli, or Api
- xUnit for all tests; test naming: `<SubjectUnderTest>Tests` / `<Method>_<Condition>_<ExpectedOutcome>`
- .NET 8, C# 12, nullable reference types enabled
- XML doc comments on all new public types and members
- No LINQ in hot paths (strategy code is not changing)
- `StringComparison.OrdinalIgnoreCase` for all string comparisons in `ValidateWarnings`
- `DonchianBreakoutStrategy` tests are additive preservation coverage — strategy code is NOT changing
- `BarEvent` construction pattern: `new BarEvent("SPY", "1D", open, high, low, close, volume, timestamp)`
- `SignalEvent` uses `decimal? Strength` (third positional parameter is Strength, not Price)
