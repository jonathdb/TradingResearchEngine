# Requirements Document

## Introduction

This document captures the requirements for a large batch of improvements to the TradingResearchEngine — a .NET 8 / Blazor Server / MudBlazor event-driven backtesting engine for quantitative strategy research. The batch spans critical bug fixes (P0), quant correctness (P0/P1), architecture improvements (P1), UI/UX enhancements (P1), research/robustness features (P2), and strategy creation tooling (P2). Requirements follow EARS patterns and INCOSE quality rules.

## Glossary

- **Engine**: The `BacktestEngine` component in Core that processes market data events through the heartbeat loop and dispatches them to strategy, risk, and execution layers.
- **Strategy_Builder**: The Blazor Server page (`StrategyBuilder.razor`) providing the 5-step wizard for creating and configuring trading strategies.
- **Job_Executor**: The Application-layer singleton service responsible for enqueueing, executing, and tracking backtest jobs on background tasks.
- **Job_Status_Page**: The Blazor page (`JobStatus.razor`) that polls job status and auto-redirects on completion.
- **Strategy_Factory**: The `IStrategyFactory` interface in Core that creates isolated `IStrategy` instances for parallel workflows.
- **Fill_Engine**: The execution engine components (`TryFillLimit`, `TryFillStopMarket`, `TryFillStopLimit`, `ProcessPendingOrders`) responsible for simulating order fills against bar data.
- **Metrics_Calculator**: The `MetricsCalculator` static class in Core that computes performance metrics (Sharpe, Sortino, VaR, CVaR) from equity curves and return series.
- **Walk_Forward_Workflow**: The Application-layer research workflow that runs in-sample optimization followed by out-of-sample validation across rolling windows.
- **Parameter_Sweep_Workflow**: The Application-layer research workflow that evaluates strategy performance across a grid of parameter combinations.
- **Study_Detail_Page**: The Blazor page (`StudyDetail.razor`) that displays results for completed research studies.
- **Dashboard**: The Blazor page (`Dashboard.razor`) showing recent runs, strategy cards, and robustness warnings.
- **Strategy_Library_Page**: The Blazor page (`StrategyLibrary.razor`) displaying all saved strategies.
- **Background_Study_Service**: The Application-layer service that executes long-running research studies and emits progress events.
- **CPCV_Workflow**: The Combinatorial Purged Cross-Validation workflow producing out-of-sample path Sharpe distributions.
- **Sensitivity_Workflow**: The research workflow that perturbs execution parameters (slippage, commission, fill delay) to measure strategy robustness.
- **Checklist_Service**: The `ResearchChecklistService` that evaluates a 9-item confidence checklist against backtest results.
- **AI_Strategy_Assistant**: The `IAIStrategyAssistant` interface providing AI-powered strategy generation and refinement via Google Gemini.
- **Interpretation_Service**: The `StudyInterpretationService` that generates result-aware textual interpretations of study outcomes.
- **Skender_Bridge**: The `SkenderBridgeIndicator` generic adapter that wraps any Skender.Stock.Indicators indicator without hand-written subclasses.
- **Indicator_Catalog**: The `SkenderIndicatorCatalog` metadata registry describing all available Skender indicators with parameters and output fields.
- **Renderer_Registry**: The `StudyRendererRegistry` mapping `StudyType` values to Blazor renderer components.
- **SignalR_Circuit**: The Blazor Server SignalR connection that handles UI interactions; blocking this thread freezes the entire UI.
- **RunState**: The mutable internal state object within `BacktestEngine` that tracks pending orders, position, and equity during a single `RunAsync` call.
- **ProgressUpdate**: The record type in Core representing backtest progress (bars processed, total bars).

## Requirements

### Requirement 1: Async Backtest Job Dispatch

**User Story:** As a researcher, I want backtest execution to run off the Blazor SignalR thread, so that the UI remains responsive during long-running backtests.

#### Acceptance Criteria

