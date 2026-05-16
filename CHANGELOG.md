# Changelog

All notable changes to TradingResearchEngine are documented in this file, organized chronologically by version.

---

## V1 — Foundation

**Scope: Event-driven backtesting engine for quantitative strategy research.**

- Bar-level and tick-level replay via a heartbeat loop
- Pluggable strategy, risk, slippage, and commission components
- Research workflows: parameter sweep, variance testing, Monte Carlo, walk-forward
- Prop-firm challenge and instant-funding economics modelling
- CLI host and ASP.NET Core minimal API host (removed in Web-Only UX Overhaul — now Web-only)
- CSV and HTTP REST data providers
- JSON file persistence
- Console and Markdown reporting

**Core Engine**: event hierarchy, heartbeat loop, dispatch table, portfolio, metrics (Sharpe, Sortino, Calmar, RoMaD, equity curve smoothness, average holding period, Historical VaR, Historical CVaR, Omega Ratio, Ulcer Index)

**Product Goals**:
- Reproducible, parameterised backtesting via ScenarioConfig JSON files
- Research workflows as first-class capabilities
- Prop-firm evaluation without modifying core engine
- Clean architecture with enforced layer boundaries

---

## V2 — Engine Correctness Overhaul

**Scope: Engine-only correctness fixes. UI rework deferred to V3.**

- Eliminated look-ahead bias: pending-order queue with 4-step per-bar processing (BUG-01)
- Sharpe/Sortino computed from equity curve period returns with configurable BarsPerYear (BUG-02)
- Continuous mark-to-market on every bar with enriched EquityCurvePoint (BUG-03)
- Direction.Short removed; long-only V2 scope (BUG-04)
- Monte Carlo resamples normalised ReturnOnRisk, multiplicative path reconstruction (BUG-05)
- O(1) rolling SMA in all strategies (IMP-01)
- ADF stationarity test cached with recheck interval (IMP-02)
- K-Ratio replaces R² smoothness (IMP-03)
- Bid/ask-aware tick fills (IMP-04)
- Intra-bar limit, stop-market, and stop-limit fill logic (IMP-05)
- FillMode (NextBarOpen default) and BarsPerYear (252 default) on ScenarioConfig
- ClosedTrade.ReturnOnRisk computed property
- V2 regression unit tests for all bug fixes

---

## V2.1 — Execution Realism, Research Robustness, and Engine Maturity

**Scope: Still engine-only. UI rework remains V3.**

- Execution realism profiles (FastResearch, StandardBacktest, BrokerConservative)
- ExecutionResult as canonical IExecutionHandler return type with partial fill support
- Advanced slippage models: ATR-scaled, percent-of-price, session-aware, volatility-bucket
- Session calendar support (ISessionCalendar, ForexSessionCalendar, UsEquitySessionCalendar)
- IPositionSizingPolicy with 4 implementations; DefaultRiskLayer delegates sizing
- Configurable portfolio constraints (max positions, max capital per symbol, max gross exposure)
- Walk-forward upgrade: composite OOS equity curve, parameter drift score
- Parameter stability workflow with fragility scoring
- Sensitivity analysis workflow (cost and delay perturbations)
- Realism sensitivity workflow (same strategy across 3 profiles)
- Regime segmentation (volatility, trend, session)
- ExperimentMetadata on BacktestResult for reproducibility
- Optional event trace mode (zero overhead when disabled)
- Extended analytics: recovery factor, longest flat period
- Strategy comparison workflow under matched assumptions

---

## V3 — Product & UX

**Scope: Transforms the engine into a user-facing research product. Single-user, local/single-tenant.**

