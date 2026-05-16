# Requirements Document

## Introduction

This specification covers the remaining improvements identified in the v3 Full Code Audit review. The 4 residual bugs have already been fixed. This document addresses 2 structural gaps and 18 improvement opportunities across engine/quant capabilities, product UX, architecture/code quality, and testing.

## Glossary

- **System**: The TradingResearchEngine application as a whole
- **GridOptimizer**: The Application-layer component responsible for parameter sweep execution across a defined parameter grid
- **CompositeStrategy**: A strategy composed of visual condition-builder indicators and entry/exit condition expressions
- **CompositeStrategyConfig**: The typed configuration record for a CompositeStrategy, containing indicators, conditions, and DirectionMode
- **CompositeParameterGrid**: A proposed mapping structure that associates indicator IDs with numeric parameter ranges for sweep/walk-forward
- **MarkdownReporter**: The Infrastructure-layer reporter that exports BacktestResult metrics to Markdown format
- **TradeExcursionTracker**: The Application-layer component that computes Maximum Adverse Excursion (MAE) and Maximum Favorable Excursion (MFE) per trade
- **MetricsCalculator**: The Core-layer component that computes all performance metrics from equity curve and trade data
- **ChartComputationHelpers**: The Application-layer utility class for chart-related data transformations
- **WalkForwardSummary**: The result record from walk-forward analysis containing composite OOS metrics and parameter drift scoring
- **ResearchChecklistService**: The Application-layer service that evaluates a configurable checklist of research quality gates
- **IStreamingDataProvider**: The Core-layer interface for streaming market data to the paper trading session
- **SimulatedPaperTradingSession**: The Application-layer session that replays or streams bars through the full backtest execution pipeline for paper trading
- **PaperTradingOptions**: The configuration record for paper trading session behaviour
- **DataFeedMode**: A proposed enum distinguishing Replay (simulated) from Live (real broker feed) data sources
- **PollingRestStreamingDataProvider**: A proposed Infrastructure-layer implementation of IStreamingDataProvider that polls a REST endpoint on a configurable interval
- **ExportValidator**: The Application-layer validator for PineScript and MQL export correctness
- **CompositeStrategyConfigValidator**: The Application-layer validator for CompositeStrategyConfig structural and semantic correctness
- **ConditionParser**: The Application-layer parser for entry/exit condition expression strings
- **StrategyBuilder**: The multi-step Blazor wizard UI for creating and configuring strategies
- **ResultDetail**: The Blazor page displaying full backtest result metrics, charts, and trade log
- **BacktestList**: The Blazor page listing historical backtest results with filtering
- **SessionSetup**: The Blazor page for configuring and launching paper trading sessions
- **RobustnessAdvisoryService**: The Application-layer service that evaluates metrics and emits robustness warnings
- **DSR**: Deflated Sharpe Ratio — a statistical correction for multiple-testing bias
- **MinBTL**: Minimum Backtest Length — the minimum number of observations required for statistical significance
- **MAE**: Maximum Adverse Excursion — the worst unrealised loss during a trade
- **MFE**: Maximum Favorable Excursion — the best unrealised profit during a trade
- **BarRecord**: The Core-layer record representing a single OHLCV bar
- **MinBtlCalculator**: The existing Application-layer service that computes Minimum Backtest Length using the Bailey–López de Prado formula
- **ConfigDraft**: A persisted snapshot of in-progress StrategyBuilder wizard state, keyed by strategy identity
- **Compare**: The Blazor page for side-by-side comparison of multiple backtest results

## Requirements

### Requirement 1: Composite Strategy Parameter Sweep Support

**User Story:** As a researcher, I want to sweep indicator parameters within a composite strategy, so that I can optimise composite strategies without manual iteration.

#### Acceptance Criteria

1. THE GridOptimizer SHALL accept a CompositeParameterGrid that maps indicator IDs to numeric parameter ranges (start, end, step)
2. WHEN a CompositeParameterGrid is provided, THE GridOptimizer SHALL clone the CompositeStrategyConfig for each parameter combination and inject the overridden parameter value into the matching IndicatorConfig
3. WHEN a CompositeParameterGrid is provided, THE GridOptimizer SHALL execute the parameter sweep using the same parallel execution and concurrency budget as standard parameter sweeps
4. WHEN a CompositeParameterGrid references an indicator ID not present in the CompositeStrategyConfig, THE GridOptimizer SHALL return a validation error identifying the unresolved indicator ID
5. THE WalkForwardWorkflow SHALL support CompositeParameterGrid for walk-forward optimisation of composite strategies using the same IS/OOS window mechanics as standard strategies
6. WHEN a CompositeParameterGrid contains zero valid parameter ranges, THE GridOptimizer SHALL return a validation error indicating no sweep dimensions are defined