1. WHEN the user clicks "Launch Backtest" in the Strategy_Builder, THE Job_Executor SHALL enqueue the backtest as a background job and return a job identifier immediately without blocking the SignalR_Circuit.
2. WHEN a job is enqueued, THE Strategy_Builder SHALL navigate to the Job_Status_Page for that job identifier.
3. WHILE a backtest job is running, THE Job_Status_Page SHALL poll job status every 2 seconds using a `PeriodicTimer` and display an indeterminate progress bar.
4. WHEN a backtest job completes successfully, THE Job_Status_Page SHALL automatically redirect the user to the backtest result page after a 1-second delay.
5. IF a backtest job fails, THEN THE Job_Status_Page SHALL display the error message in a severity-error alert with an "Edit & Retry" button linking back to the Strategy_Builder.
6. THE Job_Executor SHALL be registered as a singleton in `ServiceCollectionExtensions.cs`.
7. THE Job_Status_Page SHALL dispose the `PeriodicTimer` and `CancellationTokenSource` in `IAsyncDisposable.DisposeAsync`.
8. THE Strategy_Builder SHALL contain zero inline `await RunUseCase.RunAsync(...)` calls after this change.

---

### Requirement 2: Strategy Factory for Parallel Isolation

**User Story:** As a researcher, I want parallel workflows to use isolated strategy instances, so that walk-forward and parameter sweep results are free from shared-state data races.

#### Acceptance Criteria

1. THE Strategy_Factory SHALL be defined in Core as `IStrategyFactory` with a `Create(StrategyConfig config)` method returning a new `IStrategy` instance.
2. THE Engine constructor SHALL accept `IStrategyFactory` instead of `IStrategy` directly.
3. WHEN `Create(config)` is called, THE Strategy_Factory SHALL return a new independent `IStrategy` instance with its own mutable indicator state.
4. THE Walk_Forward_Workflow SHALL call `factory.Create(config)` for each parallel iteration instead of reusing a single strategy instance.
5. THE Parameter_Sweep_Workflow SHALL call `factory.Create(config)` for each parallel iteration instead of reusing a single strategy instance.
6. WHEN 20 concurrent instances are created from the same factory and executed in parallel, THE Strategy_Factory SHALL produce independent, deterministic results with no shared mutable state.

---

### Requirement 3: Stop-Limit Triggered State Persistence

**User Story:** As a researcher, I want stop-limit orders that trigger but do not fill on bar N to retain their triggered state on subsequent bars, so that the limit portion fills correctly without re-evaluating the stop condition.

#### Acceptance Criteria

1. WHEN a stop-limit order triggers on bar N but the limit price is not reached, THE Fill_Engine SHALL return an `ExecutionResult` with `Outcome = Unfilled` and a non-null `TriggeredOrder` carrying `StopTriggered = true`.
2. WHEN `ProcessPendingOrders` re-queues an unfilled order, THE Fill_Engine SHALL use the `TriggeredOrder` from the `ExecutionResult` if present, preserving the triggered state.
3. WHEN a previously-triggered stop-limit order is evaluated on bar N+1, THE Fill_Engine SHALL skip the stop-price check and evaluate only the limit-price condition.
4. WHEN the limit price is reached on bar N+1 for a triggered order, THE Fill_Engine SHALL fill the order at the limit price.

---

### Requirement 4: Synthetic Bar Timeframe Fix

**User Story:** As a researcher, I want `CreateFillAtPrice` to use the actual timeframe from the current market event, so that timeframe-aware commission and slippage models produce correct results on intraday strategies.

#### Acceptance Criteria

1. WHEN `CreateFillAtPrice` constructs a synthetic `BarEvent`, THE Fill_Engine SHALL use the timeframe from the most recent `BarEvent` processed by the engine.
2. IF no prior `BarEvent` exists (tick-only data), THEN THE Fill_Engine SHALL fall back to `"1D"` as the synthetic bar timeframe.
3. WHEN a strategy runs on M15 data, THE synthetic bar timeframe SHALL be `"M15"` for all fills generated during that run.

