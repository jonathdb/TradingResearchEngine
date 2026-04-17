# Implementation Plan: V6 Engine Upgrades

## Overview

V6 delivers four sequential tracks: (1) short selling engine, (2) persistence & performance, (3) visualisation, and (4) quant depth & lifecycle. Each track ends with a checkpoint. All code is C# 12 / .NET 8 following the `Core ← Application ← Infrastructure ← { Cli, Api, Web }` dependency rule. Tasks are ordered so that Core changes land before Application and Infrastructure consumers.

## Tasks

- [x] 1. Track 1 — Short Selling Engine
  - [x] 1.1 Add `RetirementNote` as a nullable trailing parameter on `StrategyIdentity` in Core
    - Add `string? RetirementNote = null` as the last parameter on the `StrategyIdentity` sealed record in `Application/Strategy/StrategyIdentity.cs`
    - This MUST be done before any Application or Infrastructure tasks that read `RetirementNote` (Track 2 retirement UI)
    - _Requirements: 11.3, 11.4_

  - [x] 1.2 Create `BarsPerYearDefaults` static class in Core
    - Create `TradingResearchEngine.Core/Configuration/BarsPerYearDefaults.cs`
    - Define constants for all 8 timeframes: M1 (362880), M5 (72576), M15 (24192), M30 (12096), H1 (6048), H2 (3024), H4 (1512), D1 (252)
    - Implement `ForTimeframe(string timeframe)` returning `int?`
    - Implement `BarsToHumanDuration(int bars, string timeframe)` returning human-readable string
    - XML doc comments on all public members
    - _Requirements: 5.1, 5.2, 24.1, 24.2_

  - [x] 1.3 Add `AllowReversals` flag to `ExecutionConfig` in Core
    - Add `bool AllowReversals = false` as a trailing parameter on the `ExecutionConfig` sealed record
    - _Requirements: 3.3_

  - [x] 1.4 Implement short position tracking in `Portfolio` (Core)
    - Add `_shortPositions` dictionary parallel to existing `_positions`
    - Expose `ShortPositions` as `IReadOnlyDictionary`
    - Update `Update(FillEvent)` to handle `Direction.Short` (open short, cash += proceeds) and `Direction.Flat` with open short (close short, cash -= cover cost, PnL = entry - exit)
    - Update `MarkToMarket` to compute short unrealised PnL as `(entryPrice - currentPrice) × |qty|`
    - Update `TotalEquity` to include both long and short unrealised PnL
    - Update `GetExposureBySymbol()` to sum absolute values of long and short exposure
    - Update `OpenPositionCount` to count both long and short positions
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6_

  - [x] 1.5 Remove `LongOnlyGuard` from `SimulatedExecutionHandler` (Application)
    - Remove the `LongOnlyGuard.EnsureLongOnly(direction)` call
    - Implement short fill: `fillPrice = basePrice - slippageAmount`
    - Long fill unchanged: `fillPrice = basePrice + slippageAmount`
    - Flat fill: `fillPrice = basePrice - slippageAmount` (favorable to closer)
    - For tick data: Short fills at Bid, Long fills at Ask
    - _Requirements: 2.1, 2.2, 2.3_

  - [x] 1.6 Update `DefaultRiskLayer` for short signal handling (Application)
    - Remove `LongOnlyGuard.EnsureLongOnly` call
    - Handle `Direction.Short` signals: create short order using `IPositionSizingPolicy` quantity
    - Handle `Direction.Flat` with open short: create close order with short position's quantity
    - When `AllowReversals == false` and opposing position exists: log `RiskRejection`, return null
    - Use `Math.Abs(quantity)` for exposure checks on both long and short
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 1.7 Update `IPositionSizingPolicy` implementations for signed quantity (Application)
    - All four implementations (`FixedFractionalSizing`, `FixedLotSizing`, `VolatilityTargetSizing`, `PercentOfEquitySizing`) return positive quantity for both Long and Short
    - Sign is applied by `DefaultRiskLayer` caller, not the sizing policy
    - _Requirements: 3.1, 3.4_

  - [x] 1.8 Add bidirectional signal support to strategies (Application)
    - `DonchianBreakoutStrategy`: add `DirectionMode` parameter (Long/Short/Both) with `[ParameterMeta]`; emit `Direction.Short` on lower breakdown when mode is Short or Both
    - `VolatilityScaledTrendStrategy`: add `DirectionMode` parameter with bidirectional signal support
    - `ZScoreMeanReversionStrategy`: emit `Direction.Short` instead of `Direction.Flat` on negative z-score threshold crossing
    - `StationaryMeanReversionStrategy`: same as ZScore — wire `Direction.Short` on short entry
    - `BaselineBuyAndHoldStrategy` and `MacroRegimeRotationStrategy` remain long-only (no changes)
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_

  - [x] 1.9 Add `BARSYEAR_MISMATCH_INTRADAY` warning to `PreflightValidator` (Application)
    - When `ScenarioConfig.BarsPerYear` is default (252) but resolved timeframe is intraday, emit `PreflightSeverity.Warning` with code `BARSYEAR_MISMATCH_INTRADAY` and suggested value from `BarsPerYearDefaults`
    - Include human-readable duration from `BarsToHumanDuration` in MinBTL findings
    - _Requirements: 5.3, 24.3_

  - [x] 1.10 Update `Step2DataExecutionWindow.razor` for BarsPerYear auto-population (Web)
    - Auto-populate `BarsPerYear` from `BarsPerYearDefaults.ForTimeframe` when timeframe changes
    - Extend timeframe selector to include: 1m, 5m, 15m, 30m, 1H, 2H, 4H, Daily
    - Remove hardcoded `MudSelectItem` lists; drive from a `TimeframeOptions` static class in Web layer
    - _Requirements: 5.4_

  - [x]* 1.11 Write unit tests for short selling (UnitTests)
    - Create `UnitTests/V6/ShortSellingTests.cs`
    - Short open: fill at correct price with negative quantity
    - Short close: correct PnL = (entry - exit) × qty
    - Short mark-to-market: TotalEquity moves inversely to price
    - Short reversal when `AllowReversals = true`
    - `LongOnlyGuard` removed: `Direction.Short` no longer throws
    - _Requirements: 1.1, 1.2, 1.3, 2.1, 2.3, 25.1, 28.1_

  - [x]* 1.12 Write unit tests for BarsPerYearDefaults (UnitTests)
    - Create `UnitTests/V6/BarsPerYearDefaultsTests.cs`
    - All 8 timeframe constants are positive integers
    - M1 = 252 × 1440 exactly
    - `ForTimeframe` returns null for unknown timeframe
    - Preflight emits `BARSYEAR_MISMATCH_INTRADAY` warning for daily BarsPerYear on M1 data
    - _Requirements: 5.1, 5.2, 5.3, 28.1_

  - [x]* 1.13 Write property test: ShortLongPnlSymmetry (UnitTests)
    - **Property 9: Short/Long PnL Symmetry**
    - A long trade and symmetric short trade on same price series produce equal-magnitude PnL
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 1.2, 25.1**

  - [x]* 1.14 Write property test: PortfolioEquityConservationWithShorts (UnitTests)
    - **Property 10: Portfolio Equity Conservation with Shorts**
    - TotalEquity == Cash + longUnrealised + shortUnrealised at all times
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 1.3, 1.4, 1.5, 1.6**

  - [x]* 1.15 Write property test: BarsPerYearConsistency (UnitTests)
    - **Property 13: BarsPerYear Consistency**
    - For any known timeframe, `ForTimeframe(tf)` equals `252 × barsPerDay(tf)`
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 5.1, 5.2**

  - [x]* 1.16 Write property test: SlippageDirectionSymmetry (UnitTests)
    - **Property 15: Slippage Direction Symmetry**
    - Short fill: `fillPrice = basePrice - slippageAmount`; Long fill: `fillPrice = basePrice + slippageAmount`
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 2.1, 2.2**

