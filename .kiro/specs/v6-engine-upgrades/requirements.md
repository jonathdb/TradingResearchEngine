# Requirements Document

## Introduction

V6 Engine Upgrades delivers four tracks of improvements to TradingResearchEngine: (1) full long/short execution replacing the V5 `LongOnlyGuard`, (2) SQLite index persistence with parallel research workflow execution, (3) Plotly.Blazor interactive charting across strategy detail and research result pages, and (4) quant depth improvements including CPCV implementation, prop-firm evaluation wiring, and timeframe-aware recommendations. All changes preserve the `Core ← Application ← Infrastructure ← { Cli, Api, Web }` dependency rule.

## Glossary

- **Portfolio**: The Core-layer class that tracks open long and short positions, cash balance, unrealised PnL, and equity curve by consuming FillEvent instances.
- **SimulatedExecutionHandler**: The Application-layer execution handler that simulates order fills with slippage and commission for long, short, and flat directions.
- **DefaultRiskLayer**: The Application-layer risk component that converts strategy signals into orders, enforcing exposure limits and delegating to IPositionSizingPolicy.
- **PreflightValidator**: The Application-layer validator that checks ScenarioConfig for issues before an engine run starts.
- **BarsPerYearDefaults**: A Core-layer static class providing canonical intraday bar-count constants for Forex (24h × 252 trading days).
- **SqliteIndexRepository**: An Infrastructure-layer persistence implementation that maintains a SQLite index over existing JSON files for O(log n) lookups.
- **ResearchChecklistService**: The Application-layer service that computes a strategy's research completeness checklist and confidence level.
- **CpcvStudyHandler**: The Application-layer research workflow implementing Combinatorial Purged Cross-Validation (De Prado, 2018).
- **PropFirmEvaluationRepository**: The persistence interface and implementation for storing prop-firm evaluation records.
- **PropFirmPackLoader**: The service interface and implementation for loading prop-firm rule packs from JSON files.
- **EquityCurveChart**: A Blazor Web component rendering an equity line with optional drawdown overlay using Plotly.Blazor.
- **MonteCarloFanChart**: A Blazor Web component rendering P10/P50/P90 percentile bands for Monte Carlo simulation results.
- **WalkForwardCompositeChart**: A Blazor Web component rendering the stitched OOS composite equity curve with window boundary shading.
- **ParameterSweepHeatmap**: A Blazor Web component rendering a 2D heatmap of parameter sweep results coloured by Sharpe ratio.
- **ChartDataHelpers**: A static helper class in the Web layer containing pure data transformation functions for chart rendering.
- **StrategyLibrary**: The Blazor page displaying all strategies with filtering, retirement toggle, and management actions.
- **BackgroundStudyService**: The Application-layer service that orchestrates background execution of research studies.
- **DukascopyDataProvider**: The Infrastructure-layer data provider that fetches and caches Dukascopy Forex/CFD data.

## Requirements

### Requirement 1: Short Position Execution

**User Story:** As a quant researcher, I want the engine to execute short positions with correct PnL and mark-to-market, so that I can backtest bidirectional strategies.

#### Acceptance Criteria

1. WHEN a FillEvent with Direction.Short is received, THE Portfolio SHALL open a short position with negative quantity and increase CashBalance by `fillPrice × |quantity| - commission`
2. WHEN a FillEvent with Direction.Flat is received while a short position is open, THE Portfolio SHALL close the short position, decrease CashBalance by `fillPrice × |quantity| + commission`, and record a ClosedTrade with PnL equal to `(entryPrice - exitPrice) × |quantity|`
3. WHILE a short position is open, THE Portfolio SHALL compute UnrealisedPnl as `(entryPrice - currentPrice) × |quantity|` on every mark-to-market update
4. THE Portfolio SHALL compute TotalEquity as `CashBalance + Σ(longUnrealisedPnl) + Σ(shortUnrealisedPnl)` at all times
5. THE Portfolio SHALL compute GetExposureBySymbol by summing absolute values of both long and short exposure
6. THE Portfolio SHALL count both long and short open positions in OpenPositionCount

### Requirement 2: Short Execution in SimulatedExecutionHandler

