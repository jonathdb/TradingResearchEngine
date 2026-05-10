# Requirements Document

## Introduction

This document captures the requirements for the TradingResearchEngine implementation batch covering critical bug fixes, architecture improvements, robustness enhancements, and UX improvements. The system is a .NET 8 / Blazor Server / MudBlazor event-driven backtesting engine for quantitative strategy research. Requirements are organized by priority (P0 critical → P3 polish) and follow EARS patterns with INCOSE quality rules.

## Glossary

- **Engine**: The `BacktestEngine` component in Core that processes market data events through the heartbeat loop and dispatches them to strategy, risk, and execution layers.
- **Strategy_Builder**: The Blazor Server page (`StrategyBuilder.razor`) providing the 5-step wizard for creating and configuring trading strategies.
- **Job_Executor**: The Application-layer service (`JobExecutor`) responsible for enqueueing, executing, and tracking backtest jobs on background threads.
- **Strategy_Factory**: The `IStrategyFactory` interface in Core that creates isolated `IStrategy` instances for parallel workflows.
- **Fill_Engine**: The execution engine components (`TryFillLimit`, `TryFillStopMarket`, `TryFillStopLimit`) responsible for simulating order fills against bar data.
- **Metrics_Calculator**: The `MetricsCalculator` static class in Core that computes performance metrics (Sharpe, Sortino, Calmar, VaR, CVaR) from equity curves.
- **Walk_Forward_Workflow**: The Application-layer research workflow that runs in-sample optimization followed by out-of-sample validation across rolling windows.
- **Parameter_Sweep_Workflow**: The Application-layer research workflow that evaluates strategy performance across a grid of parameter combinations.
- **Study_Detail_Page**: The Blazor page (`StudyDetail.razor`) that displays results for completed research studies.
- **Dashboard**: The Blazor page (`Dashboard.razor`) showing recent runs, strategy cards, and robustness warnings.
- **Repository**: The `IRepository<T>` interface and its `SqliteIndexRepository<T>` implementation providing JSON persistence with SQLite indexing.
- **Background_Study_Service**: The Application-layer service that executes long-running research studies and emits progress events.
- **CPCV_Workflow**: The Combinatorial Purged Cross-Validation workflow producing out-of-sample path Sharpe distributions.
- **Sensitivity_Workflow**: The research workflow that perturbs execution parameters (slippage, commission, fill delay) to measure strategy robustness.
- **Checklist_Service**: The `ResearchChecklistService` that evaluates a 9-item confidence checklist against backtest results.
- **AI_Strategy_Assistant**: The `IAIStrategyAssistant` interface providing AI-powered strategy generation and refinement via Google Gemini.
- **Interpretation_Service**: The `StudyInterpretationService` that generates result-aware textual interpretations of study outcomes.
- **Skender_Bridge**: The `SkenderBridgeIndicator` generic adapter that wraps any Skender.Stock.Indicators indicator without hand-written subclasses.
- **Indicator_Catalog**: The `SkenderIndicatorCatalog` metadata registry describing all available Skender indicators with parameters and output fields.
- **Renderer_Registry**: The `StudyRendererRegistry` mapping `StudyType` values to Blazor renderer components.
- **BarsPerYear**: The `ScenarioConfig.BarsPerYear` field serving as the canonical source of truth for annualization across all timeframes.
- **Direction**: The `Direction` enum (`Long`, `Short`, `Flat`) representing trade direction in the engine.
- **SignalR_Circuit**: The Blazor Server SignalR connection that handles UI interactions; blocking this thread freezes the entire UI.

## Requirements

### Requirement 1: Async Backtest Job Dispatch

**User Story:** As a researcher, I want backtest execution to run off the Blazor SignalR thread, so that the UI remains responsive during long-running backtests.

#### Acceptance Criteria