- [x] 2. Checkpoint 1 — Short Selling Engine
  - Ensure all existing V1–V5 tests still pass
  - Ensure all new Track 1 unit and property tests pass
  - Verify no `LongOnlyGuard.EnsureLongOnly` calls remain in any execution path
  - Ask the user if questions arise

- [x] 3. Track 2 — Persistence & Performance
  - [x] 3.1 Update `tech.md` with new NuGet packages
    - Add `Microsoft.Data.Sqlite` under Infrastructure project
    - Add `Plotly.Blazor` under Web project
    - _Requirements: 27.1, 27.2_

  - [x] 3.2 Create `IBacktestResultRepository` interface (Application)
    - Create `TradingResearchEngine.Application/Research/IBacktestResultRepository.cs`
    - Extend `IRepository<BacktestResult>` with `ListByVersionAsync(versionId)` and `ListByStrategyAsync(strategyId)`
    - _Requirements: 7.1_

  - [x] 3.3 Create `SqliteIndexRepository<T>` (Infrastructure)
    - Create `TradingResearchEngine.Infrastructure/Persistence/SqliteIndexRepository.cs`
    - Add `Microsoft.Data.Sqlite` NuGet reference to Infrastructure project
    - Implement `InitializeAsync`: scan JSON directory, build SQLite index in `{AppData}/TradingResearchEngine/index.db`
    - Implement `GetByIdAsync`: read file path from index, deserialise JSON (O(1))
    - Implement `SaveAsync`: write JSON file FIRST, then upsert SQLite index row (not atomic — if crash between steps, `InitializeAsync` rebuilds from JSON on next startup)
    - Implement `DeleteAsync`: remove JSON file and index row
    - Implement `ListByVersionAsync` and `ListByStrategyAsync`: query SQLite index, read matching JSON files
    - Handle corrupted/missing index: rebuild from full JSON scan without data loss
    - Handle stale index rows (deleted JSON): return null, log warning, remove stale row
    - Create SQLite schema with `BacktestResultIndex` and `StudyIndex` tables per design
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 6.5, 6.6, 6.7_

  - [x] 3.4 Wire `SqliteIndexRepository` into DI and update `ResearchChecklistService` (Application/Infrastructure)
    - Register `SqliteIndexRepository<BacktestResult>` as `IBacktestResultRepository` in DI
    - Update `ResearchChecklistService.GetVersionAsync` to use `IBacktestResultRepository.ListByVersionAsync` instead of O(n×m) full-scan loop
    - _Requirements: 7.2_

  - [x] 3.5 Implement parallel walk-forward execution (Application)
    - In `WalkForwardWorkflow.cs`: replace sequential `while (true)` loop with parallel pattern
    - Pre-compute all window date ranges (pure date arithmetic, no I/O)
    - Execute via `Parallel.ForEachAsync` with `SemaphoreSlim(Max(1, ProcessorCount - 1))`
    - Each window creates its own `EventQueue` and `Portfolio` instance
    - Collect into `ConcurrentBag<WalkForwardWindow>`, sort by `windowIndex` after completion
    - Propagate `CancellationToken` to every parallel unit
    - Update `IProgressReporter` to report aggregate completion (windows completed / total)
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 8.6, 26.1, 26.2_

  - [x] 3.6 Implement parallel parameter sweep execution (Application)
    - In `ParameterSweepWorkflow.cs`: apply `Parallel.ForEachAsync` + `SemaphoreSlim` pattern
    - Each combination creates its own `EventQueue` instance
    - Concurrency limit: `Environment.ProcessorCount - 1`
    - Propagate `CancellationToken` to every parallel unit
    - _Requirements: 9.1, 9.2, 9.3, 26.1, 26.2_

  - [x] 3.7 Add second-level aggregated cache to `DukascopyDataProvider` (Infrastructure)
    - Cache path: `{CacheDir}/{symbol}/{priceType}/{year}/{month}/{day}_{interval}.csv`
    - Before aggregating, check if aggregated CSV exists and is newer than source 1m cache
    - Write aggregated result to cache after first aggregation
    - Log cache hits/misses at `LogLevel.Debug`
    - _Requirements: 10.1, 10.2, 10.3_

  - [x] 3.8 Implement strategy retirement UI in `StrategyLibrary.razor` (Web)
    - Note: `RetirementNote` on `StrategyIdentity` was already added in task 1.1
    - Hide `Stage == Retired` strategies from default view
    - Add "Show Retired" toggle to reveal retired strategies with greyed-out RETIRED badge
    - Retirement action prompts for optional free-text `RetirementNote`
    - Un-retire action sets stage back to `Hypothesis`, preserves `RetirementNote`
    - _Requirements: 11.1, 11.2, 11.3, 11.4_

  - [x]* 3.9 Write unit tests for SQLite index repository (UnitTests)
    - Create `UnitTests/V6/SqliteIndexRepositoryTests.cs`
    - Index built correctly from existing JSON files
    - `ListByVersionAsync` returns only matching items
    - Save → index row created; Delete → index row removed
    - Cold start (no index file) falls back to full scan and rebuilds
    - Note: UnitTests reference Core and Application only; use in-memory fakes for repository interface testing
    - _Requirements: 6.1, 6.5, 6.7, 28.1_

  - [x]* 3.10 Write unit tests for parallel walk-forward (UnitTests)
    - Create `UnitTests/V6/ParallelWalkForwardTests.cs`
    - Same seed + config → identical results as sequential execution
    - Cancellation token respected (all windows cancelled promptly)
    - Window count formula unchanged (existing Property 7 still passes)
    - _Requirements: 8.5, 8.6, 28.1_

  - [x]* 3.11 Write unit tests for strategy retirement (UnitTests)
    - Create `UnitTests/V6/StrategyRetirementTests.cs`
    - Retired strategy hidden from default library view
    - `RetirementNote` persisted and retrieved correctly
    - Un-retire sets stage to `Hypothesis`
    - _Requirements: 11.1, 11.3, 11.4, 28.1_

  - [x]* 3.12 Write property test: SQLiteIndexSync (UnitTests)
    - **Property 14: SQLite Index Sync**
    - For any entity saved via repository, the index row's FilePath points to a valid JSON file with matching Id
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 6.2, 6.3, 6.4**

  - [x]* 3.13 Write property test: ParallelWalkForwardDeterminism (UnitTests)
    - **Property 12: Parallel Walk-Forward Determinism**
    - Same seed + config → identical window results sorted by window index
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 8.4, 8.6**