**User Story:** As a quant researcher, I want the execution handler to fill short orders with correct slippage direction, so that short trades are simulated realistically.

#### Acceptance Criteria

1. WHEN an OrderEvent with Direction.Short is received, THE SimulatedExecutionHandler SHALL compute fillPrice as `basePrice - slippageAmount` and return an ExecutionResult with Outcome.Filled
2. WHEN an OrderEvent with Direction.Long is received, THE SimulatedExecutionHandler SHALL compute fillPrice as `basePrice + slippageAmount` and return an ExecutionResult with Outcome.Filled
3. THE SimulatedExecutionHandler SHALL execute orders for all three directions (Long, Short, Flat) without calling LongOnlyGuard.EnsureLongOnly

### Requirement 3: Short Signal Handling in DefaultRiskLayer

**User Story:** As a quant researcher, I want the risk layer to convert short signals into orders with correct sizing and exposure checks, so that short positions respect risk limits.

#### Acceptance Criteria

1. WHEN a SignalEvent with Direction.Short is received, THE DefaultRiskLayer SHALL create an OrderEvent with Direction.Short using the quantity from IPositionSizingPolicy
2. WHEN a SignalEvent with Direction.Flat is received while a short position is open, THE DefaultRiskLayer SHALL create a close order with the short position's quantity
3. WHEN a SignalEvent with Direction.Short is received while a long position is open and AllowReversals is false, THE DefaultRiskLayer SHALL log a RiskRejection and return null
4. THE DefaultRiskLayer SHALL use Math.Abs(quantity) for exposure checks on both long and short orders

### Requirement 4: Bidirectional Strategy Signals

**User Story:** As a quant researcher, I want built-in strategies to support short signals, so that I can backtest both long and short entry logic.

#### Acceptance Criteria

1. WHEN DirectionMode is set to Both, THE DonchianBreakoutStrategy SHALL emit Direction.Long on upper channel breakout and Direction.Short on lower channel breakdown
2. WHEN DirectionMode is set to Short, THE DonchianBreakoutStrategy SHALL emit Direction.Short on lower channel breakdown and Direction.Flat on upper channel breakout
3. WHEN the z-score crosses below the negative threshold, THE ZScoreMeanReversionStrategy SHALL emit Direction.Short instead of Direction.Flat
4. WHEN the z-score crosses below the negative threshold, THE StationaryMeanReversionStrategy SHALL emit Direction.Short instead of Direction.Flat
5. THE VolatilityScaledTrendStrategy SHALL support bidirectional signal emission based on a DirectionMode parameter

### Requirement 5: BarsPerYear Intraday Defaults

**User Story:** As a quant researcher, I want correct annualisation constants for all intraday timeframes, so that Sharpe and Sortino ratios are computed accurately for sub-daily data.

#### Acceptance Criteria

1. THE BarsPerYearDefaults SHALL define constants for all eight timeframes: M1 (362880), M5 (72576), M15 (24192), M30 (12096), H1 (6048), H2 (3024), H4 (1512), D1 (252)
2. WHEN a timeframe string is provided, THE BarsPerYearDefaults.ForTimeframe method SHALL return the corresponding constant or null for unknown timeframes
3. WHEN ScenarioConfig.BarsPerYear is the default (252) but the resolved timeframe is intraday, THE PreflightValidator SHALL emit a warning with severity Warning and code BARSYEAR_MISMATCH_INTRADAY including the suggested value
4. WHEN a timeframe is selected in the strategy builder, THE Step2DataExecutionWindow SHALL auto-populate BarsPerYear from BarsPerYearDefaults.ForTimeframe


### Requirement 6: SQLite Index Persistence

**User Story:** As a quant researcher, I want fast lookups of backtest results by strategy version, so that the UI loads quickly even with hundreds of stored results.

#### Acceptance Criteria