- Strategy identity model: StrategyIdentity, StrategyVersion, StudyRecord as persistent Application-layer concepts
- Strategy templates: 6 pre-built templates for all built-in strategies
- Guided strategy builder: 5-step wizard (Template → Market → Rules → Execution → Save) with advanced mode toggle
- Strategy library: browse, version, and manage strategies with linked runs and studies
- Research explorer: browse and launch studies from strategy context
- Prop firm rule packs: PropFirmRulePack with multi-phase ChallengePhase support
- Pre-built firm packs: FTMO 100k, MyFundedFX 200k, TopStep 100k, The5ers 60k
- Phase-by-phase evaluation with pass/near-breach/fail status and margin display
- Robustness warnings: automatic badges for suspicious metrics (Sharpe > 3, trades < 30, K-Ratio < 0, etc.)
- Multi-format export: Markdown report, CSV trade log, JSON result
- Failed/cancelled run banners with Edit & Retry action
- JSON-based persistence: JsonStrategyRepository, JsonStudyRepository, SettingsService
- DataFileService for CSV discovery, validation, and preview

**Product Goals**:
- Strategy identity and versioning — name, version, and track strategies as persistent research concepts
- Studies as first-class entities linking research workflows to strategy versions
- Enriched prop firm model with multi-phase challenge rules and per-rule pass/near-breach/fail evaluation
- Strategy templates for guided creation from pre-built starting points

---

## V4 — Research Lifecycle and Overfitting Awareness

**Core Engine additions**:
- `FailureDetail`, `DeflatedSharpeRatio`, and `TrialCount` on `BacktestResult` for failure diagnostics and overfitting awareness

**Application — Product Domain additions**:
- `DevelopmentStage` enum (Hypothesis → Exploring → Optimizing → Validating → FinalTest → Retired) and nullable `Hypothesis` field on `StrategyIdentity` for research lifecycle tracking
- `TotalTrialsRun` and `SealedTestSet` on `StrategyVersion`
- Partial result fields (`IsPartial`, `CompletedCount`, `TotalCount`) on `StudyRecord`
- New `StudyType` entries: `AnchoredWalkForward`, `CombinatorialPurgedCV`, `RegimeSegmentation`

**Application — V4 Services**:
- `DsrCalculator` (Deflated Sharpe Ratio)
- `MinBtlCalculator` (Minimum Backtest Length)
- `ResearchChecklistService` (8-item validation checklist with confidence level)
- `FinalValidationUseCase` (sealed test set one-time validation)
- `BackgroundStudyService` (singleton study lifecycle manager with progress/completion events)

**Application — V4 Interfaces**:
- `IProgressReporter` (progress reporting for long-running operations)
- `IReportExporter` (multi-format export: Markdown, CSV trade log, CSV equity curve, JSON)
- `IDataFileRepository` (CRUD for `DataFileRecord` metadata)
- `WalkForwardMode` enum (Rolling, Anchored)

**Infrastructure additions**:
- `JsonDataFileRepository`
- `MigrationService` (migrates orphaned pre-V4 results into the strategy model on startup)

**Product Goals**:
- Research lifecycle tracking with DevelopmentStage and hypothesis fields
- Deflated Sharpe Ratio and trial count tracking for overfitting awareness
- Sealed test set enforcement and final validation workflow
- Background study service for long-running study lifecycle management
- Research checklist with 8-item validation and confidence level scoring
- Multi-format export (Markdown, CSV, JSON) via IReportExporter
- Data file registration and validation via DataFileRecord and IDataFileRepository
- Explicit `Timeframe` label on `ScenarioConfig` for human-readable timeframe tagging

---

## V5 — Config Decomposition, Parameter Schemas, and Realism Enhancements

**Core Engine additions**:
- `RealismAdvisories` on `BacktestResult`
- `MaxFillPercentOfVolume` on `ExecutionOptions`
- `Direction.Short` with `LongOnlyGuard` (now obsolete — removed in V6); short execution deferred to V6
- `ScenarioConfig` sub-object decomposition (`DataConfig`, `StrategyConfig`, `RiskConfig`, `ExecutionConfig`, `ResearchConfig`) with backward-compatible `Effective*` computed properties
- `PresetId`/`DataFileIdentity` on `ExperimentMetadata`