- [x] 4. Checkpoint 2 — Persistence & Performance
  - Ensure all existing tests still pass
  - Ensure all new Track 2 unit and property tests pass
  - Verify `ListByVersionAsync` is used in `ResearchChecklistService` and `StrategyDetail.razor`
  - Ask the user if questions arise

- [x] 5. Track 3 — Visualisation
  - [x] 5.1 Add `Plotly.Blazor` NuGet reference to Web project
    - Add `Plotly.Blazor` package reference to `TradingResearchEngine.Web.csproj`
    - _Requirements: 27.2_

  - [x] 5.2 Create `ChartDataHelpers` static class (Web)
    - Create `TradingResearchEngine.Web/Helpers/ChartDataHelpers.cs`
    - Implement `ComputeMonthlyReturns(IReadOnlyList<EquityCurvePoint>)` → percentage values grouped by calendar month
    - Implement `BinTradePnl(IReadOnlyList<ClosedTrade>, int bins = 20)` → histogram buckets covering full PnL range
    - Implement `BinHoldingPeriods(IReadOnlyList<ClosedTrade>)` → duration-in-bars histogram
    - Empty inputs return empty collections
    - _Requirements: 18.1, 18.2, 18.3, 18.4_

  - [x] 5.3 Create `EquityCurveChart` component (Web)
    - Create `TradingResearchEngine.Web/Components/Charts/EquityCurveChart.razor`
    - Parameters: `IReadOnlyList<EquityCurvePoint> Curve`, `string Title`, `bool ShowDrawdown`
    - Primary series: TotalEquity line; secondary: drawdown filled area on right Y-axis
    - Tooltip: Timestamp, Equity, Unrealised PnL, Drawdown %
    - `@rendermode InteractiveServer`, responsive width, 320px height
    - _Requirements: 12.3, 12.4_

  - [x] 5.4 Wire `EquityCurveChart` into `StrategyDetail.razor` (Web)
    - Add chart below metrics summary chips, above Research Checklist on Overview tab
    - Show only when `_latestRun is not null`
    - Empty state: "Run a backtest to see the equity curve"
    - _Requirements: 12.1, 12.2_

  - [x] 5.5 Create `MonthlyReturnsHeatmap` component (Web)
    - Create `TradingResearchEngine.Web/Components/Charts/MonthlyReturnsHeatmap.razor`
    - Parameters: `IReadOnlyList<EquityCurvePoint> Curve`
    - Calendar grid of monthly return %, green/red colour scale
    - Uses `ChartDataHelpers.ComputeMonthlyReturns` for data transformation
    - _Requirements: 13.2_

  - [x] 5.6 Create `TradePnlHistogram` component (Web)
    - Create `TradingResearchEngine.Web/Components/Charts/TradePnlHistogram.razor`
    - Parameters: `IReadOnlyList<ClosedTrade> Trades`
    - 20-bin PnL distribution using `ChartDataHelpers.BinTradePnl`
    - _Requirements: 13.3_

  - [x] 5.7 Create `HoldingPeriodHistogram` component (Web)
    - Create `TradingResearchEngine.Web/Components/Charts/HoldingPeriodHistogram.razor`
    - Parameters: `IReadOnlyList<ClosedTrade> Trades`
    - Duration-in-bars distribution
    - _Requirements: 13.4_

  - [x] 5.8 Add Charts tab to `ResultDetail.razor` (Web)
    - Add "Charts" tab alongside existing Trades tab
    - Include: EquityCurveChart (with drawdown), MonthlyReturnsHeatmap, TradePnlHistogram, HoldingPeriodHistogram
    - _Requirements: 13.1_

  - [x] 5.9 Create `MonteCarloFanChart` component (Web)
    - Create `TradingResearchEngine.Web/Components/Charts/MonteCarloFanChart.razor`
    - Parameters: `IReadOnlyList<MonteCarloPercentileBand> Bands`, `decimal StartEquity`
    - Three series: P10 (red dashed), P50 (primary solid), P90 (green dashed)
    - Filled band between P10 and P90 at 20% opacity
    - Horizontal reference line at StartEquity
    - Subtitle: "Ruin probability: {ruinProb:P1}"
    - Wire into Monte Carlo study result page
    - _Requirements: 14.1, 14.2, 14.3_

  - [x] 5.10 Create `WalkForwardCompositeChart` component (Web)
    - Create `TradingResearchEngine.Web/Components/Charts/WalkForwardCompositeChart.razor`
    - Parameters: `WalkForwardSummary Summary`
    - Stitched OOS composite equity curve as line
    - Alternating background colours for window boundaries with vertical dashed lines
    - Secondary Y-axis: per-window OOS Sharpe as bar chart (green positive, red negative)
    - Wire into Walk-Forward result page
    - _Requirements: 15.1, 15.2, 15.3_

  - [x] 5.11 Create `ParameterSweepHeatmap` component (Web)
    - Create `TradingResearchEngine.Web/Components/Charts/ParameterSweepHeatmap.razor`
    - Parameters: `SweepResult`, `string XParam`, `string YParam`, `string MetricName`
    - 2D heatmap: red (negative) → white (zero) → green (positive) Sharpe colour scale
    - Tooltip: parameter values + Sharpe, MaxDD, WinRate
    - Dropdowns for axis selection when sweep has >2 params
    - Display only when ≥2 numeric parameters with ≥4 unique values each
    - Wire into Parameter Sweep result page
    - _Requirements: 16.1, 16.2, 16.3, 16.4_

  - [x] 5.12 Enhance `TimelineSplitVisualizer` with bar-count density (Web)
    - Colour-coded segments: IS (blue), OOS (orange), Sealed (red/hatched)
    - Bar count label below each segment derived from `BarsPerYear × segment_years`
    - Warning icon if IS segment has < MinBTL bars
    - _Requirements: 17.1, 17.2, 17.3_

  - [x]* 5.13 Write unit tests for `ChartDataHelpers` (UnitTests)
    - Create `UnitTests/V6/MonthlyReturnCalculatorTests.cs`
    - Equity curve covering 3 months → 3 monthly returns computed correctly
    - Single bar → single month return
    - Returns normalised as percentages
    - Create `UnitTests/V6/TradePnlBinningTests.cs`
    - 20 bins cover full PnL range
    - Empty trades list → empty histogram
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 28.1_

  - [x]* 5.14 Write property test: MonthlyReturnComputation (UnitTests)
    - **Property 18: Monthly Return Computation**
    - Non-empty equity curve spanning multiple months → one return per calendar month
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 13.2, 18.1**

  - [x]* 5.15 Write property test: PnLBinningCoverage (UnitTests)
    - **Property 19: PnL Binning Coverage**
    - Non-empty trade PnL list → exactly 20 bins covering min to max PnL
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 13.3, 18.2**