1. WHEN SqliteIndexRepository.InitializeAsync is called, THE SqliteIndexRepository SHALL scan the existing JSON directory and build a SQLite index in `{AppData}/TradingResearchEngine/index.db`
2. WHEN GetByIdAsync is called, THE SqliteIndexRepository SHALL read the JSON file path from the index and return the deserialised entity in O(1) time
3. WHEN SaveAsync is called, THE SqliteIndexRepository SHALL write the JSON file and upsert the corresponding index row atomically
4. WHEN DeleteAsync is called, THE SqliteIndexRepository SHALL remove both the JSON file and the index row
5. IF the SQLite index file is corrupted or missing on startup, THEN THE SqliteIndexRepository SHALL rebuild the index from a full JSON directory scan without data loss
6. IF an index row points to a deleted or moved JSON file, THEN THE SqliteIndexRepository SHALL return null, log a warning, and remove the stale index row
7. WHEN ListByVersionAsync is called, THE SqliteIndexRepository SHALL query the SQLite index and return only matching results in O(log n) time

### Requirement 7: Backtest Result Repository Interface

**User Story:** As a quant researcher, I want to query backtest results by strategy version and strategy ID, so that the research checklist and strategy detail pages load efficiently.

#### Acceptance Criteria

1. THE IBacktestResultRepository SHALL extend IRepository with ListByVersionAsync(versionId) and ListByStrategyAsync(strategyId) methods
2. WHEN ResearchChecklistService.GetVersionAsync is called, THE ResearchChecklistService SHALL use IBacktestResultRepository.ListByVersionAsync instead of the previous O(n×m) full-scan loop

### Requirement 8: Parallel Walk-Forward Execution

**User Story:** As a quant researcher, I want walk-forward analysis to execute windows in parallel, so that multi-window studies complete faster on multi-core machines.

#### Acceptance Criteria

1. WHEN a walk-forward study is launched, THE WalkForwardWorkflow SHALL pre-compute all window date ranges before execution begins
2. THE WalkForwardWorkflow SHALL execute windows using Parallel.ForEachAsync with concurrency bounded by a SemaphoreSlim limited to Max(1, Environment.ProcessorCount - 1)
3. THE WalkForwardWorkflow SHALL create a separate EventQueue instance for each parallel window to prevent shared mutable state
4. WHEN all windows complete, THE WalkForwardWorkflow SHALL sort results by windowIndex before building the summary
5. THE WalkForwardWorkflow SHALL propagate the CancellationToken to every parallel unit
6. WHEN the same seed and config are provided, THE WalkForwardWorkflow SHALL produce identical results regardless of whether execution is parallel or sequential

### Requirement 9: Parallel Parameter Sweep Execution

**User Story:** As a quant researcher, I want parameter sweeps to execute combinations in parallel, so that large grids complete faster.

#### Acceptance Criteria

1. THE ParameterSweepWorkflow SHALL execute parameter combinations using Parallel.ForEachAsync with concurrency bounded by a SemaphoreSlim limited to Max(1, Environment.ProcessorCount - 1)
2. THE ParameterSweepWorkflow SHALL create a separate EventQueue instance for each parallel combination
3. THE ParameterSweepWorkflow SHALL propagate the CancellationToken to every parallel unit

### Requirement 10: Intraday Data Caching

**User Story:** As a quant researcher, I want aggregated intraday data to be cached after the first aggregation, so that repeated backtests at the same timeframe skip redundant computation.

#### Acceptance Criteria

1. WHEN aggregated data for a symbol/timeframe/day combination does not exist in cache, THE DukascopyDataProvider SHALL aggregate from 1-minute data and write the result to `{CacheDir}/{symbol}/{priceType}/{year}/{month}/{day}_{interval}.csv`
2. WHEN aggregated data for a symbol/timeframe/day combination exists in cache and is newer than the source 1-minute cache, THE DukascopyDataProvider SHALL read directly from the aggregated cache without re-aggregating
3. THE DukascopyDataProvider SHALL log cache hits and misses at LogLevel.Debug

### Requirement 11: Strategy Retirement

**User Story:** As a quant researcher, I want to retire strategies that are no longer viable, so that my active library stays focused on promising strategies.

#### Acceptance Criteria

1. WHEN a strategy has Stage equal to Retired, THE StrategyLibrary SHALL hide the strategy from the default view
2. WHEN the "Show Retired" toggle is enabled, THE StrategyLibrary SHALL display retired strategies with a greyed-out RETIRED badge
3. WHEN a user retires a strategy, THE StrategyLibrary SHALL prompt for an optional free-text RetirementNote and store it on StrategyIdentity.RetirementNote
4. WHEN a user un-retires a strategy, THE StrategyLibrary SHALL set the stage back to Hypothesis and preserve the RetirementNote

