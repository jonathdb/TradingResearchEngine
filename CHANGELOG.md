# Changelog

All notable changes to TradingResearchEngine are documented in this file, organized chronologically by version.

---

## V1 — Foundation

**Scope: Event-driven backtesting engine for quantitative strategy research.**

- Bar-level and tick-level replay via a heartbeat loop
- Pluggable strategy, risk, slippage, and commission components
- Research workflows: parameter sweep, variance testing, Monte Carlo, walk-forward
- Prop-firm challenge and instant-funding economics modelling
- CLI host (argument-driven + interactive) and ASP.NET Core minimal API host
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
- `Direction.Short` with `LongOnlyGuard` (removed in V6); short execution deferred to V6
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
- `Direction.Short` enum value with `LongOnlyGuard` runtime safety net (short execution deferred to V6)
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
- Full long/short execution replacing the V5 `LongOnlyGuard` — bidirectional strategies, short position tracking, signed quantity sizing
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