1. WHEN the user clicks "Launch Backtest" in the Strategy_Builder, THE Job_Executor SHALL enqueue the backtest as a background job and immediately navigate to a status page without blocking the SignalR_Circuit.
2. WHILE a backtest job is running, THE Job_Status_Page SHALL display live progress updates polled every 2 seconds via a `PeriodicTimer`.
3. WHEN a backtest job completes successfully, THE Job_Status_Page SHALL automatically redirect the user to the backtest result page after a 1-second delay.
4. IF a backtest job fails, THEN THE Job_Status_Page SHALL display the error message in a severity-error alert with an "Edit & Retry" button linking back to the Strategy_Builder.
5. THE Strategy_Builder SHALL contain zero inline `await RunUseCase.RunAsync(...)` calls after this change.

---

### Requirement 2: Strategy Factory for Parallel Isolation

**User Story:** As a researcher, I want parallel workflows to use isolated strategy instances, so that walk-forward and parameter sweep results are free from shared-state data races.

#### Acceptance Criteria

1. THE Strategy_Factory SHALL be defined in Core with zero references to Application or Web layers.
2. WHEN `Create(StrategyConfig)` is called on a Strategy_Factory, THE Strategy_Factory SHALL return a new independent `IStrategy` instance with its own mutable state.
3. THE Walk_Forward_Workflow SHALL call `factory.Create(config)` for each parallel iteration instead of reusing a single strategy instance.
4. THE Parameter_Sweep_Workflow SHALL call `factory.Create(config)` for each parallel iteration instead of reusing a single strategy instance.
5. WHEN 20 concurrent instances are created from the same factory and executed in parallel, THE Strategy_Factory SHALL produce independent results with no shared mutable state.

---

### Requirement 3: Short Direction Fill Logic

**User Story:** As a researcher, I want short-direction limit, stop-market, and stop-limit orders to fill correctly, so that short-selling strategies can be backtested accurately.

#### Acceptance Criteria

1. WHEN a `Direction.Short` limit order is pending and the bar's High is greater than or equal to the limit price, THE Fill_Engine SHALL fill the order at the limit price using bid-side pricing.
2. WHEN a `Direction.Short` stop-market order is pending and the bar's Low is less than or equal to the stop price, THE Fill_Engine SHALL trigger and fill the order at the stop price.
3. WHEN a `Direction.Short` stop-limit order is pending and the bar's Low is less than or equal to the stop price and the bar's High is greater than or equal to the limit price, THE Fill_Engine SHALL trigger and fill the order at the limit price.
4. WHEN fill conditions are not met for a `Direction.Short` order, THE Fill_Engine SHALL return null without filling.
5. THE Fill_Engine SHALL maintain all existing `Direction.Long` fill behavior without regression.

---

### Requirement 4: Sortino Ratio Downside Deviation Correction

**User Story:** As a researcher, I want the Sortino Ratio to use the standard downside deviation formula, so that risk-adjusted return metrics are mathematically correct.

#### Acceptance Criteria

1. THE Metrics_Calculator SHALL compute downside deviation using ALL period returns, zeroing out upside deviations: `downsideDev = sqrt(mean(min(r - threshold, 0)^2 for all r))`.
2. WHEN all period returns are positive, THE Metrics_Calculator SHALL return a non-null Sortino value (null only when `downsideDev == 0`).
3. WHEN a known synthetic return series with mixed winning and losing periods is provided, THE Metrics_Calculator SHALL produce a Sortino value matching the hand-computed expected result.
4. THE Metrics_Calculator SHALL accept `int barsPerYear` for Sortino annualization and use `ScenarioConfig.BarsPerYear` as the source value.

---

### Requirement 5: Calmar Ratio BarsPerYear Annualization

**User Story:** As a researcher, I want the Calmar Ratio to use `BarsPerYear` for annualization, so that intraday timeframes produce correct risk-adjusted metrics.

#### Acceptance Criteria

1. THE Metrics_Calculator SHALL accept `int barsPerYear` as a parameter to `ComputeCalmarRatio`.
2. THE Metrics_Calculator SHALL annualize returns using `meanReturn * barsPerYear` instead of a hardcoded 252-day approximation.
3. WHEN the same equity curve is evaluated with `barsPerYear=131040` (M1) and `barsPerYear=252` (D1), THE Metrics_Calculator SHALL produce different Calmar values reflecting the correct annualization for each timeframe.
4. THE Engine SHALL pass `ScenarioConfig.BarsPerYear` to all Calmar Ratio call sites.