### Requirement 12: Equity Curve Chart

**User Story:** As a quant researcher, I want to see an interactive equity curve chart on the strategy detail page, so that I can visually assess strategy performance.

#### Acceptance Criteria

1. WHEN a latest backtest run exists, THE StrategyDetail page SHALL display an EquityCurveChart component with the run's equity curve and drawdown overlay enabled
2. WHEN no backtest run exists, THE StrategyDetail page SHALL display an empty state message: "Run a backtest to see the equity curve"
3. THE EquityCurveChart SHALL render a primary TotalEquity line and a secondary drawdown filled area on a right Y-axis when ShowDrawdown is true
4. THE EquityCurveChart SHALL display a tooltip showing Timestamp, Equity, Unrealised PnL, and Drawdown percentage on hover


### Requirement 13: Result Detail Charts

**User Story:** As a quant researcher, I want detailed charts on the result detail page, so that I can analyse trade distribution, holding periods, and monthly returns.

#### Acceptance Criteria

1. THE ResultDetail page SHALL include a Charts tab containing an EquityCurveChart with drawdown overlay, a MonthlyReturnsHeatmap, a TradePnlHistogram, and a HoldingPeriodHistogram
2. THE MonthlyReturnsHeatmap SHALL compute monthly returns from the equity curve grouped by calendar month and display them as percentage values in a green/red colour scale
3. THE TradePnlHistogram SHALL bin individual trade NetPnl values into 20 buckets covering the full PnL range
4. THE HoldingPeriodHistogram SHALL display trade duration in bars as a histogram

### Requirement 14: Monte Carlo Fan Chart

**User Story:** As a quant researcher, I want a fan chart showing Monte Carlo simulation percentile bands, so that I can visualise the range of possible equity outcomes.

#### Acceptance Criteria

1. THE MonteCarloFanChart SHALL render three series: P10 (red dashed), P50 (primary solid), and P90 (green dashed) with a filled band between P10 and P90 at 20% opacity
2. THE MonteCarloFanChart SHALL display a horizontal reference line at StartEquity labelled "Start"
3. THE MonteCarloFanChart SHALL display ruin probability as a subtitle in the format "Ruin probability: {ruinProb:P1}"

### Requirement 15: Walk-Forward Composite Chart

**User Story:** As a quant researcher, I want a composite chart showing the stitched OOS equity curve with window boundaries, so that I can assess walk-forward performance continuity.

#### Acceptance Criteria

1. THE WalkForwardCompositeChart SHALL render the stitched OOS composite equity curve from WalkForwardSummary.CompositeOosEquityCurve as a line
2. THE WalkForwardCompositeChart SHALL shade each walk-forward window with alternating background colours and display vertical dashed lines at window boundaries
3. THE WalkForwardCompositeChart SHALL display per-window OOS Sharpe ratio as a bar chart on a secondary Y-axis, coloured green for positive and red for negative values

### Requirement 16: Parameter Sweep Heatmap

**User Story:** As a quant researcher, I want a 2D heatmap of parameter sweep results, so that I can identify optimal parameter regions and assess parameter stability.

#### Acceptance Criteria

1. THE ParameterSweepHeatmap SHALL render a 2D heatmap with X and Y axes corresponding to selected parameter values and cell colour representing Sharpe ratio on a red-white-green scale
2. THE ParameterSweepHeatmap SHALL display a tooltip showing exact parameter values, Sharpe, MaxDD, and WinRate on hover
3. WHEN the sweep has more than two numeric parameters, THE ParameterSweepHeatmap SHALL provide dropdowns to select which two parameters map to X and Y axes
4. THE ParameterSweepHeatmap SHALL display only when the sweep has at least two numeric parameters with at least four unique values each

### Requirement 17: Timeline Split Visualizer Enhancement

**User Story:** As a quant researcher, I want the timeline split visualizer to show bar-count density, so that I can verify data sufficiency for each segment.

#### Acceptance Criteria

