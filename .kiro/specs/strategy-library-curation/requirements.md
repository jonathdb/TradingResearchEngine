# Requirements — Strategy Library Curation & Research Alignment

## Introduction

The current built-in strategy catalog contains 8 strategy implementations and 6 templates, but suffers from significant overlap and weak research alignment. Three strategies (SmaCrossover, BollingerBands, MeanReversion) are research-overlapping — all implement the same "buy below rolling mean, sell above" hypothesis with different indicator wrappers. Two breakout strategies (Breakout, DonchianBreakout) are near-duplicates. The RSI strategy is another oscillator-threshold variant that overlaps with mean reversion. Meanwhile, the sample scenario files include 3 SMA crossover variants that differ only by data source.

This spec curates the built-in strategy library into a focused set of distinct quant archetypes, adds lightweight metadata for discoverability, aligns the builder/detail UX with hypothesis-driven research, and defines per-strategy research workflow recommendations. The goal is a default library that teaches good research behavior, not just provides code.

### Strategy Audit Summary

| Current Strategy | Family | Verdict | Rationale |
|---|---|---|---|
| SmaCrossoverStrategy | Trend | **Remove** — replaced by VolatilityScaledTrend | Toy-level trend demo, no risk normalization, no vol awareness |
| BollingerBandsStrategy | MeanReversion | **Merge** into ZScoreMeanReversion | Bollinger entry is just z-score < -K on close; redundant with a proper z-score reversion strategy |
| MeanReversionStrategy | MeanReversion | **Merge** into ZScoreMeanReversion | Same hypothesis as Bollinger, same rolling-mean-deviation logic |
| StationaryMeanReversionStrategy | MeanReversion | **Keep, reframe (metadata only)** | Unique ADF stationarity filter adds genuine research value; reframe via descriptor display name and hypothesis — runtime strategy type key `stationary-mean-reversion` is unchanged |
| DonchianBreakoutStrategy | Breakout | **Keep as-is** | Clean Donchian channel breakout, good research baseline, lagged bands avoid lookahead |
| BreakoutStrategy | Breakout | **Remove** — redundant with Donchian | Identical N-bar high/low channel logic but without lagged bands (weaker implementation) |
| RsiStrategy | MeanReversion | **Remove** — research-overlapping with z-score reversion | RSI oversold/overbought is another oscillator-threshold mean reversion variant |
| MacroRegimeRotationStrategy | RegimeAware | **Keep, reframe (metadata only)** | Unique regime-switching logic, directly exercises V4 regime segmentation; runtime strategy type key `macro-regime-rotation` is unchanged |

### Target Strategy Catalog (Tier 1)

| Strategy | Family | Hypothesis | Status |
|---|---|---|---|
| VolatilityScaledTrendStrategy | Trend | Persistent directional moves continue long enough for trend entries to overcome costs when sized by volatility | **New** — replaces SmaCrossover |
| ZScoreMeanReversionStrategy | MeanReversion | Short-term price dislocations around a rolling equilibrium tend to mean-revert in non-trending regimes | **New** — consolidates Bollinger + MeanReversion |
| DonchianBreakoutStrategy | Breakout | Range expansion after compression signals the start of a sustained move | **Keep** |
| StationaryMeanReversionStrategy | MeanReversion | Mean reversion signals are only reliable when the return series is statistically stationary | **Keep, reframe (metadata only — type key unchanged)** |
| MacroRegimeRotationStrategy | RegimeAware | No single signal family dominates across all regimes; switching behavior by regime improves robustness | **Keep, reframe (metadata only — type key unchanged)** |
| BaselineBuyAndHoldStrategy | Benchmark | Markets have a positive long-term drift; any active strategy must outperform passive exposure to justify its complexity | **New** |

### Tier 2 (Deferred)

| Strategy | Family | Reason for Deferral |
|---|---|---|
| PairsMeanReversionStrategy | RelativeValue | Engine does not support multi-series input; requires architecture extension |
| CrossSectionalMomentumStrategy | CrossSectional | Engine does not support portfolio/universe-level strategy execution |

---

## Requirements

### Requirement 1 — Strategy Catalog Consolidation