---

### Requirement 5: PendingOrders Allocation Optimization

**User Story:** As a researcher, I want `ProcessPendingOrders` to avoid allocating a new list on every bar, so that M1 backtests over 2.6 million bars do not suffer excessive GC pressure.

#### Acceptance Criteria

1. THE RunState SHALL maintain a pre-allocated swap buffer for pending order processing.
2. WHEN `ProcessPendingOrders` executes, THE Fill_Engine SHALL populate the swap buffer as the "remaining" list and swap references at the end — zero `new List<OrderEvent>()` allocations per bar.
3. WHEN processing 2,600,000 bars with pending orders on every bar, THE Fill_Engine SHALL allocate at most 2 `List<OrderEvent>` instances total (the primary list and the swap buffer).

---

### Requirement 6: Historical VaR Small-Sample Guard

**User Story:** As a researcher, I want VaR and CVaR to return null for small samples, so that misleading risk metrics are not displayed for insufficient data.

#### Acceptance Criteria

1. WHEN the returns list contains fewer than 30 elements, THE Metrics_Calculator SHALL return null for `ComputeHistoricalVaR`.
2. WHEN the returns list contains fewer than 30 elements, THE Metrics_Calculator SHALL return null for `ComputeHistoricalCVaR`.
3. WHEN the returns list contains 30 or more elements, THE Metrics_Calculator SHALL compute and return the correct VaR value at the specified confidence level.
4. THE minimum sample threshold SHALL be defined as a named constant `MinSampleForPercentile = 30`.

---

### Requirement 7: IProgress Surface on IBacktestEngine

**User Story:** As a developer, I want `IBacktestEngine.RunAsync` to accept an `IProgress<ProgressUpdate>` parameter, so that callers can subscribe to live progress updates during execution.

#### Acceptance Criteria

1. THE `IBacktestEngine.RunAsync` method signature SHALL include an optional `IProgress<ProgressUpdate>? progress = null` parameter.
2. WHILE the engine processes bars, THE Engine SHALL emit progress reports at intervals of approximately `totalBars / 100` bars (yielding ~100 updates per run).
3. WHEN `progress` is null, THE Engine SHALL skip progress reporting with zero overhead.
4. THE `ProgressUpdate` record SHALL carry `BarsProcessed` and `TotalBars` fields at minimum.

---

### Requirement 8: IStrategy Lifecycle Hooks

**User Story:** As a researcher, I want strategies to support `Initialize` and `Reset` lifecycle methods, so that walk-forward workflows can reuse instances between windows without full reconstruction.

#### Acceptance Criteria

1. THE `IStrategy` interface SHALL define an `Initialize(ScenarioConfig config)` method.
2. THE `IStrategy` interface SHALL define a `Reset()` method that clears all indicator state and internal tracking to initial values.
3. WHEN `Reset()` is called on a strategy after processing bars, and the strategy is then run again, THE strategy SHALL produce results identical to a freshly constructed instance given the same input data.
4. THE Engine SHALL call `strategy.Initialize(config)` before the event loop starts in `RunAsync`.
5. THE Walk_Forward_Workflow SHALL call `strategy.Reset()` before each out-of-sample window instead of constructing a new engine instance.
6. THE `Initialize` and `Reset` methods SHALL have default empty implementations on `IStrategy` to avoid breaking existing strategy implementations.

---

### Requirement 9: Consolidate Strategy Namespaces

**User Story:** As a developer, I want a single canonical `Strategies` namespace in the Application layer, so that there is no ambiguity between `Application/Strategy/` and `Application/Strategies/`.

#### Acceptance Criteria

1. THE Application layer SHALL contain a single `Strategies` folder — the `Strategy` folder SHALL NOT exist after consolidation.
2. WHEN all files are moved, THE solution SHALL build with zero errors and zero namespace conflicts.
3. THE `StrategyNotFoundException` and all other types previously in `Application/Strategy/` SHALL reside in `Application/Strategies/` with updated namespace declarations.