**Application — Product Domain additions**:
- Typed strategy parameter schemas (`StrategyParameterSchema`, `IStrategySchemaProvider`, `ParameterMetaAttribute`, `SensitivityHint`)
- `SourceType`/`DifficultyLevel` enums
- `StrategyDiffService`
- `ConfigPreset`/`ConfigDraft` records
- `PreflightValidator`, `ResolvedConfigService`
- `BacktestJob`/`JobExecutor` for async execution
- Extended `StrategyVersion` with `SourceType`, `Hypothesis`, and `ExpectedFailureMode` fields

**Infrastructure additions**:
- `JsonFileRepository<T>` registrations for `BacktestJob`, `ConfigDraft`, and `ConfigPreset` persistence

**Product Goals**:
- `ScenarioConfig` sub-object decomposition with backward-compatible adapter and `Effective*` computed properties
- `Direction.Short` enum value (short execution deferred to V6; `LongOnlyGuard` now obsolete)
- Execution realism enhancements (`MaxFillPercentOfVolume`, `RealismAdvisories`)
- Typed strategy parameter schemas (`StrategyParameterSchema`, `IStrategySchemaProvider`, `[ParameterMeta]` attribute, `SensitivityHint` enum) for builder UI, API discovery, and parameter validation

---

## V6 — Engine Upgrades (Long/Short, SQLite, Charting, Quant Depth)

**Scope: Four tracks of improvements.**

**Core Engine additions**:
- `BarsPerYearDefaults` (all 8 intraday timeframes M1 through D1) with human-readable duration conversion
- `AllowReversals` on `ExecutionConfig`
- Full short position tracking in `Portfolio`

**Infrastructure additions**:
- `SqliteIndexRepository<T>` — index-only SQLite layer over existing JSON files providing O(log n) lookups by strategy version and strategy ID, implementing `IBacktestResultRepository`

**Tracks**:
- Full long/short bidirectional execution — bidirectional strategies, short position tracking, signed quantity sizing
- SQLite index persistence over JSON files for O(log n) lookups; parallel walk-forward and parameter sweep execution via `Parallel.ForEachAsync` with `SemaphoreSlim` concurrency control
- Plotly.Blazor interactive charting: equity curve, monthly returns heatmap, trade PnL histogram, holding period histogram, Monte Carlo fan chart, walk-forward composite chart, parameter sweep heatmap
- Quant depth: CPCV (Combinatorial Purged Cross-Validation) implementation, prop-firm evaluation persistence wiring, IPropFirmPackLoader DI service, benchmark excess Sharpe wiring, timeframe-aware MinBTL recommendations, 9-item research checklist with updated confidence thresholds
- Strategy retirement with "Show Retired" toggle and optional retirement notes
- Intraday data caching for aggregated timeframes in DukascopyDataProvider

---

## V7 — Background Study Dispatch

**Application — Product Domain additions**:
- `StudyType` entries: `BenchmarkComparison`, `Variance`, `RandomisedOos` for background study dispatch

---

## V8 — AI Strategy Builder, Export, Paper Trading, and Portfolio Backtesting

**Infrastructure additions**:
- `GeminiStrategyAssistant` (Google Gemini AI client via `Mscc.GenerativeAI` for natural-language strategy generation)
- `MQL4StrategyExporter`/`MQL5StrategyExporter`/`PineScriptExporter` (strategy code export to external platforms)
- `PollingStreamingDataProvider` (polling-based streaming data for paper trading)

**Product Goals**:
- AI Strategy Builder with Google Gemini integration for natural-language strategy generation and iterative refinement
- Strategy export to MQL4, MQL5, and PineScript for deployment on external platforms
- Paper trading mode with simulated live execution reusing the backtest pipeline
- Shared indicator library backed by Skender.Stock.Indicators with 8 standard wrappers (SMA, EMA, RSI, MACD, Bollinger Bands, ATR, Stochastic, Donchian)
- Parallel multi-symbol portfolio backtesting with correlation analysis and equity curve merging
- BenchmarkDotNet performance suite with object pooling for hot-path allocation reduction