**User Story:** As a researcher, I want the built-in strategy library to contain one strong representative per quant family, so that I can compare genuinely different return-generation hypotheses without wading through near-duplicates.

#### Acceptance Criteria

1. THE default built-in strategy template set SHALL contain exactly 6 templates. THE built-in registry SHALL include at least these 6 strategy types: `volatility-scaled-trend`, `zscore-mean-reversion`, `donchian-breakout`, `stationary-mean-reversion`, `macro-regime-rotation`, `baseline-buy-and-hold`.
2. THE following strategy classes SHALL be removed from the Application/Strategies folder: `SmaCrossoverStrategy`, `BollingerBandsStrategy`, `MeanReversionStrategy`, `BreakoutStrategy`, `RsiStrategy`.
3. WHEN a removed strategy type is referenced in an existing saved `StrategyIdentity` or `ScenarioConfig` JSON file, THEN the `StrategyRegistry.Resolve` call SHALL throw `StrategyNotFoundException` with a message listing the known strategy names — no silent fallback, no crash. This is existing behavior and requires no change.
4. THE `DefaultStrategyTemplates.All` list SHALL be updated to contain exactly 6 templates matching the 6 retained/new strategies.
5. ALL sample scenario JSON files under `samples/scenarios/` SHALL be replaced with one scenario per Tier 1 strategy, each using the `csv` data provider and `samples/data/spy-daily.csv`.
6. ALL references in documentation and tests to removed sample scenario filenames or removed strategy types SHALL be updated as part of the same change.

---

### Requirement 2 — New Strategy: VolatilityScaledTrendStrategy

**User Story:** As a researcher, I want a trend-following strategy that normalizes signals by volatility, so that I have a more realistic trend archetype than a plain MA crossover.

#### Acceptance Criteria

1. THE `VolatilityScaledTrendStrategy` SHALL be registered with `[StrategyName("volatility-scaled-trend")]`.
2. THE strategy SHALL compute a trend signal using a fast/slow SMA crossover (configurable periods, defaults: fast=10, slow=50).
3. THE strategy SHALL compute a trailing ATR (configurable period, default: 14) as a volatility measure.
4. THE signal strength emitted in `SignalEvent.Strength` SHALL be `Close / ATR`, providing a volatility-normalized signal that the RiskLayer can use for position sizing.
5. ENTRY: WHEN fast SMA crosses above slow SMA AND the strategy is not already long, THEN the strategy SHALL emit `Direction.Long`.
6. EXIT: WHEN fast SMA crosses below slow SMA AND the strategy is long, THEN the strategy SHALL emit `Direction.Flat`.
7. THE strategy SHALL use O(1) rolling accumulators for SMA and ATR computation (no full-window recalculation per bar).
8. CONSTRUCTOR parameters SHALL be: `fastPeriod` (int, default 10), `slowPeriod` (int, default 50), `atrPeriod` (int, default 14).

---

### Requirement 3 — New Strategy: ZScoreMeanReversionStrategy

**User Story:** As a researcher, I want a clean z-score mean reversion strategy, so that I have a single, well-defined mean reversion archetype that consolidates the overlapping Bollinger/MeanReversion demos.

#### Acceptance Criteria

1. THE `ZScoreMeanReversionStrategy` SHALL be registered with `[StrategyName("zscore-mean-reversion")]`.
2. THE strategy SHALL compute a rolling z-score of close price: `z = (Close - SMA) / StdDev` over a configurable lookback (default: 30).
3. ENTRY: WHEN z-score drops below `-entryThreshold` (default: 2.0) AND the strategy is not already long, THEN the strategy SHALL emit `Direction.Long`.
4. EXIT: WHEN z-score rises above `exitThreshold` (default: 0.0, i.e. mean reversion to the SMA) AND the strategy is long, THEN the strategy SHALL emit `Direction.Flat`.
5. THE strategy SHALL use O(1) rolling sum and sum-of-squares accumulators.
6. CONSTRUCTOR parameters SHALL be: `lookback` (int, default 30), `entryThreshold` (decimal, default 2.0), `exitThreshold` (decimal, default 0.0).

---