---

### Requirement 10: Inject LoggerFactory into BacktestEngine

**User Story:** As a developer, I want `BacktestEngine` to accept an `ILoggerFactory` via its constructor, so that `Portfolio` and `DataHandler` produce observable log output instead of swallowing logs via `NullLoggerFactory`.

#### Acceptance Criteria

1. THE Engine constructor SHALL accept an `ILoggerFactory` parameter.
2. THE Engine SHALL pass the injected `ILoggerFactory` to `Portfolio` and `DataHandler` during construction inside `RunAsync`.
3. THE DI registration in `ServiceCollectionExtensions.cs` SHALL inject `ILoggerFactory` into the Engine.
4. WHEN `ILoggerFactory` is provided, THE Portfolio and DataHandler SHALL emit log messages for named events (`MarginBreachWarning`, `RiskRejection`).

---

### Requirement 11: Live Study Progress Display

**User Story:** As a researcher, I want to see live progress bars during long-running studies, so that I have feedback on execution status without polling the database.

#### Acceptance Criteria

1. WHILE a study is running, THE Study_Detail_Page SHALL display a progress bar showing completed iterations out of total.
2. THE Background_Study_Service SHALL expose a progress event mechanism that emits `(Guid studyId, ProgressUpdate update)` tuples.
3. WHEN the Study_Detail_Page subscribes to progress events, THE page SHALL call `StateHasChanged()` on each matching update to refresh the progress bar.
4. WHEN a study completes, THE Study_Detail_Page SHALL hide the progress bar and render the final results.
5. WHEN the Study_Detail_Page is disposed, THE page SHALL unsubscribe from all Background_Study_Service event handlers to prevent memory leaks.

---

### Requirement 12: Builder Step Persistence on Refresh

**User Story:** As a user, I want the strategy builder wizard to resume at the correct step after a page refresh, so that I do not lose my progress mid-workflow.

#### Acceptance Criteria

1. WHEN the user navigates between wizard steps, THE Strategy_Builder SHALL auto-save the current step number to the `ConfigDraft` (debounced at 500ms).
2. WHEN the Strategy_Builder loads with an existing draft, THE wizard SHALL restore to the saved `CurrentStep`.
3. THE Strategy_Builder SHALL track `MaxVisitedStep` and prevent the user from skipping forward to unvisited steps after resume.
4. THE step persistence SHALL use the existing draft repository — no `localStorage` or `sessionStorage`.

---

### Requirement 13: Robustness Flag Tooltips

**User Story:** As a user, I want plain-English tooltip explanations on robustness warning chips, so that I understand what each warning means without quant expertise.

#### Acceptance Criteria

1. WHEN the user hovers over a robustness warning chip, THE Dashboard SHALL display a tooltip with a plain-English explanation of the warning.
2. THE system SHALL maintain a `RobustnessWarningCatalog` in the Application layer with explanations for all warning types.
3. IF a warning key has no catalog entry, THEN THE tooltip SHALL fall back to displaying the raw warning label without throwing an error.
4. THE catalog SHALL include explanations for at minimum: "High Sharpe", "Low Trades", and "K-Ratio < 0".

---

### Requirement 14: Dashboard Checklist Score Badge

**User Story:** As a researcher, I want to see the research checklist confidence score on each dashboard strategy card, so that I can quickly identify which strategies need further validation.

#### Acceptance Criteria

1. THE Dashboard SHALL display an "X/9 checks" badge on each strategy card using the Checklist_Service evaluation.
2. WHEN a strategy passes 7 or more checks, THE badge SHALL display in green; 5–6 checks in yellow; fewer than 5 in red.
3. WHEN the user hovers over the checklist badge, THE Dashboard SHALL show a tooltip listing the names of failed checks.
4. WHEN a strategy has no completed runs, THE Dashboard SHALL display "—" for the checklist badge.

