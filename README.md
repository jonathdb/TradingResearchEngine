# TradingResearchEngine

An event-driven backtesting engine for quantitative strategy research, built with .NET 8 / C# 12.

## Architecture

```
TradingResearchEngine.sln
src/
  TradingResearchEngine.Core           — domain abstractions, event types, engine, portfolio, metrics
  TradingResearchEngine.Application    — use cases, research workflows, prop-firm module, risk/execution
  TradingResearchEngine.Infrastructure — CSV/HTTP data providers, JSON persistence, reporters
  TradingResearchEngine.Cli            — argument-driven + interactive CLI host
  TradingResearchEngine.Api            — ASP.NET Core minimal API host
  TradingResearchEngine.Web            — Blazor Server UI host (MudBlazor)
  TradingResearchEngine.UnitTests      — xUnit + FsCheck property tests
  TradingResearchEngine.IntegrationTests — end-to-end and infrastructure tests
```

Dependency rule: `Core ← Application ← Infrastructure ← { Cli, Api, Web }`

## Module Boundaries

- **Core Engine**: event hierarchy, heartbeat loop, dispatch table, portfolio, metrics (Sharpe, Sortino, Calmar, RoMaD, equity curve smoothness, average holding period, and more). V4 adds `FailureDetail`, `DeflatedSharpeRatio`, and `TrialCount` to `BacktestResult` for failure diagnostics and overfitting awareness. V5 adds `RealismAdvisories` to `BacktestResult`, `MaxFillPercentOfVolume` to `ExecutionOptions`, `Direction.Short` with `LongOnlyGuard` (short execution deferred to V6), `ScenarioConfig` sub-object decomposition (`DataConfig`, `StrategyConfig`, `RiskConfig`, `ExecutionConfig`, `ResearchConfig`) with backward-compatible `Effective*` computed properties, and `PresetId`/`DataFileIdentity` on `ExperimentMetadata`.
- **Application — Product Domain (V3+)**: strategy identity and versioning (`StrategyIdentity`, `StrategyVersion`), study records (`StudyRecord`), strategy templates (`StrategyTemplate`), enriched prop firm model (`PropFirmRulePack`, `ChallengePhase`, `PhaseEvaluationResult`), persistence interfaces (`IStrategyRepository`, `IStudyRepository`). V4 adds `DevelopmentStage` enum (Hypothesis → Exploring → Optimizing → Validating → FinalTest → Retired) and a nullable `Hypothesis` field to `StrategyIdentity` for research lifecycle tracking. V4 also adds `TotalTrialsRun` and `SealedTestSet` to `StrategyVersion`, partial result fields (`IsPartial`, `CompletedCount`, `TotalCount`) to `StudyRecord`, and new `StudyType` entries (`AnchoredWalkForward`, `CombinatorialPurgedCV`, `RegimeSegmentation`). V5 adds typed strategy parameter schemas (`StrategyParameterSchema`, `IStrategySchemaProvider`, `ParameterMetaAttribute`, `SensitivityHint`), `SourceType`/`DifficultyLevel` enums, `StrategyDiffService`, `ConfigPreset`/`ConfigDraft` records, `PreflightValidator`, `ResolvedConfigService`, `BacktestJob`/`JobExecutor` for async execution, and extended `StrategyVersion` with `SourceType`, `Hypothesis`, and `ExpectedFailureMode` fields.
- **Application — V4 Services**: `DsrCalculator` (Deflated Sharpe Ratio), `MinBtlCalculator` (Minimum Backtest Length), `ResearchChecklistService` (8-item validation checklist with confidence level), `FinalValidationUseCase` (sealed test set one-time validation), `BackgroundStudyService` (singleton study lifecycle manager with progress/completion events)
- **Application — V4 Interfaces**: `IProgressReporter` (progress reporting for long-running operations), `IReportExporter` (multi-format export: Markdown, CSV trade log, CSV equity curve, JSON), `IDataFileRepository` (CRUD for `DataFileRecord` metadata), `WalkForwardMode` enum (Rolling, Anchored)
- **Application — Market Data Acquisition**: `IMarketDataProvider` (provider-agnostic download interface with `SourceName`, `GetSupportedSymbolsAsync`, `DownloadToFileAsync`), `IMarketDataImportRepository` (CRUD for import records), `MarketDataImportRecord` (persistent import job record), `MarketDataImportStatus` (Running, Completed, Failed, Cancelled), `MarketSymbolInfo` (provider symbol metadata), `CsvWriteResult` (canonical CSV output metadata), `MarketDataImportService` (singleton orchestrator: validate → download → normalize → register DataFileRecord → update import record; one-at-a-time concurrency guard, progress/completion events, startup recovery, duplicate detection, temp-file-then-rename write pattern)
- **Research Workflows**: parameter sweep, variance testing, Monte Carlo, walk-forward, parameter perturbation, randomized out-of-sample, scenario comparison, benchmark comparison
- **PropFirmModule**: challenge/instant-funding economics, rule evaluation, variance presets, multi-phase rule packs (V3)
- **Infrastructure**: CSV, HTTP, and in-memory data providers, data file discovery/metadata service, JSON file repository, console/markdown reporters, `JsonDataFileRepository` (V4), `MigrationService` (V4 — migrates orphaned pre-V4 results into the strategy model on startup), `DukascopyHelpers` (shared static helpers for Dukascopy decompression, parsing, aggregation, and CSV I/O), `DukascopyImportProvider` (Dukascopy adapter for `IMarketDataProvider`), `JsonMarketDataImportRepository` (JSON file persistence for import records). V5 adds `JsonFileRepository<T>` registrations for `BacktestJob`, `ConfigDraft`, and `ConfigPreset` persistence.