---

## Market Data Acquisition

**Application additions**:
- `IMarketDataProvider` (provider-agnostic download interface with `SourceName`, `GetSupportedSymbolsAsync`, `DownloadToFileAsync`)
- `IMarketDataImportRepository` (CRUD for import records)
- `MarketDataImportRecord` (persistent import job record)
- `MarketDataImportStatus` (Running, Completed, Failed, Cancelled)
- `MarketSymbolInfo` (provider symbol metadata)
- `CsvWriteResult` (canonical CSV output metadata)
- `MarketDataImportService` (singleton orchestrator: validate → download → normalize → register DataFileRecord → update import record; one-at-a-time concurrency guard, progress/completion events, startup recovery, duplicate detection, temp-file-then-rename write pattern)

**Infrastructure additions**:
- `DukascopyHelpers` (shared static helpers for Dukascopy decompression, parsing, aggregation, and CSV I/O)
- `DukascopyImportProvider` (Dukascopy adapter for `IMarketDataProvider`)
- `JsonMarketDataImportRepository` (JSON file persistence for import records)

**Product Goal**: Standalone workflow for downloading, normalizing, and registering historical candles from external providers (Dukascopy first) as validated Data Files.


---

## PR Gate Implementation — Gates 1–10

**Scope: Walk-forward correctness, indicator fixes, performance & concurrency, configuration canonicalization, persistence & resilience, repository cleanup, research analytics expansion, engine capability expansion, code quality & async correctness, research depth & developer experience.**

### Added

- `OptimizationObjective` enum (Sharpe, CAGR, MAR) and `ParameterGrid`/`ParameterRange` records
- `GridOptimizer` with objective-based ranking and structured `ExcludedCandidate` explanations
- Walk-forward in-sample grid optimization in `WalkForwardWorkflow`
- Walk-forward pre-run validation in `PreflightValidator` (minimum data range, window count, statistical significance warning)
- `ConcurrencyBudget` with `SemaphoreSlim` and `IOptions<ConcurrencyOptions>` (global concurrency control)
- Provider-aware progress estimation via `IDataProvider.EstimateBarCountAsync`
- `ScenarioConfigNormalizer` for legacy-to-canonical config transformation (no disk modification on load)
- Typed provider configuration (`CsvDataProviderOptions`, `HttpDataProviderOptions`, `DukascopyDataProviderOptions`) via `IOptions<T>`
- AI call timeout and cancellation in `GeminiClient` with linked `CancellationTokenSource`
- Job retry policy with configurable backoff, `Retrying` status, and terminal failure handling
- `ConsistencyReconciler` for SQLite/JSON reconciliation (JSON as source of truth)
- Configurable paper-trading polling via `IOptionsMonitor<PaperTradingOptions>` with hot-reload
- `MonteCarloSimulationMode` enum with `TradeResample`, `BlockBootstrap`, `ReturnSeries` modes
- `WalkForwardAnalytics` record: OOS profitability rate, concatenated OOS equity curve, parameter drift score
- `TradeAnatomy` record with MAE/MFE/Duration on `ClosedTrade`
- `MaxAdverseExcursion` and `MaxFavorableExcursion` fields on `ClosedTrade` for edge ratio and R-multiple analysis
- Correlation-aware portfolio constraints via `CorrelationConstraintEnforcer` integrated into `DefaultRiskLayer`
- `ComparisonReportGenerator` for persistent Markdown (and optional HTML) comparison reports
- AI refinement loop with backtest context in `GeminiStrategyAssistant.RefineStrategyAsync`
- Large sweep result virtualization via Blazor `Virtualize` component
- Multi-timeframe strategy support: `IMultiTimeframeStrategy`, `SecondaryTimeframeConfig`, `MultiTimeframeDataHandler`
- `ExportValidator` for Pine Script and MQL structural correctness validation
- Expression compiler negative test coverage (malformed inputs produce descriptive `ExpressionCompileError`)
- `MaxPromptLength` guard on `GeminiStrategyAssistant` (default 30000 chars)
- `ExportComparisonMarkdownAsync` on `IReportExporter` for persisting comparison reports
- Multi-criteria ranking in `ScenarioComparisonUseCase` with `ComparisonFilter` (MinWinRate, MinTrades, MaxDrawdown, sort key)
- Strategy version side-by-side comparison with metric deltas
- `DataProviderConfig` discriminated union (`CsvDataProviderConfig | HttpDataProviderConfig | DukascopyDataProviderConfig`) replacing `Dictionary<string, object>`
- End-to-end integration test for walk-forward → OOS → persist cycle
- Observable job queue depth metrics via `IJobQueueMetrics` (PendingCount, RunningCount, FailedCount)
- Architecture dependency enforcement test using `NetArchTest.Rules`
- `BacktestResult.Tags` — user-assigned tags for filtering and annotation (V9)
- `BacktestResult.Notes` — free-text user notes attached to runs (V9)
- `BacktestResult.CreatedAt` — explicit creation timestamp replacing RunId prefix parsing (V9)
- `BacktestResult.CompletedAt` — timestamp when the run completed or failed (V9)
- Concatenated OOS equity curve as computed property on `WalkForwardResult`
- OOS profitability rate on `WalkForwardSummary`

