# Requirements Document

## Introduction

This specification covers 23 items identified in the second-pass code review of TradingResearchEngine (.NET 8, C# 12, Blazor Server, MudBlazor). Items are organised by priority: P0 critical bugs that cause incorrect results or data corruption, P1 architecture and correctness improvements, P2 research features, and P3 polish and UX enhancements. All changes preserve the `Core ← Application ← Infrastructure ← { Cli, Api, Web }` dependency rule and follow existing conventions for XML doc comments, deterministic stochastic workflows, and immutable record types.

## Glossary

- **RunScenarioUseCase**: The Application-layer use case in `Engine/RunScenarioUseCase.cs` that orchestrates a single backtest run including strategy construction, engine execution, and result enrichment.
- **IStrategyRepository**: The Application-layer interface in `Strategies/IStrategyRepository.cs` for persisting and querying strategy identities and versions.
- **StrategyVersion**: An immutable record representing a specific version of a strategy, including `TotalTrialsRun` and `StrategyVersionId`.
- **RandomizedOosWorkflow**: The Application-layer research workflow in `Research/RandomizedOosWorkflow.cs` that performs randomised out-of-sample testing across multiple iterations.
- **RandomizedOosResult**: The immutable result record returned by `RandomizedOosWorkflow` containing iteration-level IS/OOS Sharpe data.
- **RandomizedOosOptions**: The options record controlling iteration count, OOS fraction, and warmup configuration for `RandomizedOosWorkflow`.
- **ParameterSweepWorkflow**: The Application-layer research workflow in `Research/ParameterSweepWorkflow.cs` that evaluates a grid of parameter combinations.
- **SweepResult**: The immutable result record returned by `ParameterSweepWorkflow` containing ranked parameter combinations.
- **BacktestEngine**: The Core-layer engine in `Engine/BacktestEngine.cs` that replays market data through the event-driven loop.
- **IBacktestEngine**: The Core-layer interface for the backtest engine, enabling DI and mocking.
- **IBacktestEngineFactory**: A Core-layer factory interface for creating `IBacktestEngine` instances with per-run dependencies.
- **SimulatedExecutionHandler**: The Application-layer execution handler that simulates order fills with slippage and commission models.
- **IRiskLayer**: The Core-layer interface for risk management that converts signals to orders.
- **IExecutionHandler**: The Core-layer interface for order execution.
- **PreflightValidator**: The Application-layer validator in `Engine/PreflightValidator.cs` that checks `ScenarioConfig` for issues before an engine run.
- **SealedTestSetGuard**: The Application-layer guard in `Engine/SealedTestSetGuard.cs` that prevents access to sealed test data until explicitly unlocked.
- **ITestSetAuditLog**: An Application-layer interface for recording test-set unlock and re-seal events.
- **ScenarioConfig**: The Core-layer immutable record containing all configuration for a single backtest run.
- **DeepClone**: An extension method on `ScenarioConfig` that produces an independent copy of all mutable reference-type properties.
- **SweepSortMetric**: An Application-layer enum specifying the metric used to rank parameter sweep results.
- **DSR**: Deflated Sharpe Ratio — a statistical correction for multiple testing computed in `RunScenarioUseCase.EnrichWithTrialCountAndDsrAsync`.
- **ConvertJsonElement**: A private helper method in `RunScenarioUseCase` that converts `System.Text.Json.JsonElement` values to CLR types for strategy constructor parameters.
- **CompositeStrategyVisualiser**: A Web-layer Blazor component rendering a tree view of composite strategy nodes.
- **StudyDetail**: The Web-layer Blazor page displaying research study results.
- **Dashboard**: The Web-layer Blazor page displaying the strategy library and run history.

## Requirements

### Requirement 1: Eliminate Full Table-Scan in DSR Enrichment

**User Story:** As a quant researcher, I want DSR enrichment to query a single strategy version directly, so that parallel sweeps do not degrade to O(N×M) repository scans on every completed backtest.

#### Acceptance Criteria

1. THE IStrategyRepository SHALL expose a `GetVersionByIdAsync(Guid versionId, CancellationToken ct)` method that returns a single `StrategyVersion` or null without loading all strategies.
2. WHEN `EnrichWithTrialCountAndDsrAsync` is invoked, THE RunScenarioUseCase SHALL call `GetVersionByIdAsync` with the result's `StrategyVersionId` instead of iterating all strategies and versions.
3. WHEN `EnrichWithTrialCountAndDsrAsync` completes, THE RunScenarioUseCase SHALL have performed at most two repository calls (one read, one write) per invocation.
4. THE RunScenarioUseCase SHALL contain no nested `foreach` loop over all strategies and their versions within `EnrichWithTrialCountAndDsrAsync`.

### Requirement 2: Atomic Trial Count Increment

**User Story:** As a quant researcher, I want trial count increments to be atomic, so that parallel sweep workers do not lose updates through read-modify-write races.

#### Acceptance Criteria

1. THE IStrategyRepository SHALL expose an `IncrementTrialCountAsync(Guid versionId, CancellationToken ct)` method that atomically increments `TotalTrialsRun` by one.
2. WHEN a 200-combination sweep completes against a single `StrategyVersionId`, THE StrategyVersion.TotalTrialsRun SHALL be exactly 200 higher than its pre-sweep value.
3. THE RunScenarioUseCase SHALL contain no `version with { TotalTrialsRun = version.TotalTrialsRun + 1 }` pattern.
4. WHEN `IncrementTrialCountAsync` is called concurrently by multiple workers, THE IStrategyRepository implementation SHALL serialise the increments without lost updates.

### Requirement 3: Contiguous OOS Blocks with Warmup

**User Story:** As a quant researcher, I want randomised OOS windows to be contiguous time blocks with indicator warmup, so that strategies with multi-period indicators produce valid OOS measurements.

#### Acceptance Criteria

1. WHEN `RandomizedOosWorkflow` selects OOS bars for an iteration, THE RandomizedOosWorkflow SHALL select a contiguous block of bars rather than scattered random indices.
2. THE RandomizedOosOptions SHALL expose a `WarmupBars` property with a default value of 200.
3. WHEN the total bar count is insufficient for the OOS fraction plus warmup, THE RandomizedOosWorkflow SHALL throw an `InvalidOperationException` with a descriptive message before any engine run.
4. THE RandomizedOosWorkflow SHALL not contain a `HashSet<int>` of scattered OOS indices.
5. WHEN a strategy with a 50-bar indicator is tested, THE RandomizedOosWorkflow SHALL produce non-null OOS Sharpe values for all successful iterations.
6. WHEN building the OOS engine configuration, THE RandomizedOosWorkflow SHALL prepend `WarmupBars` bars before the OOS start as warmup context that is not counted in OOS performance measurement.

### Requirement 4: Per-Run Service Scope for Stateful Services

**User Story:** As a quant researcher, I want each parallel backtest run to use isolated service instances, so that mutable state on `IRiskLayer` and `IExecutionHandler` does not leak between concurrent runs.

#### Acceptance Criteria

1. WHEN `RunScenarioUseCase.RunAsync` is invoked, THE RunScenarioUseCase SHALL create a new `IServiceScope` and resolve `IRiskLayer` and `IExecutionHandler` from the scoped service provider.
2. THE SimulatedExecutionHandler.RealismAdvisories collection on one run SHALL never contain advisories from another concurrent run.
3. WHEN 50 concurrent backtest runs execute via a sweep, THE system SHALL produce 50 independent `RealismAdvisories` collections.
4. THE DI composition root SHALL not register `IRiskLayer` or `IExecutionHandler` as Singleton.
5. WHEN `RunAsync` completes or throws, THE IServiceScope SHALL be disposed.

### Requirement 5: Memoized Strategy Factory Delegates

**User Story:** As a quant researcher, I want strategy construction reflection to be cached, so that a 500-combination sweep does not repeat expensive constructor scanning on every iteration.

#### Acceptance Criteria

1. WHEN `CreateStrategy` is called for a given strategy type, THE RunScenarioUseCase SHALL call `GetConstructors()` at most once per unique type across the application lifetime.
2. WHEN a 500-combination sweep runs against the same strategy type, THE RunScenarioUseCase SHALL invoke the cached factory delegate 499 times without additional reflection.
3. THE cached factory delegate SHALL preserve all existing parameter-matching behaviour including defaults and fallbacks.
4. THE factory cache SHALL be thread-safe for concurrent access from parallel sweep workers.

### Requirement 6: Explicit JsonElement Type Conversion

**User Story:** As a quant researcher, I want strategy parameters of type Enum, TimeSpan, DateTimeOffset, and Guid to be correctly converted from JSON, so that strategy construction does not silently fail on common parameter types.

#### Acceptance Criteria

1. WHEN a `JsonElement` targets a type of `Enum`, THE ConvertJsonElement method SHALL parse the string value (case-insensitive) or integer value into the correct enum member.
2. WHEN a `JsonElement` targets a type of `TimeSpan`, THE ConvertJsonElement method SHALL parse the string representation into a `TimeSpan` value.
3. WHEN a `JsonElement` targets a type of `Guid`, THE ConvertJsonElement method SHALL parse the string into a `Guid` value.
4. WHEN a `JsonElement` targets a type of `DateTimeOffset`, THE ConvertJsonElement method SHALL parse the string into a `DateTimeOffset` value.
5. WHEN a `JsonElement` targets a `Nullable<T>` wrapper, THE ConvertJsonElement method SHALL unwrap to the underlying type before conversion.
6. IF an unhandled target type is encountered, THEN THE ConvertJsonElement method SHALL throw a `NotSupportedException` with a message identifying the type and `JsonValueKind`.
7. WHEN constructor matching encounters a `NotSupportedException`, THE RunScenarioUseCase SHALL surface a `PreflightSeverity.Error` finding instead of silently falling through.

### Requirement 7: Trade-Level Return Moments for DSR

**User Story:** As a quant researcher, I want DSR skewness and kurtosis computed from trade-level returns, so that strategies with infrequent trades receive an accurate overfitting correction rather than one diluted by flat-equity bars.

#### Acceptance Criteria

1. WHEN `BacktestResult.Trades` contains 3 or more trades, THE ComputeReturnMoments method SHALL derive skewness and kurtosis from per-trade percentage returns (`PnL / (EntryPrice × Quantity)`).
2. WHEN `BacktestResult.Trades` is null or contains fewer than 3 trades, THE ComputeReturnMoments method SHALL fall back to equity-curve bar returns.
3. WHEN a strategy produces 20 trades per year over 5 years, THE DSR skewness SHALL differ materially from the equity-curve-based computation.

### Requirement 8: Deep Clone for Parallel Config Isolation

**User Story:** As a developer, I want a `DeepClone()` extension on `ScenarioConfig` that copies all dictionary properties, so that parallel sweep workers cannot corrupt each other's configuration through shared references.

#### Acceptance Criteria

1. THE ScenarioConfig type SHALL have a `DeepClone()` extension method that returns a new instance with independent copies of `DataProviderOptions`, `StrategyParameters`, and `ResearchWorkflowOptions` dictionaries.
2. WHEN a parallel sweep worker mutates `DataProviderOptions` on its cloned config, THE mutation SHALL not be visible on any other worker's config instance.
3. THE ParameterSweepWorkflow and RandomizedOosWorkflow SHALL use `DeepClone()` before mutating config inside `Parallel.ForEachAsync` bodies.
4. THE DeepClone method SHALL handle null dictionary properties without throwing.

### Requirement 9: Injectable BacktestEngine via Factory

**User Story:** As a developer, I want `BacktestEngine` resolved through an `IBacktestEngineFactory` interface, so that unit tests can substitute a mock engine without instantiating the real implementation.

#### Acceptance Criteria

1. THE Core layer SHALL define an `IBacktestEngineFactory` interface with a `Create` method accepting `IDataProvider`, `IStrategy`, `IRiskLayer`, `IExecutionHandler`, and optional `ISessionCalendar` and `BarDataPool` parameters.
2. THE RunScenarioUseCase SHALL inject `IBacktestEngineFactory` and use it to create engine instances instead of calling `new BacktestEngine(...)`.
3. THE RunScenarioUseCase SHALL contain no `new BacktestEngine(...)` invocation.
4. WHEN a unit test substitutes a mock `IBacktestEngineFactory`, THE test SHALL be able to verify engine creation parameters without running the real engine.

### Requirement 10: Visible Failed Iteration Count in Randomized OOS

**User Story:** As a quant researcher, I want to see how many randomised OOS iterations failed, so that I can assess whether the mean OOS Sharpe is computed over a representative sample.

#### Acceptance Criteria

1. THE RandomizedOosResult record SHALL include a `FailedIterationCount` property reflecting the number of iterations that threw or returned invalid results.
2. WHEN more than 20% of iterations fail, THE RandomizedOosWorkflow SHALL attach a realism advisory warning to the result.
3. THE `MeanOosSharpe` SHALL be computed as the mean of succeeded iterations only, with the denominator clearly documented in an XML doc comment.
4. THE result renderer SHALL display `FailedIterationCount` with a warning badge when the value is greater than zero.

### Requirement 11: Deterministic Sort in Parameter Sweep Results

**User Story:** As a quant researcher, I want parameter sweep results explicitly sorted by a configurable metric, so that the ranked output is deterministic and meaningful regardless of `ConcurrentBag` enumeration order.

#### Acceptance Criteria

1. THE Application layer SHALL define a `SweepSortMetric` enum with values: `SharpeRatio`, `MaxDrawdown`, `ProfitFactor`, `WinRate`, `CalmarRatio`.
2. THE SweepOptions class SHALL expose a `SortBy` property of type `SweepSortMetric` defaulting to `SharpeRatio`.
3. WHEN the sweep completes, THE ParameterSweepWorkflow SHALL apply an explicit sort to the collected results based on `SortBy` before constructing `SweepResult`.
4. WHEN `SortBy` is `MaxDrawdown`, THE sort SHALL order results with the smallest (least negative) drawdown first.
5. THE UI heatmap metric selector SHALL drive `SweepOptions.SortBy` so that the selected metric determines both sort order and heatmap colouring.

### Requirement 12: BarsPerYear and Interval Consistency Check

**User Story:** As a quant researcher, I want a preflight warning when `BarsPerYear` is inconsistent with the configured interval, so that I do not accidentally produce incorrectly annualised Sharpe and Calmar ratios.

#### Acceptance Criteria

1. WHEN `BarsPerYear` is set to 252 and `Interval` is `"1H"`, THE PreflightValidator SHALL emit a finding with `PreflightSeverity.Warning` indicating the mismatch and the expected range.
2. WHEN `BarsPerYear` is set to 252 and `Interval` is `"1D"`, THE PreflightValidator SHALL emit no warning for this check.
3. WHEN `Interval` is an unknown or custom value not in the lookup table, THE PreflightValidator SHALL skip this check without error.
4. THE warning message SHALL include the configured `BarsPerYear`, the interval, and the expected range for that interval.

### Requirement 13: Test Set Unlock Audit Log

**User Story:** As a quant researcher, I want every test-set unlock and re-seal event recorded with a timestamp and reason, so that I have accountability for when sealed data was accessed.

#### Acceptance Criteria

1. WHEN the research phase transitions to `FinalTest`, THE SealedTestSetGuard SHALL record an audit entry via `ITestSetAuditLog.RecordUnlockAsync` with the strategy version ID, timestamp, and optional reason.
2. WHEN the research phase transitions back from `FinalTest` (re-seal), THE SealedTestSetGuard SHALL record a separate audit entry.
3. THE ITestSetAuditLog.GetEntriesAsync method SHALL return the full chronological unlock/re-seal history for a given strategy version.
4. THE StrategyDetail page SHALL display the audit log entries when any exist for the current version.

### Requirement 14: IS/OOS Efficiency Ratio Distribution Chart

**User Story:** As a quant researcher, I want a histogram of IS/OOS efficiency ratios across randomised OOS iterations, so that I can visually assess how consistently the strategy transfers in-sample performance to out-of-sample.

#### Acceptance Criteria

1. WHEN a Randomized OOS study result is displayed, THE result renderer SHALL include a histogram of `EfficiencyRatio` values binned into 10 buckets.
2. THE histogram SHALL display a vertical reference line at `EfficiencyRatio = 1.0`.
3. THE renderer SHALL display summary statistics: mean efficiency, percentage of iterations with ratio ≥ 0.5, and `FailedIterationCount`.
4. THE renderer SHALL display a colour-coded badge: green when mean efficiency ≥ 0.7, amber when 0.4–0.7, red when less than 0.4.

### Requirement 15: Buy-and-Hold Benchmark Overlay on Equity Curve

**User Story:** As a quant researcher, I want a buy-and-hold benchmark line overlaid on the equity curve chart, so that I can visually determine whether strategy returns represent alpha or market beta.

#### Acceptance Criteria

1. WHEN a backtest completes, THE RunScenarioUseCase SHALL compute a buy-and-hold benchmark equity curve normalised to the same `InitialCash` and aligned to the strategy's equity curve timestamps.
2. THE BacktestResult record SHALL include a `BenchmarkEquityCurve` property of type `IReadOnlyList<EquityPoint>?`.
3. WHEN `BenchmarkEquityCurve` is populated, THE equity curve chart component SHALL render a secondary line in a muted colour labelled "Buy & Hold".
4. WHEN the data provider cannot supply benchmark data, THE `BenchmarkEquityCurve` SHALL be null and the chart SHALL render with a single line without error.

### Requirement 16: Strategy Comparison View

**User Story:** As a quant researcher, I want to compare up to 5 backtest runs side by side with overlaid equity curves and a metrics table, so that I can make informed decisions about which parameter set or strategy variant to advance.

#### Acceptance Criteria

1. THE Web layer SHALL include a `CompareRuns` page accepting up to 5 run IDs via query parameter.
2. WHEN multiple runs are loaded, THE CompareRuns page SHALL render an overlaid equity curve chart with each run in a distinct colour.
3. THE CompareRuns page SHALL render a metrics comparison table showing Sharpe, Sortino, MaxDD, WinRate, ProfitFactor, Calmar, and DSR for each run.
4. THE metrics table SHALL highlight the best value in each metric row with a subtle green background.
5. WHEN more than 5 run IDs are provided, THE CompareRuns page SHALL display an error message and render nothing.
6. THE Dashboard run table SHALL include checkboxes and a "Compare Selected" button that navigates to the CompareRuns page with the selected IDs.

### Requirement 17: CSV/JSON Export for Trade Log and Equity Curve

**User Story:** As a quant researcher, I want to export the trade log and equity curve as CSV or JSON files, so that I can perform further analysis in external tools like Python or Excel.

#### Acceptance Criteria

1. WHEN the user selects CSV export for the trade log, THE IStrategyExporter SHALL produce a CSV file with one row per closed trade including columns: EntryDate, ExitDate, Direction, EntryPrice, ExitPrice, Quantity, PnL, ReturnOnRisk, HoldingBars.
2. WHEN the user selects JSON export for the trade log, THE IStrategyExporter SHALL produce a JSON array of trade objects with the same fields.
3. WHEN the user selects CSV export for the equity curve, THE IStrategyExporter SHALL produce a CSV file with columns: Timestamp, TotalEquity, CashBalance, UnrealisedPnl, DrawdownPercent.
4. WHEN the user selects JSON export for the equity curve, THE IStrategyExporter SHALL produce a JSON array of equity point objects.
5. THE export SHALL be triggered from the result detail page via a download button with format selection (CSV or JSON).

### Requirement 18: Composite Strategy Tree Visualiser

**User Story:** As a quant researcher, I want a visual tree representation of composite strategy nodes, so that I can understand the structure and weighting of multi-strategy compositions.

#### Acceptance Criteria

1. WHEN a composite strategy is displayed, THE CompositeStrategyVisualiser SHALL render a tree view showing each node's strategy name, weight, and allocation method.
2. THE tree view SHALL support at least 3 levels of nesting for deeply composed strategies.
3. WHEN a leaf node is selected, THE visualiser SHALL display that node's parameter summary in a detail panel.
4. THE visualiser SHALL use MudBlazor `MudTreeView` or equivalent component for consistent design language.

### Requirement 19: Keyboard Shortcut System

**User Story:** As a power user, I want keyboard shortcuts for common navigation and actions, so that I can work efficiently without reaching for the mouse.

#### Acceptance Criteria

1. WHEN the user presses `Ctrl+K`, THE application SHALL open a command palette overlay listing available actions.
2. WHEN the user presses `Ctrl+N`, THE application SHALL navigate to the new strategy builder.
3. WHEN the user presses `Ctrl+R`, THE application SHALL trigger a re-run of the last backtest configuration.
4. THE keyboard shortcut system SHALL not interfere with browser-native shortcuts (Ctrl+C, Ctrl+V, Ctrl+T, etc.).
5. THE command palette SHALL support fuzzy search filtering of available commands.

### Requirement 20: Pre-Launch Study Cost Estimator

**User Story:** As a quant researcher, I want to see an estimated time and resource cost before launching a research study, so that I can make informed decisions about whether to proceed with expensive computations.

#### Acceptance Criteria

1. WHEN a research study is configured but not yet launched, THE study launcher SHALL display an estimated duration based on the number of iterations, data bar count, and a calibrated per-iteration cost factor.
2. THE estimator SHALL display the total number of engine runs that will be executed (e.g., "This study will execute 500 backtest runs").
3. WHEN the estimated duration exceeds 5 minutes, THE estimator SHALL display a warning badge.
4. THE cost factor SHALL be calibrated from the most recent completed study of the same type, or use a conservative default if no prior study exists.

### Requirement 21: Fix StdDev Double-Arithmetic in RandomizedOosWorkflow

**User Story:** As a quant researcher, I want the OOS Sharpe standard deviation computed with correct floating-point arithmetic, so that the reported dispersion is numerically accurate.

#### Acceptance Criteria

1. THE RandomizedOosWorkflow SHALL compute `StdDevOosSharpe` using `double` arithmetic throughout the variance calculation, converting to `decimal` only for the final result.
2. WHEN all OOS Sharpe values are identical, THE `StdDevOosSharpe` SHALL be exactly zero.
3. THE computation SHALL use the population standard deviation formula (dividing by N, not N-1) consistent with the existing `MeanOosSharpe` denominator.

### Requirement 22: Delete Obsolete Validate Method in RunScenarioUseCase

**User Story:** As a developer, I want dead code removed from `RunScenarioUseCase`, so that the codebase remains clean and maintainable.

#### Acceptance Criteria

1. THE RunScenarioUseCase SHALL not contain a method named `Validate` that duplicates functionality already provided by `PreflightValidator`.
2. WHEN the obsolete `Validate` method is removed, THE solution SHALL compile without errors.
3. IF any callers reference the removed method, THEN those callers SHALL be updated to use `PreflightValidator` instead.

### Requirement 23: Multi-Symbol Data Provider Foundation

**User Story:** As a quant researcher, I want the data provider interface to support multi-symbol data requests, so that future multi-asset strategies can receive correlated data streams.

#### Acceptance Criteria

1. THE Core layer SHALL define an `IMultiSymbolDataProvider` interface with a method `GetBarsAsync(IReadOnlyList<string> symbols, DateRange range, string interval, CancellationToken ct)` returning `IAsyncEnumerable<SymbolBar>`.
2. THE `SymbolBar` record SHALL include `Symbol`, `Timestamp`, `Open`, `High`, `Low`, `Close`, and `Volume` fields.
3. WHEN a single symbol is requested, THE IMultiSymbolDataProvider SHALL behave identically to the existing `IDataProvider` for that symbol.
4. THE interface SHALL be defined in Core with no implementation in this iteration — implementation is deferred to a future version.
5. THE existing `IDataProvider` interface SHALL remain unchanged and fully functional.