1. THE TimelineSplitVisualizer SHALL display a horizontal bar with colour-coded segments: IS (blue), OOS (orange), and Sealed (red/hatched if present)
2. THE TimelineSplitVisualizer SHALL display a bar count label below each segment derived from BarsPerYear multiplied by segment duration in years
3. WHEN the IS segment has fewer bars than MinBTL, THE TimelineSplitVisualizer SHALL display a warning icon

### Requirement 18: Chart Data Transformation Helpers

**User Story:** As a developer, I want chart data transformations extracted into pure static helpers, so that they can be unit tested independently of Blazor rendering.

#### Acceptance Criteria

1. THE ChartDataHelpers SHALL provide a method to compute monthly returns from an equity curve, returning percentage values grouped by calendar month
2. THE ChartDataHelpers SHALL provide a method to bin trade PnL values into a specified number of buckets covering the full PnL range
3. WHEN an empty equity curve is provided, THE ChartDataHelpers monthly return method SHALL return an empty collection
4. WHEN an empty trades list is provided, THE ChartDataHelpers PnL binning method SHALL return an empty histogram

### Requirement 19: Prop Firm Evaluation Persistence

**User Story:** As a quant researcher, I want prop-firm evaluation results persisted, so that the research checklist can track whether a strategy has been evaluated against firm rules.

#### Acceptance Criteria

1. WHEN a prop-firm evaluation is completed, THE PropFirmEvaluationRepository SHALL persist a PropFirmEvaluationRecord containing StrategyVersionId, FirmName, PhaseName, Passed, and EvaluatedAt
2. WHEN HasCompletedEvaluationAsync is called for a strategy version with a saved evaluation, THE PropFirmEvaluationRepository SHALL return true
3. WHEN HasCompletedEvaluationAsync returns true, THE ResearchChecklistService SHALL mark the prop-firm evaluation checklist item as complete

### Requirement 20: Benchmark Excess Sharpe Wiring

**User Story:** As a quant researcher, I want the benchmark excess Sharpe chip to show real data from a completed benchmark comparison study, so that I can see true alpha over buy-and-hold.

#### Acceptance Criteria

1. WHEN a completed BenchmarkComparison study exists for the current strategy version, THE StrategyDetail page SHALL load the BenchmarkComparisonResult and display ExcessSharpe on the chip
2. WHEN no BenchmarkComparison study exists, THE StrategyDetail page SHALL display the chip as null with tooltip "Run a Benchmark Comparison study to see excess Sharpe vs Buy & Hold"

### Requirement 21: Prop Firm Pack Loader Service

**User Story:** As a developer, I want prop-firm rule packs loaded via a DI-injected service, so that duplicate LoadBuiltInPacks calls are eliminated and the loading logic is centralised.

#### Acceptance Criteria

1. THE IPropFirmPackLoader SHALL provide LoadAllPacksAsync and LoadPackAsync(firmId) methods for loading prop-firm rule packs
2. THE JsonPropFirmPackLoader SHALL read rule packs from `data/firms/*.json` asynchronously
3. THE IPropFirmPackLoader SHALL be registered as a singleton in DI and injected into StrategyDetail and PropFirmEvaluation pages replacing all inline LoadBuiltInPacks calls


### Requirement 22: CPCV Study Implementation

**User Story:** As a quant researcher, I want to run Combinatorial Purged Cross-Validation, so that I can estimate the probability that my strategy is overfit to in-sample data.

#### Acceptance Criteria