---

### Requirement 15: Dashboard Recent Runs Sorting and Filtering

**User Story:** As a user, I want to sort and filter the recent runs table, so that I can quickly find specific backtest results.

#### Acceptance Criteria

1. THE Dashboard recent runs table SHALL support ascending and descending sorting on Sharpe, Max Drawdown, and Trade Count columns via `MudTable` `SortLabel` attributes.
2. THE Dashboard SHALL display strategy filter chips that reactively filter displayed runs by strategy type.
3. THE Dashboard SHALL provide a "Show failed runs" toggle that includes or excludes runs with failed status.
4. THE sorting and filtering SHALL operate on the in-memory result set without additional repository queries.

---

### Requirement 16: Strategy Library Empty State

**User Story:** As a new user, I want a helpful empty state in the strategy library, so that I understand the research lifecycle and know how to create my first strategy.

#### Acceptance Criteria

1. WHEN the strategy library contains zero strategies, THE Strategy_Library_Page SHALL display a structured empty state with a research lifecycle explanation.
2. THE empty state SHALL provide a "Start from Template" button navigating to `/builder` and a "Use AI Builder" button navigating to `/builder?mode=ai`.
3. WHEN strategies exist in the library, THE empty state SHALL NOT be displayed.

---

### Requirement 17: CPCV Distribution Visualization

**User Story:** As a researcher, I want to see a histogram and percentile table of CPCV out-of-sample path Sharpe ratios, so that I can assess the distribution of strategy performance across combinatorial paths.

#### Acceptance Criteria

1. THE CPCV result renderer SHALL display a Plotly histogram of all OOS path Sharpe ratios with bars colored red (Sharpe < 0), yellow (0 ≤ Sharpe < 1), and green (Sharpe ≥ 1).
2. THE histogram SHALL display vertical dashed lines at zero and at the median Sharpe value.
3. THE CPCV result renderer SHALL display a percentile table showing P10, P25, P50, P75, and P90 Sharpe values.
4. THE `CpcvResult` record SHALL carry a `PathSharpeRatios` field of type `IReadOnlyList<decimal>` populated by the CPCV_Workflow.

---

### Requirement 18: Parameter Sweep Heatmap Metric Selector

**User Story:** As a researcher, I want to switch the heatmap metric between Sharpe, MaxDD, WinRate, ProfitFactor, and TotalTrades, so that I can evaluate parameter sensitivity across multiple dimensions.

#### Acceptance Criteria

1. THE parameter sweep heatmap SHALL display a metric selector dropdown with options: Sharpe Ratio, Max Drawdown, Win Rate, Profit Factor, and Trade Count.
2. WHEN "Max Drawdown" is selected, THE heatmap SHALL render with an inverted color scale (lower absolute values are green).
3. WHEN the user selects a different metric, THE heatmap SHALL re-render reactively without a page reload.
4. THE `SweepCell` record SHALL carry values for all 5 metrics populated by the Parameter_Sweep_Workflow.

---

### Requirement 19: Fill Delay Perturbation in Sensitivity Analysis

**User Story:** As a researcher, I want sensitivity analysis to include fill-delay perturbation, so that I can measure how fill timing affects strategy performance.

#### Acceptance Criteria

1. THE Sensitivity_Workflow SHALL include `FillDelayBars` as a standard perturbation dimension with values `[0, 1, 2]`.
2. WHEN `FillDelayBars` is set to N, THE Engine SHALL defer order placement into the pending-order queue by N bars.
3. THE Strategy_Builder Advanced Overrides panel SHALL expose a `FillDelayBars` numeric input (range 0–5, default 0).
4. THE sensitivity result renderer SHALL include fill-delay results alongside slippage and commission perturbations.

---

### Requirement 20: Pluggable Study Result Renderer Registry

**User Story:** As a developer, I want study result rendering to use a pluggable registry pattern, so that adding new study types requires no modification to `StudyDetail.razor`.