### Changed

- Walk-forward workflow now performs real in-sample parameter optimization with grid support
- Research checklist surfaced as active workflow guide with navigation paths and low-confidence explanations
- Final validation requires explicit user confirmation before consuming the test set
- Portfolio hot-path optimized with cached snapshots and O(1) `OpenPositionCount`
- Monte Carlo workflow parallelized with deterministic seeding and bounded concurrency
- CPCV workflow parallelized with fold isolation and order-independent aggregation
- Parameter Perturbation workflow parallelized with deterministic jitter
- Strategy construction unified through `StrategyRegistry` with startup `VerifyAll()`
- `OptimizationObjective.CAGR` renamed/corrected to compute true annualised CAGR (was total return)
- `ReportExporter` file I/O replaced with async `File.WriteAllTextAsync` throughout
- `StrategyRegistry` default parameter inference migrated from reflection to attribute-based schema
- Comparison page consolidated to single canonical route
- Indicator catalog audited for completeness with startup validation
- Beginner-mode strategy builder defaults to `StandardBacktest` realism profile

### Fixed

- `GridOptimizer.ComputeCagr` now computes true annualised CAGR instead of total return
- Synchronous `File.WriteAllText` calls in `ReportExporter` replaced with async equivalents
- `ComparisonReportGenerator` output now persisted via `IReportExporter` (was generated but never saved)
- Commented-out metrics (VaR95, CVaR95, OmegaRatio, UlcerIndex) resolved — restored in `MetricsCalculator`

### Deprecated

- `LongOnlyGuard` marked `[Obsolete]` — V6+ supports full bidirectional execution via `Direction` enum

---

### Gate 1 — Walk-Forward Correctness
- `OptimizationObjective` enum (Sharpe, CAGR, MAR) and `ParameterGrid`/`ParameterRange` records
- `GridOptimizer` with objective-based ranking and structured `ExcludedCandidate` explanations
- Walk-forward in-sample grid optimization in `WalkForwardWorkflow`
- Walk-forward pre-run validation in `PreflightValidator` (minimum data range, window count, statistical significance warning)

### Gate 2 — Indicator Fixes & Validation
- Final validation confirmation gate in `FinalValidationUseCase` (explicit confirmation, cancellation, already-consumed guard)
- Research checklist as active workflow guide with navigation paths and low-confidence explanations
- Indicator catalog completeness audit with startup validation
- `LongOnlyGuard` marked `[Obsolete]` with documentation cleanup
- Beginner-mode realism defaults (`StandardBacktest` profile)