### Requirement 2: Live Data Feed Transparency and Long-Term Provider

**User Story:** As a researcher, I want to understand whether paper trading uses real or simulated data, so that I can correctly interpret paper trading results.

#### Acceptance Criteria

1. THE PaperTradingOptions SHALL include a DataFeedMode enum with values Replay and Live
2. WHILE DataFeedMode is set to Replay, THE SessionSetup page SHALL display a visible indicator stating that paper trading uses simulated playback data
3. WHILE DataFeedMode is set to Live and no real feed provider is configured, THE SessionSetup page SHALL display a warning that live mode is unavailable and will fall back to Replay
4. WHEN a PollingRestStreamingDataProvider is configured, THE System SHALL poll the configured REST endpoint at the configured interval and emit bars as they arrive through the IStreamingDataProvider interface
5. THE PollingRestStreamingDataProvider SHALL accept a configurable polling interval appropriate for the target timeframe (daily, hourly)
6. IF the PollingRestStreamingDataProvider receives an error response from the REST endpoint, THEN THE PollingRestStreamingDataProvider SHALL log the error and retry on the next polling interval without terminating the session

### Requirement 3: MarkdownReporter Metrics Completeness

**User Story:** As a researcher, I want exported Markdown reports to include all computed risk metrics, so that exported reports match the metrics visible in the UI.

#### Acceptance Criteria

1. THE MarkdownReporter SHALL include VaR95 in the performance metrics table of the exported Markdown report
2. THE MarkdownReporter SHALL include CVaR95 in the performance metrics table of the exported Markdown report
3. THE MarkdownReporter SHALL include OmegaRatio in the performance metrics table of the exported Markdown report
4. THE MarkdownReporter SHALL include UlcerIndex in the performance metrics table of the exported Markdown report
5. WHEN any of VaR95, CVaR95, OmegaRatio, or UlcerIndex is null on the BacktestResult, THE MarkdownReporter SHALL display "N/A" for that metric rather than omitting the row

### Requirement 4: TradeExcursionTracker OHLC Bar Support

**User Story:** As a researcher, I want MAE/MFE calculations to use intra-bar high/low prices, so that excursion metrics accurately reflect worst-case and best-case price movements within each bar.

#### Acceptance Criteria

1. THE TradeExcursionTracker SHALL accept a full BarRecord (containing Open, High, Low, Close) for price updates instead of a single price value
2. WHILE tracking a long position, THE TradeExcursionTracker SHALL use bar.Low as the adverse price extreme and bar.High as the favorable price extreme
3. WHILE tracking a short position, THE TradeExcursionTracker SHALL use bar.High as the adverse price extreme and bar.Low as the favorable price extreme
4. FOR ALL bar sequences, THE MAE computed using High/Low extremes SHALL be greater than or equal to the MAE computed using Close prices only (property: OHLC MAE is at least as severe as close-only MAE)
5. FOR ALL bar sequences, THE MFE computed using High/Low extremes SHALL be greater than or equal to the MFE computed using Close prices only (property: OHLC MFE is at least as favorable as close-only MFE)

### Requirement 5: Time-Weighted Return for Grid Optimisation

**User Story:** As a researcher, I want the grid optimiser to use time-weighted return as an objective, so that parameter sets from different window lengths are compared fairly without window-length bias.

#### Acceptance Criteria

1. THE GridOptimizer SHALL provide a TimeWeightedReturn optimisation objective that annualises total return based on the window duration
2. WHEN TimeWeightedReturn is selected as the objective, THE GridOptimizer SHALL compute the annualised return as (EndEquity / StartEquity)^(BarsPerYear / windowBars) − 1, where windowBars is the count of equity curve data points (BacktestResult.EquityCurve.Count) — the actual number of bars processed by the engine during the IS window, not inferred from timestamps or provider interval semantics
3. WHEN TimeWeightedReturn is selected and the window duration is available from BacktestResult metadata, THE GridOptimizer SHALL use BacktestResult.EquityCurve.Count as the deterministic windowBars value
4. THE existing TotalReturn objective SHALL remain available for backward compatibility

### Requirement 6: Parameter Drift Score Interpretation

**User Story:** As a researcher, I want to understand what the parameter drift score means and what action to take when it is high, so that I can assess whether walk-forward results are reproducible.

#### Acceptance Criteria

