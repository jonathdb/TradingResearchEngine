# Bugfix Requirements Document

## Introduction

The Donchian Breakout strategy (`donchian-breakout`) exists in the codebase and is registered at startup, but has three issues that affect correctness and usability:

1. The `DefaultStrategyTemplates` entry for `tpl-donchian-breakout` sets `RecommendedTimeframe = "Daily"`, but the strategy is timeframe-agnostic. When the UI builder copies this value into `DataConfig.Timeframe`, it misleads users into thinking the strategy only works on daily data and can cause interval mismatches with non-daily CSV data.

2. `ConfigDraftValidator` validates required fields per builder step but does not warn when `DataConfig.Timeframe` conflicts with the strategy template's `RecommendedTimeframe`. Users can unknowingly create scenarios with mismatched intervals and receive zero trades with no explanation.

3. No unit tests exist for `DonchianBreakoutStrategy`. The other built-in strategies (`VolatilityScaledTrendStrategy`, `ZScoreMeanReversionStrategy`, `BaselineBuyAndHoldStrategy`) all have dedicated test classes, but the Donchian strategy has none — leaving warmup behavior, signal correctness, duplicate signal prevention, and look-ahead exclusion unverified.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a user selects the `tpl-donchian-breakout` template in the strategy builder THEN the system copies `RecommendedTimeframe = "Daily"` into the draft's `Timeframe` field, incorrectly constraining a timeframe-agnostic strategy to daily data.

1.2 WHEN a `ConfigDraft` has a `DataConfig.Timeframe` value that conflicts with the actual data interval (e.g. template says "Daily" but CSV contains 15-minute bars) THEN the system produces no validation warning, allowing the user to proceed to a backtest that may yield zero trades with no explanation.

1.3 WHEN the `DonchianBreakoutStrategy` is modified or refactored THEN the system has no unit tests to catch regressions in warmup behavior, entry/exit signal generation, duplicate signal prevention, or look-ahead bias exclusion.

### Expected Behavior (Correct)

2.1 WHEN a user selects the `tpl-donchian-breakout` template in the strategy builder THEN the system SHALL copy `RecommendedTimeframe = "Any"` into the draft's `Timeframe` field, correctly reflecting that the strategy works on any bar interval.

2.2 WHEN a `ConfigDraft` at step 2 or later has a `DataConfig` with a `Timeframe` value that does not match the strategy template's `RecommendedTimeframe` (and neither value is `"Any"` or null) THEN the system SHALL return a warning (not a hard error) via a new `ValidateWarnings(ConfigDraft, IReadOnlyList<StrategyTemplate>)` method on `ConfigDraftValidator`, keeping the existing `ValidateStep` return type unchanged. This means templates with `RecommendedTimeframe = "Any"` (e.g. donchian-breakout after fix 2.1) will correctly never trigger this warning.

2.3 WHEN the `DonchianBreakoutStrategy` is part of the codebase THEN the system SHALL have unit tests covering: warmup returns no signals, entry signal on channel breakout, exit signal on channel breakdown, no duplicate Long signals while already in position, look-ahead exclusion (channel computed from prior bars only), and edge cases (e.g. `period = 1`).

2.4 WHEN the `ConfigDraftValidator.ValidateWarnings` method is added THEN the system SHALL have unit tests covering: no warning when `RecommendedTimeframe` is `"Any"`, no warning when `RecommendedTimeframe` is null, warning fires when both `DataConfig.Timeframe` and `RecommendedTimeframe` are non-null, non-`"Any"`, and mismatched, and no warning when both values match.

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a user selects any other strategy template (e.g. `tpl-vol-trend`, `tpl-zscore-mr`) THEN the system SHALL CONTINUE TO copy the existing `RecommendedTimeframe` value ("Daily") into the draft's `Timeframe` field unchanged.

3.2 WHEN a `ConfigDraft` has valid required fields for its current step THEN the system SHALL CONTINUE TO return an empty error list from `ConfigDraftValidator.ValidateStep` with no signature changes. The new `ValidateWarnings` method is a separate entry point returning `IReadOnlyList<string>` warnings, so existing callers of `ValidateStep` are unaffected.

3.3 WHEN the `DonchianBreakoutStrategy` receives `BarEvent` data THEN the system SHALL CONTINUE TO produce correct entry signals (close > prior upper band), correct exit signals (close < prior lower band), and maintain long-only behavior (no `Direction.Short` signals).

3.4 WHEN `StrategyRegistry.RegisterAssembly` is called with the Application assembly THEN the system SHALL CONTINUE TO register `donchian-breakout` in `KnownNames` and resolve it correctly.
