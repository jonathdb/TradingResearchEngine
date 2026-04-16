# Donchian Breakout Fixes — Bugfix Design

## Overview

Three related issues affect the Donchian Breakout strategy's usability and correctness confidence:

1. The `tpl-donchian-breakout` template hardcodes `RecommendedTimeframe = "Daily"` despite the strategy being timeframe-agnostic, misleading users and causing silent interval mismatches.
2. `ConfigDraftValidator` has no mechanism to warn when a draft's `DataConfig.Timeframe` conflicts with the template's `RecommendedTimeframe`, so users proceed to backtests that yield zero trades with no explanation.
3. No unit tests exist for `DonchianBreakoutStrategy`, leaving warmup, signal correctness, duplicate prevention, and look-ahead exclusion unverified.

The fix changes one string literal, adds one new static method, and adds two test classes — all within Application and UnitTests. No Core, Infrastructure, or host changes required.

## Glossary

- **Bug_Condition (C)**: The set of inputs where the current code produces incorrect or misleading behavior — specifically, template selection yielding a wrong timeframe default, and validator silence on mismatched intervals.
- **Property (P)**: The desired behavior — `tpl-donchian-breakout` uses `"Any"` as its recommended timeframe, and `ValidateWarnings` surfaces a warning when timeframe and template disagree.
- **Preservation**: Existing `ValidateStep` behavior, other template timeframes, and `DonchianBreakoutStrategy` signal logic must remain unchanged.
- **ConfigDraftValidator**: Static class in `Application/Strategy/` that validates `ConfigDraft` fields per builder step.
- **DefaultStrategyTemplates**: Static class in `Application/Strategy/StrategyTemplate.cs` providing built-in `StrategyTemplate` instances.
- **DonchianBreakoutStrategy**: `IStrategy` implementation in `Application/Strategies/` using lagged Donchian channel bands for entry/exit signals.
- **RecommendedTimeframe**: `string` property on `StrategyTemplate` — copied into `DataConfig.Timeframe` by the builder UI.

## Bug Details

### Bug Condition

The bug manifests in two related scenarios:

1. When a user selects `tpl-donchian-breakout`, the builder copies `"Daily"` into `DataConfig.Timeframe`, incorrectly constraining a timeframe-agnostic strategy.
2. When a `ConfigDraft` at step ≥ 2 has a `DataConfig.Timeframe` that differs from the matched template's `RecommendedTimeframe` (and neither is null or `"Any"`), no warning is surfaced.

**Formal Specification:**
```
FUNCTION isBugCondition_TemplateTimeframe(input)
  INPUT: input of type StrategyTemplate
  OUTPUT: boolean

  RETURN input.TemplateId == "tpl-donchian-breakout"
         AND input.RecommendedTimeframe == "Daily"
END FUNCTION

FUNCTION isBugCondition_MissingWarning(draft, templates)
  INPUT: draft of type ConfigDraft, templates of type IReadOnlyList<StrategyTemplate>
  OUTPUT: boolean

  LET template = templates.FirstOrDefault(t => t.TemplateId == draft.TemplateId)
  RETURN draft.CurrentStep >= 2
         AND draft.DataConfig IS NOT NULL
         AND draft.DataConfig.Timeframe IS NOT NULL
         AND draft.DataConfig.Timeframe != "Any"
         AND template IS NOT NULL
         AND template.RecommendedTimeframe IS NOT NULL
         AND template.RecommendedTimeframe != "Any"
         AND draft.DataConfig.Timeframe != template.RecommendedTimeframe
         AND ValidateWarnings(draft, templates) returns empty list
END FUNCTION
```

### Examples

- **Template selection**: User selects `tpl-donchian-breakout` → builder sets `DataConfig.Timeframe = "Daily"`. Expected: `"Any"`. Actual: `"Daily"`.
- **Interval mismatch (no warning)**: User loads 15-minute CSV data with `tpl-vol-trend` (RecommendedTimeframe = "Daily"), `DataConfig.Timeframe = "M15"`. Expected: warning "Data timeframe 'M15' does not match template recommended timeframe 'Daily'". Actual: no warning, backtest proceeds silently.
- **Matching timeframes (no warning expected)**: User loads daily CSV with `tpl-vol-trend`, `DataConfig.Timeframe = "Daily"`. Expected: no warning. Actual: no warning (correct).
- **"Any" template (no warning expected)**: After fix, user loads any data with `tpl-donchian-breakout` (RecommendedTimeframe = "Any"). Expected: no warning regardless of `DataConfig.Timeframe`. Actual: depends on fix.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- `ConfigDraftValidator.ValidateStep` signature and return semantics remain identical — returns `IReadOnlyList<string>` errors, empty when valid.
- All other templates (`tpl-vol-trend`, `tpl-zscore-mr`, `tpl-stationary-mr`, `tpl-regime-rotation`, `tpl-buy-hold`) retain their existing `RecommendedTimeframe = "Daily"` values.
- `DonchianBreakoutStrategy.OnMarketData` signal logic (entry on close > prior upper band, exit on close < prior lower band, long-only) is unchanged.
- `StrategyRegistry` continues to resolve `"donchian-breakout"` from the Application assembly.