#### Acceptance Criteria

1. THE Study_Detail_Page SHALL contain no `switch` statement on `StudyType` for result rendering after this change.
2. THE Renderer_Registry SHALL map each `StudyType` to a dedicated Blazor renderer component type.
3. WHEN a new study type is added, THE system SHALL require only a new renderer component and a registry entry — no changes to the Study_Detail_Page.
4. THE rendered output for all existing study types SHALL be visually identical to the pre-refactor display.
5. THE Renderer_Registry SHALL be registered in `ServiceCollectionExtensions.cs`.

---

### Requirement 21: AI Strategy Streaming and Refinement

**User Story:** As a user, I want AI-generated strategy text to stream token by token and support iterative refinement, so that I receive immediate feedback and can improve drafts without starting over.

#### Acceptance Criteria

1. WHEN the user submits a strategy generation prompt, THE AI_Strategy_Assistant SHALL stream response tokens via `IAsyncEnumerable<string>` through a `GenerateStreamAsync` method.
2. WHILE streaming is in progress, THE Strategy_Builder SHALL display a "Stop generation" button that cancels the stream via `CancellationToken`.
3. WHEN the stream completes, THE Strategy_Builder SHALL parse the full response as `AIStrategyDraft` and auto-populate the builder form fields.
4. WHEN an AI draft exists, THE Strategy_Builder SHALL display a "Refine with AI feedback" text input and submit button.
5. WHEN the user submits a refinement prompt, THE AI_Strategy_Assistant SHALL call `RefineStreamAsync(currentDraft, feedback)` and stream the refined response.
6. THE `AIStrategyDraft` record SHALL carry a `RefinementHistory` property of type `IReadOnlyList<string>`.
7. THE Strategy_Builder SHALL display refinement history in a collapsible panel with "Revert to this version" buttons.

---

### Requirement 22: Result-Aware Dynamic Study Interpretations

**User Story:** As a researcher, I want study interpretations to reflect actual result values and trigger warnings at quantitative thresholds, so that I receive actionable guidance instead of static text.

#### Acceptance Criteria

1. THE Interpretation_Service SHALL generate text that includes specific numeric values from the actual study results.
2. WHEN Monte Carlo ruin probability exceeds 5%, THE Interpretation_Service SHALL include a warning about elevated ruin risk with the actual probability value.
3. WHEN CPCV probability of overfitting exceeds 50%, THE Interpretation_Service SHALL include a critical warning about overfitting with the actual probability value.
4. WHEN walk-forward OOS Sharpe is less than 50% of IS Sharpe, THE Interpretation_Service SHALL include a warning about performance degradation with both Sharpe values.
5. THE Interpretation_Service SHALL be registered as a scoped service in `ServiceCollectionExtensions.cs` and unit-testable via dependency injection.

---

### Requirement 23: Universal Skender Indicator Bridge

**User Story:** As a researcher, I want access to all 150+ Skender.Stock.Indicators without hand-written wrappers, so that I can use any indicator in strategy construction.

#### Acceptance Criteria

1. THE Skender_Bridge SHALL produce correct output values for at minimum: MACD, ADX, Stochastic, Williams %R, OBV, CCI, Supertrend, and Keltner Channel.
2. THE Skender_Bridge SHALL use pre-compiled `Expression<Func<...>>` delegates for indicator invocation — zero reflection during per-bar processing.
3. THE Indicator_Catalog SHALL describe 40 or more indicators with name, category, description, parameters (with defaults, min, max), and output field names.
4. THE `IndicatorRegistry.All` collection SHALL include descriptors for all catalog indicators.
5. THE Strategy_Builder SHALL provide an `IndicatorPickerPanel` component with category filter chips, text search, and an "Add to strategy" action.
6. WHEN processing 100,000 bars through the Skender_Bridge MACD configuration, THE bridge SHALL complete in less than 500ms.