---

### Requirement 6: Historical VaR Small-Sample Guard

**User Story:** As a researcher, I want VaR and CVaR to return null for small samples, so that misleading risk metrics are not displayed for insufficient data.

#### Acceptance Criteria

1. WHEN the equity curve contains fewer than 30 period returns, THE Metrics_Calculator SHALL return null for `ComputeHistoricalVaR`.
2. WHEN the equity curve contains fewer than 30 period returns, THE Metrics_Calculator SHALL return null for `ComputeHistoricalCVaR`.
3. WHEN the equity curve contains 30 or more period returns, THE Metrics_Calculator SHALL compute and return the correct VaR value at the specified confidence level.

---

### Requirement 7: Strategy Lifecycle Hooks

**User Story:** As a researcher, I want strategies to support `Initialize()` and `Reset()` lifecycle methods, so that walk-forward workflows can reuse instances between windows without reconstruction overhead.

#### Acceptance Criteria

1. THE `IStrategy` interface SHALL define an `Initialize(StrategyConfig config)` method called once before the first bar of a new execution window.
2. THE `IStrategy` interface SHALL define a `Reset()` method that clears all indicator state and internal tracking.
3. WHEN `Reset()` is called on a strategy after processing 50 bars, and the strategy is then run for another 50 bars, THE strategy SHALL produce results identical to a freshly constructed instance.
4. THE Walk_Forward_Workflow SHALL call `strategy.Reset()` before each out-of-sample window instead of creating a new instance.

---

### Requirement 8: Pluggable Study Result Renderer

**User Story:** As a developer, I want study result rendering to use a pluggable registry pattern, so that adding new study types requires no modification to `StudyDetail.razor`.

#### Acceptance Criteria

1. THE Study_Detail_Page SHALL contain no `switch` statement on `StudyType` for result rendering.
2. THE Renderer_Registry SHALL map each `StudyType` to a dedicated Blazor renderer component.
3. WHEN a new study type is added, THE system SHALL require only a new renderer component and a registry entry — no changes to the Study_Detail_Page.
4. THE rendered output for all existing study types SHALL be visually identical to the pre-refactor display.

---

### Requirement 9: Dashboard Pagination

**User Story:** As a user, I want the Dashboard to load only recent runs via database-level pagination, so that page load performance does not degrade as the result count grows.

#### Acceptance Criteria

1. THE Repository SHALL provide a `ListRecentAsync(int count, CancellationToken ct)` method that uses a database-level `LIMIT` clause.
2. WHEN the Dashboard loads, THE Dashboard SHALL issue at most 2 repository queries, neither of which loads all results into memory.
3. THE Dashboard SHALL display robustness flags correctly using only the paginated result set.

---

### Requirement 10: Live Study Progress Display

**User Story:** As a researcher, I want to see live progress bars during long-running studies, so that I have feedback on execution status without polling the database.

#### Acceptance Criteria

1. WHILE a Monte Carlo study is running, THE Study_Detail_Page SHALL display a progress bar showing completed simulations out of total.
2. WHILE a parameter sweep is running, THE Study_Detail_Page SHALL display progress across all parameter combinations.
3. WHEN a study completes, THE Study_Detail_Page SHALL automatically hide the progress bar and render the final results.
4. WHEN the Study_Detail_Page is disposed, THE page SHALL unsubscribe from all Background_Study_Service event handlers to prevent memory leaks.

---

### Requirement 11: CPCV Distribution Visualization

**User Story:** As a researcher, I want to see a histogram and percentile table of CPCV out-of-sample path Sharpe ratios, so that I can assess the distribution of strategy performance across combinatorial paths.

#### Acceptance Criteria

1. THE CPCV result page SHALL display a Plotly histogram of all OOS path Sharpe ratios with bars colored red (Sharpe < 0), yellow (0 ≤ Sharpe < 1), and green (Sharpe ≥ 1).
2. THE CPCV result page SHALL display vertical dashed lines at the median and at zero on the histogram.
3. THE CPCV result page SHALL display a percentile table showing P10, P25, P50, P75, and P90 Sharpe values.
4. THE `CpcvResult` record SHALL carry a `PathSharpeRatios` field of type `IReadOnlyList<decimal>` populated by the CPCV_Workflow.