**Scope:**
All inputs that do NOT involve the `tpl-donchian-breakout` template's `RecommendedTimeframe` value or the new `ValidateWarnings` method should be completely unaffected. This includes:
- All `ValidateStep` callers (no signature change, no behavioral change)
- Mouse/keyboard interactions in the builder UI
- Strategy execution and signal generation
- Other template defaults and metadata

## Hypothesized Root Cause

Based on the bug description, the most likely issues are:

1. **Incorrect Template Metadata**: The `tpl-donchian-breakout` entry in `DefaultStrategyTemplates.All` was authored with `RecommendedTimeframe = "Daily"` by analogy with other templates, without considering that the Donchian strategy is genuinely timeframe-agnostic (it operates on any bar interval — daily, hourly, 15-minute, etc.).

2. **Missing Validation Path**: `ConfigDraftValidator` was designed with only hard-error validation (`ValidateStep`) for required fields. No soft-warning path exists for advisory checks like timeframe mismatches. The validator simply never had a method to surface warnings.

3. **Missing Test Coverage**: `DonchianBreakoutStrategy` was added without a corresponding test class, unlike the other three built-in strategies which all have dedicated test files. This left warmup, signal correctness, and edge cases unverified.

## Correctness Properties

Property 1: Bug Condition — Donchian Template Timeframe Is "Any"

_For any_ access to `DefaultStrategyTemplates.All` that retrieves the template with `TemplateId == "tpl-donchian-breakout"`, the `RecommendedTimeframe` property SHALL equal `"Any"`.

**Validates: Requirements 2.1**

Property 2: Bug Condition — ValidateWarnings Fires on Mismatch

_For any_ `ConfigDraft` at step ≥ 2 with a non-null, non-"Any" `DataConfig.Timeframe`, paired with a template list containing a matching `TemplateId` whose `RecommendedTimeframe` is non-null, non-"Any", and different from `DataConfig.Timeframe`, `ConfigDraftValidator.ValidateWarnings` SHALL return a non-empty list containing a warning string.

**Validates: Requirements 2.2**

Property 3: Preservation — ValidateWarnings Silent for "Any" Templates

_For any_ `ConfigDraft` paired with a template whose `RecommendedTimeframe` is `"Any"` or null, `ConfigDraftValidator.ValidateWarnings` SHALL return an empty list, regardless of `DataConfig.Timeframe`.

**Validates: Requirements 2.2, 3.1**

Property 4: Preservation — ValidateStep Unchanged

_For any_ `ConfigDraft` input, `ConfigDraftValidator.ValidateStep` SHALL produce the same result as the original implementation — the method signature, return type, and behavior are unchanged.

**Validates: Requirements 3.2**

Property 5: Preservation — DonchianBreakoutStrategy Signal Correctness

_For any_ sequence of `BarEvent` inputs, `DonchianBreakoutStrategy.OnMarketData` SHALL emit `Direction.Long` when close > prior upper band (and not already long), `Direction.Flat` when close < prior lower band (and currently long), and no signals during warmup — using lagged channel values to avoid look-ahead bias.

**Validates: Requirements 2.3, 3.3**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `src/TradingResearchEngine.Application/Strategy/StrategyTemplate.cs`

**Change**: In `DefaultStrategyTemplates.All`, change the `tpl-donchian-breakout` entry's `RecommendedTimeframe` from `"Daily"` to `"Any"`.

**Specific Changes**:
1. **Template Metadata Fix**: Replace `"Daily"` with `"Any"` in the `RecommendedTimeframe` argument of the `tpl-donchian-breakout` `StrategyTemplate` constructor call. Single-line change.

---

**File**: `src/TradingResearchEngine.Application/Strategy/ConfigDraftValidator.cs`

**Method**: New static method `ValidateWarnings`

**Specific Changes**:
2. **Add ValidateWarnings Method**: Add a new `public static IReadOnlyList<string> ValidateWarnings(ConfigDraft draft, IReadOnlyList<StrategyTemplate> templates)` method that:
   - Returns an empty list if `draft.CurrentStep < 2` or `draft.DataConfig` is null or `draft.TemplateId` is null.
   - Finds the matching template by `TemplateId` (confirmed: `ConfigDraft.TemplateId` is `string?`, populated when `SourceType` is Template).
   - Returns an empty list if no matching template is found.
   - Returns an empty list if either `DataConfig.Timeframe` or `template.RecommendedTimeframe` is null or `"Any"`.
   - Returns a single warning string when both values are non-null, non-"Any", and mismatched.
   - Warning message format: `"Data timeframe '{DataConfig.Timeframe}' does not match the template's recommended timeframe '{template.RecommendedTimeframe}'. Results may be unexpected."`
   - Uses `StringComparison.OrdinalIgnoreCase` for all string comparisons.

3. **XML Doc Comment**: Add `/// <summary>` documentation on the new method describing its purpose and return semantics.

---

**File**: `src/TradingResearchEngine.UnitTests/Strategy/DonchianBreakoutStrategyTests.cs` (new)