1. WHEN a CPCV study is launched, THE CpcvStudyHandler SHALL split the data range into N equal-length folds (default N=6) and generate all C(N, k) combinations (default k=2, yielding 15 combinations)
2. WHEN C(N, k) combinations are generated, THE CpcvStudyHandler SHALL produce exactly `N! / (k! × (N-k)!)` combinations
3. FOR EACH combination, THE CpcvStudyHandler SHALL train the strategy on the N-k training folds to produce that combination's IS Sharpe, and test on the k test folds to produce that combination's OOS Sharpe
4. THE CpcvStudyHandler SHALL compute ProbabilityOfOverfitting as the count of combinations where that combination's OOS Sharpe is less than that same combination's IS Sharpe, divided by the total number of combinations
5. THE CpcvStudyHandler SHALL compute MedianOosSharpe as the median of the OOS Sharpe distribution across all combinations
6. THE CpcvStudyHandler SHALL compute PerformanceDegradation as `1 - (MedianOosSharpe / MedianIsSharpe)`, guarding against division by zero
7. WHEN the same seed and inputs are provided, THE CpcvStudyHandler SHALL produce identical CpcvResult output
8. IF the data range is too short for NumPaths folds (each fold has fewer than 30 bars), THEN THE CpcvStudyHandler SHALL throw an InvalidOperationException with a descriptive message before any engine runs
9. IF all OOS Sharpe values are zero or negative, THEN THE CpcvStudyHandler SHALL return CpcvResult with ProbabilityOfOverfitting equal to 1.0
10. THE CpcvStudyHandler SHALL validate that NumPaths is at least 3, TestFolds is at least 1, and TestFolds is less than NumPaths

### Requirement 23: CPCV Research Checklist Integration

**User Story:** As a quant researcher, I want CPCV completion tracked in the research checklist, so that my confidence level reflects whether overfitting has been assessed.

#### Acceptance Criteria

1. THE ResearchChecklistService SHALL include a 9th checklist item for CPCV study completion
2. THE ResearchChecklistService SHALL compute ConfidenceLevel as HIGH when at least 8 of 9 items are complete, MEDIUM when at least 5 are complete, and LOW when fewer than 5 are complete

### Requirement 24: Timeframe-Aware MinBTL Recommendation

**User Story:** As a quant researcher, I want the minimum backtest length expressed in human-readable trading days, so that I can quickly understand how much data is needed.

#### Acceptance Criteria

1. WHEN a bar count and timeframe are provided, THE BarsPerYearDefaults.BarsToHumanDuration method SHALL return a string in the format "~{tradingDays} trading days of {timeframeLabel} data required"
2. WHEN an unknown timeframe is provided, THE BarsPerYearDefaults.BarsToHumanDuration method SHALL return "{bars} bars" as a fallback
3. THE PreflightValidator SHALL include the human-readable duration in the preflight finding message when MinBTL is not met

### Requirement 25: Short/Long PnL Symmetry

**User Story:** As a quant researcher, I want long and short trades on the same price series to produce equal-magnitude PnL, so that I can trust the engine treats both directions fairly.

#### Acceptance Criteria

1. FOR ALL price series and positive quantities, a long trade with PnL `(exitPrice - entryPrice) × quantity` and a symmetric short trade with PnL `(entryPrice - exitPrice) × quantity` SHALL have equal absolute PnL magnitude when entry price, exit price, and quantity are identical

### Requirement 26: Parallel Execution Safety

**User Story:** As a developer, I want parallel research workflows to be thread-safe, so that concurrent backtest runs produce correct results without data corruption.

#### Acceptance Criteria

1. THE WalkForwardWorkflow and ParameterSweepWorkflow SHALL create a separate EventQueue and Portfolio instance for each parallel backtest run
2. THE WalkForwardWorkflow and ParameterSweepWorkflow SHALL collect results into a ConcurrentBag and sort after completion

### Requirement 27: NuGet Package Registration

**User Story:** As a developer, I want new NuGet dependencies documented in tech.md, so that the package list stays authoritative.

#### Acceptance Criteria

1. THE tech.md steering document SHALL list Microsoft.Data.Sqlite under the Infrastructure project
2. THE tech.md steering document SHALL list Plotly.Blazor under the Web project

### Requirement 28: V6 Test Coverage

**User Story:** As a developer, I want comprehensive test coverage for all V6 features, so that regressions are caught early.

#### Acceptance Criteria

1. THE UnitTests project SHALL contain V6 test files for ShortSelling, BarsPerYearDefaults, SqliteIndexRepository, ParallelWalkForward, StrategyRetirement, MonthlyReturnCalculator, TradePnlBinning, PropFirmChecklist, Cpcv, and BarsToHumanDuration
2. THE UnitTests project SHALL contain property tests for ShortLongPnlSymmetry (Property 9) and PortfolioEquityConservationWithShorts (Property 10) with minimum 100 iterations each
3. ALL existing V1–V5 tests SHALL continue to pass after V6 changes