## Built-in Strategies

Strategies are discovered via the `[StrategyName]` registry. Use the name in `ScenarioConfig.StrategyType`.

| Name | Class | Description |
|---|---|---|
| `volatility-scaled-trend` | `VolatilityScaledTrendStrategy` | Trend following with fast/slow SMA crossover gated by ATR warmup |
| `zscore-mean-reversion` | `ZScoreMeanReversionStrategy` | Z-score mean reversion: buys when z < -threshold, exits on reversion to mean |
| `donchian-breakout` | `DonchianBreakoutStrategy` | Long-only Donchian Channel breakout with lagged bands to avoid lookahead bias |
| `stationary-mean-reversion` | `StationaryMeanReversionStrategy` | Z-score mean reversion with ADF stationarity filter; exits on regime change |
| `macro-regime-rotation` | `MacroRegimeRotationStrategy` | Multi-regime rotation using volatility, trend, and momentum indicators; rebalances monthly with 4-tier allocation |
| `baseline-buy-and-hold` | `BaselineBuyAndHoldStrategy` | Passive buy-and-hold benchmark for comparing active strategy performance |

## Getting Started

```bash
dotnet build
dotnet test
dotnet run --project src/TradingResearchEngine.Cli -- --scenario path/to/scenario.json
dotnet run --project src/TradingResearchEngine.Web
```

## API Endpoints

| Method | Path | Description |
|---|---|---|
| POST | /scenarios/run | Run a single backtest |
| POST | /scenarios/sweep | Parameter sweep |
| POST | /scenarios/montecarlo | Monte Carlo simulation |
| POST | /scenarios/walkforward | Walk-forward analysis |

## Product Goals

- Reproducible, parameterised backtesting via ScenarioConfig JSON files
- Research workflows as first-class capabilities
- Prop-firm evaluation without modifying core engine
- Clean architecture with enforced layer boundaries
- V3: strategy identity and versioning — name, version, and track strategies as persistent research concepts
- V3: studies as first-class entities linking research workflows to strategy versions
- V3: enriched prop firm model with multi-phase challenge rules and per-rule pass/near-breach/fail evaluation
- V3: strategy templates for guided creation from pre-built starting points
- V4: research lifecycle tracking with DevelopmentStage and hypothesis fields
- V4: Deflated Sharpe Ratio and trial count tracking for overfitting awareness
- V4: sealed test set enforcement and final validation workflow
- V4: background study service for long-running study lifecycle management
- V4: research checklist with 8-item validation and confidence level scoring
- V4: multi-format export (Markdown, CSV, JSON) via IReportExporter
- V4: data file registration and validation via DataFileRecord and IDataFileRepository
- V4: explicit `Timeframe` label on `ScenarioConfig` for human-readable timeframe tagging
- V5 (in progress): `ScenarioConfig` sub-object decomposition (`DataConfig`, `StrategyConfig`, `RiskConfig`, `ExecutionConfig`, `ResearchConfig`) with backward-compatible adapter and `Effective*` computed properties
- V5 (in progress): `Direction.Short` enum value with `LongOnlyGuard` runtime safety net (short execution deferred to V6)
- V5 (in progress): execution realism enhancements (`MaxFillPercentOfVolume`, `RealismAdvisories`)
- V5 (in progress): typed strategy parameter schemas (`StrategyParameterSchema`, `IStrategySchemaProvider`, `[ParameterMeta]` attribute, `SensitivityHint` enum) for builder UI, API discovery, and parameter validation
- Market Data Acquisition: standalone workflow for downloading, normalizing, and registering historical candles from external providers (Dukascopy first) as validated Data Files