1. THE WalkForward result page SHALL display a tooltip or info panel explaining the parameter drift score meaning and interpretation
2. THE tooltip SHALL state that a high drift score suggests the strategy is highly sensitive to parameter choice and walk-forward gains may not be reproducible
3. THE RobustnessAdvisoryService SHALL evaluate the parameter drift score and emit a robustness warning when the score exceeds a configurable threshold
4. THE configurable threshold for parameter drift warnings SHALL be exposed via IOptions-bound configuration

### Requirement 7: DSR and MinBTL in Research Checklist

**User Story:** As a researcher, I want the research checklist to verify that Deflated Sharpe Ratio meets a minimum threshold, so that I am warned about multiple-testing bias before reaching final validation.

#### Acceptance Criteria

1. THE ResearchChecklistService SHALL include a DSR checklist item that evaluates BacktestResult.DeflatedSharpeRatio
2. WHEN DeflatedSharpeRatio is null, THE DSR checklist item SHALL report as incomplete with a message indicating DSR has not been computed
3. WHEN DeflatedSharpeRatio is below the configured minimum threshold, THE DSR checklist item SHALL report as failed with the actual value and the threshold
4. THE minimum DSR threshold SHALL be configurable via IOptions-bound configuration with a default value of 0.5
5. THE ResearchChecklistService SHALL include a MinBTL checklist item that calls MinBtlCalculator.Compute(BacktestResult) — which uses the Bailey–López de Prado formula: MinBTL = (1 + (1 - skewness * sharpe + ((kurtosis - 1) / 4) * sharpe^2)) * (z_alpha / sharpe)^2 — and compares the result against BacktestResult.EquityCurve.Count to verify the backtest length meets the minimum required for statistical significance

### Requirement 8: Monthly Returns Computation Extraction

**User Story:** As a developer, I want monthly returns computation to live in ChartComputationHelpers rather than inline in the Razor component, so that the logic is testable and reusable.

#### Acceptance Criteria

1. THE ChartComputationHelpers SHALL provide a ComputeMonthlyReturns method that accepts an IReadOnlyList of EquityCurvePoint and returns monthly return data grouped by calendar year and month
2. THE ComputeMonthlyReturns method SHALL compute the percentage return for each calendar month based on the first and last equity values within that month
3. THE MonthlyReturnsHeatmap component SHALL consume the output of ChartComputationHelpers.ComputeMonthlyReturns rather than computing monthly returns inline
4. WHEN the equity curve contains fewer than 2 data points for a given month, THE ComputeMonthlyReturns method SHALL return null (decimal?) for that month indicating insufficient data
5. THE MonthlyReturnsHeatmap component SHALL render null months as a distinct "no data" visual state rather than displaying 0%

### Requirement 9: Research Journal UI Page

**User Story:** As a researcher, I want a dedicated journal page for each strategy, so that I can trace the reasoning behind parameter changes and stage transitions over time.

#### Acceptance Criteria

1. THE System SHALL provide a Research Journal page accessible at /strategies/{id}/journal
2. THE Research Journal page SHALL display ResearchJournalEntry records in a timeline view grouped by action type
3. THE Research Journal page SHALL provide an "Add Note" dialog for creating free-text journal entries
4. WHEN a strategy's DevelopmentStage changes, THE System SHALL automatically create a stage-transition journal entry
5. THE Research Journal page SHALL support filtering entries by action type and date range

### Requirement 10: Compare Page Deep Linking

**User Story:** As a researcher, I want to bookmark and share comparison URLs, so that I can reproduce specific result comparisons without re-selecting results manually.

#### Acceptance Criteria

1. THE Compare page SHALL encode selected result IDs as query parameters in the URL (format: /compare?ids=id1,id2,id3)
2. WHEN the Compare page loads with ids query parameters, THE Compare page SHALL pre-populate the comparison with the specified results
3. WHEN a result ID in the query parameters does not exist, THE Compare page SHALL display a warning identifying the missing result and load the remaining valid results
4. WHEN the user adds or removes results from the comparison, THE Compare page SHALL update the URL query parameters to reflect the current selection

### Requirement 11: Sensitivity Hint Display in Sweep UI

**User Story:** As a researcher, I want to see overfitting sensitivity indicators when configuring parameter sweeps, so that I am warned before sweeping high-sensitivity parameters with tight steps.

#### Acceptance Criteria

1. THE ParameterGroupEditor SHALL display a visual indicator (coloured chip: green for Low, amber for Medium, red for High) next to each parameter sourced from StrategyParameterSchema.SensitivityHint
2. WHEN the total sweep combination count exceeds a configurable threshold and any dimension has High sensitivity, THE sweep UI SHALL display an overfitting warning
3. THE overfitting warning SHALL explain that sweeping high-sensitivity parameters increases false discovery risk
4. THE combination count threshold for the overfitting warning SHALL be configurable via IOptions-bound configuration