---

### Requirement 12: Parameter Sweep Heatmap Metric Selector

**User Story:** As a researcher, I want to switch the heatmap metric between Sharpe, MaxDD, WinRate, ProfitFactor, and TotalTrades, so that I can evaluate parameter sensitivity across multiple dimensions.

#### Acceptance Criteria

1. THE parameter sweep heatmap SHALL display a metric selector dropdown with options: Sharpe Ratio, Max Drawdown, Win Rate, Profit Factor, and Trade Count.
2. WHEN "Max Drawdown" is selected, THE heatmap SHALL render with an inverted color scale (lower values are green).
3. WHEN the user selects a different metric, THE heatmap SHALL re-render reactively without a page reload.
4. THE `SweepCell` record SHALL carry values for all 5 metrics populated by the Parameter_Sweep_Workflow.

---

### Requirement 13: Fill Delay Perturbation in Sensitivity Analysis

**User Story:** As a researcher, I want sensitivity analysis to include 1-bar entry delay perturbation, so that I can measure how fill timing affects strategy performance.

#### Acceptance Criteria

1. THE Sensitivity_Workflow SHALL include fill-delay variants of 0, 1, and 2 bars as a standard perturbation dimension.
2. WHEN `FillDelayBars` is set to 1, THE Engine SHALL defer order submission by exactly 1 bar before placing orders in the pending-order queue.
3. THE `FillDelayBars` parameter SHALL be configurable in the Advanced Overrides panel of the Strategy_Builder.
4. WHEN a strategy relies on bar-open fills, THE sensitivity results SHALL show measurable performance degradation at 1-bar delay compared to 0-bar delay.

---

### Requirement 14: Dashboard Checklist Score Display

**User Story:** As a researcher, I want to see the research checklist confidence score on each dashboard strategy card, so that I can quickly identify which strategies need further validation.

#### Acceptance Criteria

1. THE Dashboard SHALL display an "X/9 checks" badge on each strategy card using the Checklist_Service evaluation.
2. WHEN a strategy passes 7 or more checks, THE badge SHALL display in green; 5-6 checks in yellow; fewer than 5 in red.
3. WHEN the user hovers over the checklist badge, THE Dashboard SHALL show a tooltip listing the names of failed checks.
4. WHEN a strategy has no completed runs, THE Dashboard SHALL display "—" for the checklist badge.

---

### Requirement 15: AI Strategy Builder Streaming Response

**User Story:** As a user, I want AI-generated strategy text to stream token by token, so that I receive immediate feedback instead of waiting for the full response.

#### Acceptance Criteria

1. WHEN the user submits a strategy generation prompt, THE AI_Strategy_Assistant SHALL stream response tokens via `IAsyncEnumerable<string>`.
2. WHILE streaming is in progress, THE Strategy_Builder SHALL display a "Stop generation" button that cancels the stream via `CancellationToken`.
3. WHEN the stream completes, THE Strategy_Builder SHALL parse the full response as `AIStrategyDraft` JSON and auto-populate the builder form fields.
4. THE non-streaming `GenerateAsync` path SHALL continue to function without regression.

---

### Requirement 16: AI Strategy Iterative Refinement

**User Story:** As a user, I want to refine an AI-generated strategy with follow-up prompts, so that I can iteratively improve the draft without starting from scratch.

#### Acceptance Criteria

1. WHEN an AI draft has been generated, THE Strategy_Builder SHALL display a "Refine with AI feedback" section with a text input and submit button.
2. WHEN the user submits a refinement prompt, THE AI_Strategy_Assistant SHALL receive the current draft as context and stream a refined response.
3. THE Strategy_Builder SHALL maintain a refinement history showing all previous prompts and allowing reversion to any prior draft version.
4. THE `AIStrategyDraft` record SHALL carry a `RefinementHistory` property of type `IReadOnlyList<string>`.

---

### Requirement 17: Result-Aware Dynamic Study Interpretations

**User Story:** As a researcher, I want study interpretations to reflect actual result values and trigger warnings at quantitative thresholds, so that I receive actionable guidance instead of static text.