- [x] 6. Checkpoint 3 — Visualisation
  - Ensure all existing tests still pass
  - Ensure all new Track 3 unit and property tests pass
  - Verify all chart components render without errors on their respective pages
  - Ask the user if questions arise

- [x] 7. Track 4 — Quant Depth & Lifecycle
  - [x] 7.1 Create `IPropFirmEvaluationRepository` and `PropFirmEvaluationRecord` (Application)
    - Create `TradingResearchEngine.Application/PropFirm/IPropFirmEvaluationRepository.cs` with `HasCompletedEvaluationAsync` and `SaveEvaluationAsync`
    - Create `TradingResearchEngine.Application/PropFirm/PropFirmEvaluationRecord.cs` as sealed record implementing `IHasId`
    - Validate non-empty `StrategyVersionId` and `FirmName`
    - _Requirements: 19.1, 19.2_

  - [x] 7.2 Implement `JsonPropFirmEvaluationRepository` (Infrastructure)
    - Create `TradingResearchEngine.Infrastructure/Persistence/JsonPropFirmEvaluationRepository.cs`
    - Follow standard `JsonFileRepository<T>` pattern
    - Register in DI
    - _Requirements: 19.1, 19.2_

  - [x] 7.3 Wire prop-firm evaluation into `ResearchChecklistService` (Application)
    - Replace `bool propFirmEvaluation = false; // TODO` with `IPropFirmEvaluationRepository.HasCompletedEvaluationAsync`
    - This unblocks the 8th checklist item
    - _Requirements: 19.3_

  - [x] 7.4 Create `IPropFirmPackLoader` interface and `JsonPropFirmPackLoader` implementation
    - Create `TradingResearchEngine.Application/PropFirm/IPropFirmPackLoader.cs` with `LoadAllPacksAsync` and `LoadPackAsync(firmId)`
    - Create `TradingResearchEngine.Infrastructure/PropFirm/JsonPropFirmPackLoader.cs` — reads from `data/firms/*.json` asynchronously
    - Register as singleton in DI
    - Replace all `LoadBuiltInPacks()` calls in `StrategyDetail.razor` and `PropFirmEvaluation.razor` with injected `IPropFirmPackLoader`
    - _Requirements: 21.1, 21.2, 21.3_

  - [x] 7.5 Wire `BenchmarkExcessSharpe` to real data in `StrategyDetail.razor` (Web)
    - Replace `_benchmarkExcessSharpe = _latestRun.SharpeRatio` with actual `BenchmarkComparisonResult.ExcessSharpe` from most recent completed benchmark study
    - If no benchmark study exists, show chip as null with tooltip: "Run a Benchmark Comparison study to see excess Sharpe vs Buy & Hold"
    - _Requirements: 20.1, 20.2_

  - [x] 7.6 Implement `CpcvStudyHandler` (Application)
    - Implement full CPCV algorithm in `TradingResearchEngine.Application/Research/CpcvStudyHandler.cs`
    - Create `CpcvOptions` record: `NumPaths` (default 6), `TestFolds` (default 2), `Seed` (nullable)
    - Create `CpcvResult` record: `MedianOosSharpe`, `ProbabilityOfOverfitting`, `PerformanceDegradation`, `OosSharpeDistribution`, `TotalCombinations`, `IsSharpeDistribution`
    - Split data into N equal-length folds, generate C(N,k) combinations
    - For each combination: train on N-k folds (IS Sharpe), test on k folds (OOS Sharpe)
    - `ProbabilityOfOverfitting` = count of combinations where that combination's OOS Sharpe < that same combination's IS Sharpe, divided by total combinations
    - `PerformanceDegradation` = 1 - (MedianOosSharpe / MedianIsSharpe), guard div-by-zero
    - Validate: `NumPaths ≥ 3`, `TestFolds ≥ 1`, `TestFolds < NumPaths`
    - Throw `InvalidOperationException` if each fold has < 30 bars
    - Accept explicit `Seed` for deterministic output
    - _Requirements: 22.1, 22.2, 22.3, 22.4, 22.5, 22.6, 22.7, 22.8, 22.9, 22.10_

  - [x] 7.7 Add CPCV as 9th checklist item in `ResearchChecklistService` (Application)
    - Add `StudyType.Cpcv` to enum if not present
    - Wire into `BackgroundStudyService`
    - Add `bool CpcvDone` as 9th item in `ResearchChecklist` (do not remove existing items)
    - Update `TotalChecks` to 9
    - Update `ConfidenceLevel` thresholds: HIGH ≥ 8, MEDIUM ≥ 5, LOW < 5
    - _Requirements: 23.1, 23.2_

  - [x] 7.8 Add timeframe-aware MinBTL recommendation to `PreflightValidator` (Application)
    - Use `BarsPerYearDefaults.BarsToHumanDuration` to translate bar count into human-readable duration
    - Show translation in preflight finding message and `Step2DataExecutionWindow.razor` diagnostics
    - _Requirements: 24.1, 24.2, 24.3_

  - [x] 7.9 Update `product.md` with V6 scope section
    - Add V6 scope section at the bottom of `.kiro/steering/product.md`
    - _Requirements: (documentation)_

  - [x]* 7.10 Write unit tests for prop-firm checklist wiring (UnitTests)
    - Create `UnitTests/V6/PropFirmChecklistTests.cs`
    - `HasCompletedEvaluation = true` unlocks 8th checklist item
    - Confidence reaches HIGH with 8/9 items
    - _Requirements: 19.3, 23.2, 28.1_

  - [x]* 7.11 Write unit tests for CPCV (UnitTests)
    - Create `UnitTests/V6/CpcvTests.cs`
    - C(6,2) produces 15 combinations
    - Single-trade result → graceful empty output
    - Deterministic with same seed
    - `ProbabilityOfOverfitting = 1.0` when all OOS Sharpes negative
    - Validation: `NumPaths < 3` throws, `TestFolds >= NumPaths` throws
    - _Requirements: 22.1, 22.2, 22.7, 22.9, 22.10, 28.1_

  - [x]* 7.12 Write unit tests for BarsToHumanDuration (UnitTests)
    - Create `UnitTests/V6/BarsToHumanDurationTests.cs`
    - Known bar counts at each timeframe produce correct human-readable strings
    - Unknown timeframe returns "{bars} bars" fallback
    - _Requirements: 24.1, 24.2, 28.1_

  - [x]* 7.13 Write property test: CpcvCombinationCount (UnitTests)
    - **Property 11: CPCV Combination Count**
    - For valid N ≥ 3 and 1 ≤ k < N, produces exactly C(N,k) combinations
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 22.1, 22.2**

  - [x]* 7.14 Write property test: CpcvPerCombinationOverfitting (UnitTests)
    - **Property 16: CPCV Per-Combination Overfitting**
    - ProbabilityOfOverfitting equals count of combinations where OOS < IS for that same combination, divided by total
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirements 22.3, 22.4**

  - [x]* 7.15 Write property test: CpcvDeterminism (UnitTests)
    - **Property 17: CPCV Determinism**
    - Same seed + inputs → identical CpcvResult
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirement 22.7**

  - [x]* 7.16 Write property test: BarsToHumanDurationFormatting (UnitTests)
    - **Property 20: BarsToHumanDuration Formatting**
    - Positive bar count + known timeframe → string matching "~{tradingDays} trading days of {label} data required"
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirement 24.1**

  - [x]* 7.17 Write property test: ConfidenceLevelThresholds (UnitTests)
    - **Property 21: Confidence Level Thresholds**
    - N completed items out of 9: HIGH when N ≥ 8, MEDIUM when 5 ≤ N < 8, LOW when N < 5
    - Minimum 100 iterations via `[Property(MaxTest = 100)]`
    - **Validates: Requirement 23.2**

- [x] 8. Checkpoint 4 — Final
  - Ensure all V1–V6 tests pass
  - Ensure all new Track 4 unit and property tests pass
  - Verify `ConfidenceLevel == "HIGH"` is reachable with 8/9 items
  - Verify CPCV study can be launched from the Research tab
  - Verify `BenchmarkExcessSharpe` chip shows real data
  - Ask the user if questions arise

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation per track
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- Task 1.1 (RetirementNote on StrategyIdentity) is intentionally placed first in Track 1 so that all downstream consumers in Tracks 2–4 can read it
- Task 3.3 SaveAsync writes JSON first, then upserts SQLite index — not atomic; crash recovery handled by InitializeAsync rebuild
- Parameter sweep parallelism (Task 3.6) has no determinism requirement since sweeps are embarrassingly parallel with no seed-dependent state