### Requirement 12: Tags and Notes on Result Detail

**User Story:** As a researcher, I want to view, add, and edit tags and notes on backtest results, so that I can annotate results for future reference and filter history by label.

#### Acceptance Criteria

1. THE ResultDetail page SHALL display a "Notes & Tags" panel showing the current BacktestResult.Tags and BacktestResult.Notes
2. THE ResultDetail page SHALL allow editing Notes via an inline text editor and persisting changes via IRepository SaveAsync
3. THE ResultDetail page SHALL allow adding and removing Tags and persisting changes via IRepository SaveAsync
4. THE BacktestList page SHALL support filtering results by tag using selectable filter chips
5. WHEN a result has no tags or notes, THE ResultDetail page SHALL display an empty state with an "Add" action

### Requirement 13: Keyboard Shortcut for Re-Run

**User Story:** As a researcher, I want a keyboard shortcut to re-run a scenario from the result detail page, so that I can iterate faster without navigating back to the builder.

#### Acceptance Criteria

1. WHEN the user presses the configured re-run shortcut key on the ResultDetail page, THE System SHALL navigate immediately to the StrategyBuilder pre-populated with the same ScenarioConfig as the viewed result without confirmation (the ResultDetail page has no editable state that could be lost)
2. THE keyboard shortcut SHALL be registered in the KeyboardShortcutOverlay and visible in the shortcut help panel
3. THE re-run shortcut SHALL use the key "R" by default
4. WHILE the user is on the Compare page, THE "R" shortcut SHALL be inactive (the shortcut is context-specific to ResultDetail only)

### Requirement 14: Strategy Builder Draft Auto-Save

**User Story:** As a researcher, I want the strategy builder wizard to auto-save my draft, so that I do not lose work if I close the browser mid-wizard.

#### Acceptance Criteria

1. WHEN a parameter value changes in the StrategyBuilder wizard, THE System SHALL auto-save the ConfigDraft after a debounce period of 3 seconds since the last change
2. THE StrategyBuilder header SHALL display a "Draft saved" timestamp indicating the last successful auto-save
3. WHEN the StrategyBuilder loads and a persisted ConfigDraft exists for the current strategy, THE System SHALL restore the draft and resume from the last completed step
4. IF the auto-save operation fails, THEN THE System SHALL display a non-blocking warning indicating the draft was not saved
5. THE draft identity key SHALL be the tuple (StrategyId, StrategyVersionId) when editing an existing strategy version, or a transient session GUID when creating a new strategy, ensuring restored drafts are deterministic and do not collide across strategies

### Requirement 15: Obsolete Attribute Escalation for DataProviderOptions

**User Story:** As a developer, I want the Obsolete attribute on ScenarioConfig.DataProviderOptions to be escalated to error: true, so that any remaining callers are caught at compile time after the WalkForward fix.

#### Acceptance Criteria

1. WHEN the WalkForwardWorkflow.WithDateRange migration to typed DataProviderConfig is complete, THE ScenarioConfig.DataProviderOptions Obsolete attribute SHALL be changed to error: true
2. THE System SHALL compile without errors after the attribute is escalated (all callers must be migrated first)

### Requirement 16: Composite Strategy Condition Length Guard

**User Story:** As a developer, I want entry/exit condition strings to be validated for maximum length and nesting depth, so that pathologically long or deeply nested expressions produce a validation error rather than a stack overflow.

#### Acceptance Criteria

1. THE CompositeStrategyConfigValidator SHALL reject entry or exit condition strings exceeding a configurable maximum character length
2. THE CompositeStrategyConfigValidator SHALL reject condition expressions exceeding a configurable maximum operator nesting depth
3. THE maximum character length SHALL default to 2000 characters and be configurable via a named constant
4. THE maximum nesting depth SHALL default to 50 levels and be configurable via a named constant
5. WHEN a condition exceeds either limit, THE CompositeStrategyConfigValidator SHALL return a validation error identifying which limit was exceeded and the actual value

### Requirement 17: Paper Trading Session Error Resilience

**User Story:** As a developer, I want Subject emissions in SimulatedPaperTradingSession to be resilient to subscriber exceptions, so that a failing subscriber does not silently terminate the event stream.

#### Acceptance Criteria

1. WHEN a subscriber's OnNext handler throws an exception during PaperBarEvent emission, THE SimulatedPaperTradingSession SHALL catch the exception, log it, and continue emitting subsequent events
2. WHEN a subscriber's OnNext handler throws an exception during PaperTradeEvent emission, THE SimulatedPaperTradingSession SHALL catch the exception, log it, and continue emitting subsequent events
3. THE SimulatedPaperTradingSession SHALL remain in Running state after a subscriber exception (the session state machine is not affected)
4. THE logged exception SHALL include the subscriber exception message and stack trace at Error log level