### Requirement 4 — New Strategy: BaselineBuyAndHoldStrategy

**User Story:** As a researcher, I want a buy-and-hold benchmark strategy, so that I can compare any active strategy's performance against passive market exposure.

#### Acceptance Criteria

1. THE `BaselineBuyAndHoldStrategy` SHALL be registered with `[StrategyName("baseline-buy-and-hold")]`.
2. THE strategy SHALL emit `Direction.Long` on the first bar after a configurable warmup period (default: 1 bar) and never exit.
3. THE strategy SHALL emit no further signals after the initial entry.
4. CONSTRUCTOR parameters SHALL be: `warmupBars` (int, default 1).

---

### Requirement 5 — Strategy Descriptor Metadata

**User Story:** As a researcher, I want each built-in strategy to expose structured metadata (display name, family, hypothesis, best-fit conditions, suggested studies), so that the builder and detail UX can present meaningful context instead of raw type names.

#### Acceptance Criteria

1. A `StrategyDescriptor` record SHALL be defined in Application/Strategy with fields: `StrategyType` (string), `DisplayName` (string), `Family` (string), `Description` (string), `Hypothesis` (string), `BestFor` (string, nullable), `SuggestedStudies` (string array, nullable).
2. A `StrategyFamily` static class SHALL define string constants: `Trend`, `MeanReversion`, `Breakout`, `RegimeAware`, `Benchmark`.
3. THE `StrategyTemplate` record SHALL gain an optional `StrategyDescriptor? Descriptor` trailing parameter (default null) for backwards-compatible deserialization.
4. ALL 6 `DefaultStrategyTemplates` SHALL include a populated `StrategyDescriptor`.
5. THE `StrategyDescriptor.Hypothesis` field SHALL use research-grade language aligned with the V4 `StrategyIdentity.Hypothesis` concept.
6. DESCRIPTOR lookup SHALL be by `StrategyType` string match against `StrategyTemplate.StrategyType`. A missing descriptor is non-fatal; the UI SHALL fall back to current behavior (raw type name, no family badge, no suggested studies).

---

### Requirement 6 — Builder UX: Strategy Selection Enrichment

**User Story:** As a researcher, I want the Strategy Builder template selection step to show each strategy's display name, family, description, and hypothesis, so that I can make an informed choice based on research intent rather than indicator names.

#### Acceptance Criteria

1. THE Strategy Builder Step 1 (Template) SHALL display for each template card: display name, family badge, short description, and research hypothesis.
2. THE family badge SHALL use a distinct color per family: Trend (blue), MeanReversion (purple), Breakout (orange), RegimeAware (teal), Benchmark (grey).
3. WHEN a template with a `StrategyDescriptor` is selected, THEN the Strategy Builder Step 5 (Review) SHALL pre-fill the `Hypothesis` field on the created `StrategyIdentity` with the descriptor's hypothesis text. The user MAY edit it before saving. Prefill occurs only during creation from template and SHALL NOT overwrite an existing user-authored hypothesis on edit or clone flows.
4. THE template card layout SHALL remain a grid of cards; no structural UX change beyond adding the metadata fields.

---

### Requirement 7 — Strategy Detail: Descriptor Display

**User Story:** As a researcher, I want the Strategy Detail screen to show the strategy's family, hypothesis, and suggested studies, so that I have research context when reviewing results.

#### Acceptance Criteria

1. THE Strategy Detail header band SHALL display a family badge next to the strategy type chip, sourced from the matching `StrategyDescriptor` (if one exists for the strategy type).
2. THE Strategy Detail "LAUNCH STUDY" bar SHALL highlight suggested studies from the descriptor with a subtle visual indicator (e.g. a star icon or "Recommended" label).
3. IF no `StrategyDescriptor` exists for the strategy type (custom/user-created strategies), THEN the family badge SHALL not render and the study bar SHALL show all studies without recommendations.

---

### Requirement 8 — Research Workflow Alignment

**User Story:** As a researcher, I want each built-in strategy to document which V4 research studies are most appropriate, so that the sample library teaches good research behavior.

#### Acceptance Criteria