### Gate 3 — Performance & Concurrency
- `ConcurrencyBudget` with `SemaphoreSlim` and `IOptions<ConcurrencyOptions>` (global concurrency control)
- Portfolio hot-path optimization with cached snapshots and O(1) `OpenPositionCount`
- Parallel Monte Carlo workflow with deterministic seeding
- Parallel CPCV workflow with fold isolation
- Parallel Parameter Perturbation workflow with deterministic jitter
- Provider-aware progress estimation via `IDataProvider.EstimateBarCountAsync`

### Gate 4 — Configuration & Construction
- `ScenarioConfigNormalizer` for legacy-to-canonical config transformation (no disk modification on load)
- Unified strategy construction through `StrategyRegistry` with startup `VerifyAll()`
- Typed provider configuration (`CsvDataProviderOptions`, `HttpDataProviderOptions`, `DukascopyDataProviderOptions`) via `IOptions<T>`

### Gate 5 — Persistence & Resilience
- AI call timeout and cancellation in `GeminiClient` with linked `CancellationTokenSource`
- Job retry policy with configurable backoff, `Retrying` status, and terminal failure handling
- `ConsistencyReconciler` for SQLite/JSON reconciliation (JSON as source of truth)
- Configurable paper-trading polling via `IOptionsMonitor<PaperTradingOptions>` with hot-reload

### Gate 6 — Repository Cleanup
- Prompt directory audit: archival artifacts relocated, production prompts retained
- Obsolete CLI/API transition leftovers removed
- Documentation and spec alignment with implemented reality

### Gate 7 — Research Analytics Expansion
- `MonteCarloSimulationMode` enum with `TradeResample`, `BlockBootstrap`, `ReturnSeries` modes
- `WalkForwardAnalytics` record: OOS profitability rate, concatenated OOS equity curve, parameter drift score
- `TradeAnatomy` record with MAE/MFE/Duration on `ClosedTrade`
- Correlation-aware portfolio constraints via `CorrelationConstraintEnforcer` in `DefaultRiskLayer`
- `ComparisonReportGenerator` for persistent Markdown and optional HTML comparison reports

### Gate 8 — Engine Capability Expansion
- AI refinement loop with backtest context in `GeminiStrategyAssistant.RefineStrategyAsync`
- Large sweep result virtualization via Blazor `Virtualize` component
- Consolidated comparison page (single canonical route)
- Multi-timeframe strategy support: `IMultiTimeframeStrategy`, `SecondaryTimeframeConfig`, `MultiTimeframeDataHandler`
- `ExportValidator` for Pine Script and MQL structural correctness validation
- Expression compiler negative test coverage (malformed inputs → descriptive `ExpressionCompileError`)
- Reference multi-timeframe strategy implementation

### Gate 9 — Code Quality & Async Correctness
- `OptimizationObjective.CAGR` corrected to compute true annualised CAGR
- `ReportExporter` migrated to async file I/O (`File.WriteAllTextAsync`)
- `MaxPromptLength` guard on `GeminiStrategyAssistant` (default 30000 chars)
- Commented-out metrics (VaR95, CVaR95, OmegaRatio, UlcerIndex) resolved in `MetricsCalculator`
- `ExportComparisonMarkdownAsync` added to `IReportExporter`
- `StrategyRegistry` parameter inference migrated to attribute-based schema

### Gate 10 — Research Depth & Developer Experience
- `MaxAdverseExcursion` and `MaxFavorableExcursion` on `ClosedTrade` with engine tracking
- Concatenated OOS equity curve on `WalkForwardResult`
- OOS profitability rate on `WalkForwardSummary`
- Multi-criteria ranking in `ScenarioComparisonUseCase` with `ComparisonFilter`
- Strategy version side-by-side comparison with metric deltas
- `DataProviderConfig` discriminated union replacing `Dictionary<string, object>`
- End-to-end walk-forward integration test
- Observable job queue depth metrics (`IJobQueueMetrics`)
- Architecture dependency enforcement test (`NetArchTest.Rules`)
- `BacktestResult.Tags`, `BacktestResult.Notes`, `BacktestResult.CreatedAt`, `BacktestResult.CompletedAt` (V9 additions)