### Requirement 18: Source-Generated Regex in ExportValidator

**User Story:** As a developer, I want ExportValidator to use source-generated regex, so that regex patterns are compiled at build time with zero-allocation matching following modern .NET 8 patterns.

#### Acceptance Criteria

1. THE ExportValidator SHALL use [GeneratedRegex] attribute on static partial methods for all regex patterns used in ValidatePineScript and ValidateMql
2. THE ExportValidator SHALL not contain any static readonly Regex field declarations or inline Regex constructor calls
3. THE source-generated regex patterns SHALL produce identical match results to the existing compiled patterns (behavioral equivalence)

### Requirement 19: Property-Based Test for TradeExcursionTracker Direction Correctness

**User Story:** As a developer, I want a property-based test verifying MAE/MFE direction correctness, so that direction inversion bugs for short positions are caught automatically.

#### Acceptance Criteria

1. THE property test SHALL verify the direction symmetry property in normalized excursion terms: MAE_short(prices) / entryPrice == MFE_long(prices) / entryPrice for the same price sequence and entry price, where MAE and MFE are both expressed as non-negative values
2. THE property test SHALL verify that MAE is always non-negative (adverse excursion is always a loss)
3. THE property test SHALL verify that MFE is always non-negative (favorable excursion is always a gain)
4. THE property test SHALL use a minimum of 100 iterations per property as per testing standards
5. THE property test class SHALL be named TradeExcursionTrackerProperties and follow the naming convention in testing standards

### Requirement 20: Integration Test for Paper Trading Replay-to-Completion

**User Story:** As a developer, I want an integration test verifying that paper trading replay produces metrics equivalent to a standard backtest over the same data, so that the metric equivalence invariant is automatically verified.

#### Acceptance Criteria

1. THE integration test SHALL start a SimulatedPaperTradingSession with sample CSV data and run it to completion
2. THE integration test SHALL run a standard backtest over the same data with the same strategy configuration
3. THE integration test SHALL verify that the PaperTradingResult metrics match the BacktestResult metrics within acceptable floating-point tolerance
4. THE integration test SHALL use the existing sample CSV fixture data from the IntegrationTests fixtures directory
5. THE integration test class SHALL be named SimulatedPaperTradingSessionTests and follow the naming convention in testing standards


### Requirement 21: Composite Sweep Persistence Backward Compatibility

**User Story:** As a developer, I want CompositeParameterGrid persistence to be forward-compatible, so that older application versions can load persisted WalkForwardOptions without error.

#### Acceptance Criteria

1. WHEN a CompositeParameterGrid is persisted as part of WalkForwardOptions or SweepOptions, THE System SHALL use a JSON shape that is forward-compatible (older versions that do not recognise the field SHALL ignore it gracefully via default deserialisation behaviour)
2. WHEN loading a persisted WalkForwardOptions that does not contain a CompositeParameterGrid field, THE System SHALL treat it as null (no composite sweep) without error
3. THE CompositeParameterGrid field SHALL be serialised as an optional property with a default value of null in the JSON schema

### Requirement 22: Live Polling Provider Observability

**User Story:** As a researcher, I want to monitor the health of the live polling data provider, so that I can detect feed issues before they affect paper trading results.

#### Acceptance Criteria

1. THE PollingRestStreamingDataProvider SHALL expose observable metrics: last successful poll timestamp, consecutive failure count, and current DataFeedMode
2. THE SessionSetup page SHALL display the active feed mode and last successful poll time when a live session is running
3. WHEN consecutive failures exceed a configurable threshold, THE System SHALL emit a structured log warning at Warning level including the failure count and the configured threshold
4. THE consecutive failure threshold SHALL be configurable via IOptions-bound configuration

### Requirement 23: Composite Sweep Execution Guardrail

**User Story:** As a researcher, I want the system to reject excessively large parameter sweeps before execution begins, so that I do not accidentally launch a sweep that would take hours or exhaust system resources.

#### Acceptance Criteria

1. WHEN the total parameter combinations from a CompositeParameterGrid exceed a configurable maximum (default: 10000), THE GridOptimizer SHALL return a validation error before execution begins
2. THE validation error SHALL state the computed combination count and the configured maximum
3. THE configurable maximum combination count SHALL be exposed via IOptions-bound configuration with a named default constant
4. This guardrail complements Requirement 11 (UX warning) with a hard execution limit at the Application layer