#### Acceptance Criteria

1. THE Interpretation_Service SHALL generate text that includes specific numeric values from the actual study results.
2. WHEN Monte Carlo ruin probability exceeds 5%, THE Interpretation_Service SHALL include a warning about elevated ruin risk.
3. WHEN CPCV probability of overfitting exceeds 50%, THE Interpretation_Service SHALL include a critical warning about overfitting.
4. WHEN walk-forward OOS Sharpe is less than 50% of IS Sharpe, THE Interpretation_Service SHALL include a warning about performance degradation.
5. THE Interpretation_Service SHALL be unit-testable via dependency injection — interpretation logic SHALL NOT be inline in Razor components.

---

### Requirement 18: Builder Step Persistence

**User Story:** As a user, I want the strategy builder wizard to resume at the correct step after a page refresh, so that I do not lose my progress mid-workflow.

#### Acceptance Criteria

1. WHEN the user navigates between wizard steps, THE Strategy_Builder SHALL auto-save the current step number to the draft (debounced at 500ms).
2. WHEN the Strategy_Builder loads with an existing draft, THE wizard SHALL restore to the saved `CurrentStep`.
3. THE Strategy_Builder SHALL track `MaxVisitedStep` and prevent skipping forward to unvisited steps after resume.

---

### Requirement 19: Robustness Flag Tooltips

**User Story:** As a user, I want plain-English tooltip explanations on robustness warning chips, so that I understand what each warning means without quant expertise.

#### Acceptance Criteria

1. WHEN the user hovers over a robustness warning chip, THE Dashboard SHALL display a tooltip with a plain-English explanation of the warning.
2. THE system SHALL maintain a `RobustnessWarningCatalog` with explanations for all warning types emitted by the robustness advisory service.
3. IF a warning has no catalog entry, THEN THE tooltip SHALL fall back to displaying the raw warning label without throwing an error.

---

### Requirement 20: Recent Runs Table Sorting and Filtering

**User Story:** As a user, I want to sort and filter the recent runs table, so that I can quickly find specific backtest results.

#### Acceptance Criteria

1. THE Dashboard recent runs table SHALL support ascending and descending sorting on Sharpe, Max Drawdown, and Trade Count columns.
2. THE Dashboard SHALL display strategy filter chips that reactively filter displayed runs by strategy type.
3. THE Dashboard SHALL provide a "Show failed runs" toggle that includes or excludes runs with failed status.
4. THE sorting and filtering SHALL operate client-side without additional repository queries.

---

### Requirement 21: Strategy Library Empty State

**User Story:** As a new user, I want a helpful empty state in the strategy library, so that I understand the research lifecycle and know how to create my first strategy.

#### Acceptance Criteria

1. WHEN the strategy library contains zero strategies, THE Strategy_Library_Page SHALL display a structured empty state with a research lifecycle explanation.
2. THE empty state SHALL provide a "Start from Template" button navigating to the builder and a "Use AI Builder" button navigating to the AI builder mode.
3. WHEN strategies exist in the library, THE empty state SHALL NOT be displayed.

---

### Requirement 22: Universal Skender Indicator Bridge

**User Story:** As a researcher, I want access to all 150+ Skender.Stock.Indicators without hand-written wrappers, so that I can use any indicator in strategy construction.

#### Acceptance Criteria

1. THE Skender_Bridge SHALL instantiate and produce correct output values for at least: MACD, ADX, Stochastic, Williams %R, OBV, CCI, Supertrend, and Keltner Channel.
2. THE Skender_Bridge SHALL use pre-compiled delegates for indicator invocation — zero reflection during bar processing.
3. THE Indicator_Catalog SHALL describe 40 or more indicators with parameters, output fields, and category metadata.
4. THE `IndicatorRegistry.All` collection SHALL include descriptors for all catalog indicators.
5. THE Strategy_Builder SHALL provide an `IndicatorPickerPanel` with category filtering, text search, and an "Add to strategy" action.
6. WHEN processing 100,000 bars, THE Skender_Bridge (MACD configuration) SHALL complete in less than 500ms, matching the performance order of the hand-written `MacdIndicator`.