1. EACH `StrategyDescriptor` for the 6 built-in strategies SHALL include a `SuggestedStudies` array with study type names.
2. THE suggested studies SHALL follow these assignments:
   - `volatility-scaled-trend`: Walk-Forward, AnchoredWalkForward, MonteCarlo, RegimeSegmentation
   - `zscore-mean-reversion`: Sensitivity, RegimeSegmentation, MonteCarlo, ParameterStability
   - `donchian-breakout`: Walk-Forward, MonteCarlo, Sensitivity
   - `stationary-mean-reversion`: RegimeSegmentation, Sensitivity, MonteCarlo, ParameterStability
   - `macro-regime-rotation`: RegimeSegmentation, Walk-Forward, AnchoredWalkForward, Realism
   - `baseline-buy-and-hold`: MonteCarlo (as a benchmark comparison baseline only)
3. THE `StrategyDescriptor.BestFor` field SHALL describe the market conditions where the strategy is expected to perform well (e.g. "Trending markets with sustained directional moves").

---

### Requirement 9 — Sample Scenario File Cleanup

**User Story:** As a researcher, I want the sample scenario files to be a clean, non-redundant set that matches the curated strategy catalog, so that the samples directory is a useful starting point rather than a confusing collection of duplicates.

#### Acceptance Criteria

1. THE `samples/scenarios/` directory SHALL contain exactly 6 JSON files, one per Tier 1 strategy.
2. EACH scenario file SHALL use: `csv` data provider, `samples/data/spy-daily.csv`, `InitialCash` of 100000, `AnnualRiskFreeRate` of 0.05, and the strategy's default parameters from its template.
3. EACH scenario file SHALL include a `Description` field that states the strategy's hypothesis in plain language.
4. THE following scenario files SHALL be removed: `sma-crossover-spy.json`, `sma-crossover-dukascopy.json`, `sma-crossover-yahoo.json`, `bollinger-bands-spy.json`.
5. THE following scenario files SHALL be replaced with updated versions: `donchian-breakout-spy.json`, `macro-regime-spy.json`, `stationary-meanrev-spy.json`.
6. NEW scenario files SHALL be added: `volatility-scaled-trend-spy.json`, `zscore-meanrev-spy.json`, `baseline-buy-and-hold-spy.json`.

---

### Requirement 10 — Unit Tests for New and Revised Strategies

**User Story:** As a developer, I want unit tests for all new and revised strategies, so that the strategy catalog is verified and regressions are caught.

#### Acceptance Criteria

1. EACH new strategy (VolatilityScaledTrend, ZScoreMeanReversion, BaselineBuyAndHold) SHALL have a corresponding test class in UnitTests following the naming convention `<StrategyName>StrategyTests`.
2. EACH test class SHALL verify: warmup behavior (no signals before sufficient bars), entry signal generation, exit signal generation, and no-signal when already in the correct state.
3. THE `VolatilityScaledTrendStrategy` tests SHALL verify that `SignalEvent.Strength` equals `Close / ATR` on entry signals.
4. THE `BaselineBuyAndHoldStrategy` tests SHALL verify that exactly one Long signal is emitted and no Flat signal is ever emitted.
5. THE `ZScoreMeanReversionStrategy` tests SHALL verify entry at z < -threshold and exit at z > exitThreshold.
6. EXISTING tests for removed strategies (SmaCrossover, Bollinger, MeanReversion, Breakout, RSI) SHALL be removed or migrated to test the replacement strategy.

---

### Requirement 11 — Backwards Compatibility

**User Story:** As an existing user, I want my previously saved strategies and runs to remain loadable after the strategy catalog changes, so that I don't lose research history.

#### Acceptance Criteria

1. REMOVING strategy classes SHALL NOT delete or modify any persisted JSON files (StrategyIdentity, StrategyVersion, BacktestResult).
2. EXISTING `StrategyIdentity` records referencing removed strategy types SHALL remain loadable in the Strategy Library and Strategy Detail screens.
3. ATTEMPTING to run a backtest with a removed strategy type SHALL produce a clear `StrategyNotFoundException` error with the list of available strategy names.
4. THE `StrategyTemplate` list change SHALL NOT affect previously saved `StrategyVersion` records — templates are used only at creation time.