**Specific Changes**:
4. **DonchianBreakoutStrategy Tests**: Create a new test class covering:
   - Warmup period returns no signals (feed ≤ period + 1 bars)
   - Entry signal on channel breakout (close > prior upper band)
   - Exit signal on channel breakdown (close < prior lower band)
   - No duplicate Long signals while already in position
   - Look-ahead exclusion (channel computed from prior bars, not current bar)
   - Edge case: `period = 1`

---

**File**: `src/TradingResearchEngine.UnitTests/Strategy/ConfigDraftValidatorWarningTests.cs` (new)

**Specific Changes**:
5. **ConfigDraftValidator Warning Tests**: Create a new test class covering:
   - No warning when `RecommendedTimeframe` is `"Any"`
   - No warning when `RecommendedTimeframe` is null
   - No warning when `DataConfig.Timeframe` is null
   - Warning fires when both values are non-null, non-"Any", and mismatched
   - No warning when both values match
   - No warning when `CurrentStep < 2`
   - No warning when `TemplateId` is null (no template match)

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bugs on unfixed code, then verify the fixes work correctly and preserve existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bugs BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write tests that assert the expected post-fix behavior and run them on the UNFIXED code to observe failures.

**Test Cases**:
1. **Template Timeframe Test**: Assert `DefaultStrategyTemplates.All.First(t => t.TemplateId == "tpl-donchian-breakout").RecommendedTimeframe == "Any"` — will fail on unfixed code (returns `"Daily"`).
2. **Warning Fires Test**: Create a `ConfigDraft` with `DataConfig.Timeframe = "M15"` and a template with `RecommendedTimeframe = "Daily"`, call `ValidateWarnings` — will fail on unfixed code (method does not exist).

**Expected Counterexamples**:
- Template test fails: `RecommendedTimeframe` is `"Daily"` instead of `"Any"`
- Warning test fails: `ValidateWarnings` method does not exist on `ConfigDraftValidator`

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed code produces the expected behavior.

**Pseudocode:**
```
FOR ALL template WHERE template.TemplateId == "tpl-donchian-breakout" DO
  ASSERT template.RecommendedTimeframe == "Any"
END FOR

FOR ALL (draft, templates) WHERE isBugCondition_MissingWarning(draft, templates) DO
  result := ValidateWarnings(draft, templates)
  ASSERT result.Count > 0
  ASSERT result[0] == "Data timeframe '{draft.DataConfig.Timeframe}' does not match the template's recommended timeframe '{template.RecommendedTimeframe}'. Results may be unexpected."
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed code produces the same result as the original code.

**Pseudocode:**
```
FOR ALL draft DO
  ASSERT ValidateStep_fixed(draft) == ValidateStep_original(draft)
END FOR

FOR ALL template WHERE template.TemplateId != "tpl-donchian-breakout" DO
  ASSERT template.RecommendedTimeframe == template_original.RecommendedTimeframe
END FOR

FOR ALL (draft, templates) WHERE template.RecommendedTimeframe IN {null, "Any"} DO
  ASSERT ValidateWarnings(draft, templates).Count == 0
END FOR
```

**Testing Approach**: Example-based unit tests are sufficient for these fixes because:
- The template change is a single literal — one assertion covers it
- The `ValidateWarnings` method has a small, well-defined input space that can be exhaustively covered with ~7 test cases
- The `DonchianBreakoutStrategy` tests use deterministic bar sequences with known expected outputs

**Test Plan**: Observe behavior on UNFIXED code first for `ValidateStep` and other templates, then write tests to verify these continue unchanged after the fix.

**Test Cases**:
1. **ValidateStep Preservation**: Verify `ValidateStep` returns the same errors for the same drafts before and after the fix
2. **Other Template Preservation**: Verify `tpl-vol-trend`, `tpl-zscore-mr`, etc. still have `RecommendedTimeframe = "Daily"`
3. **Strategy Signal Correctness** (additive coverage — strategy code is not changing):
   - Warmup returns no signals
   - Entry signal on channel breakout (close > prior upper band)
   - Exit signal on channel breakdown (close < prior lower band)
   - No duplicate Long signals while already in position
   - Look-ahead exclusion (channel computed from prior bars, not current bar)
   - Edge case: `period = 1`

### Unit Tests

- `DonchianBreakoutStrategyTests`: warmup, entry, exit, duplicate prevention, look-ahead exclusion, edge cases
- `ConfigDraftValidatorWarningTests`: all warning/no-warning scenarios for `ValidateWarnings`
- Existing `ConfigDraftValidator` tests (if any) remain unchanged

### Property-Based Tests

Not required for this bugfix. The input spaces are small and well-defined:
- Template metadata is a static list (6 entries)
- `ValidateWarnings` has ~5 boolean conditions that can be exhaustively covered with example-based tests
- `DonchianBreakoutStrategy` tests use deterministic bar sequences

### Integration Tests

- No integration tests required. All changes are in Application layer (static methods and strategy logic) with no I/O, no DI changes, and no API surface changes.
- The existing `StrategyRegistry` integration (assembly scanning) is unaffected since `DonchianBreakoutStrategy` is not being modified.
