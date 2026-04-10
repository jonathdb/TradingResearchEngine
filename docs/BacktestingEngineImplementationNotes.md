# Backtesting Engine — Implementation Notes

## Heartbeat Loop

The engine uses two nested loops:

```
while (dataHandler.HasMore)
    dataHandler.EmitNext(queue)    // enqueues BarEvent or TickEvent
    while (queue.TryDequeue(out evt))
        Dispatch(evt)              // routes to handler, may enqueue more events
```

The outer loop advances one data step. The inner loop drains all events before the next step.

## Dispatch Table

| Event Type | Handler | Output |
|---|---|---|
| MarketDataEvent | IStrategy.OnMarketData | SignalEvent / OrderEvent |
| SignalEvent | IRiskLayer.ConvertSignal | OrderEvent (risk-approved) |
| OrderEvent (raw) | IRiskLayer.EvaluateOrder | OrderEvent (risk-approved) |
| OrderEvent (approved) | IExecutionHandler.Execute | FillEvent |
| FillEvent | Portfolio.Update | Position + cash update (no equity curve append) |

## OrderEvent Fields

`OrderEvent` carries the following fields:

| Field | Type | Default | Description |
|---|---|---|---|
| Symbol | string | — | Instrument identifier |
| Direction | Direction | — | `Long` or `Flat` |
| Quantity | decimal | — | Order size |
| OrderType | OrderType | — | `Market`, `Limit`, `StopMarket`, or `StopLimit` |
| LimitPrice | decimal? | null | Limit price for `Limit` and `StopLimit` orders |
| Timestamp | DateTimeOffset | — | Event timestamp |
| RiskApproved | bool | false | Set to `true` only by the RiskLayer after evaluation |
| StopPrice | decimal? | null | Trigger price for `StopMarket` and `StopLimit` orders |
| MaxBarsPending | int | 0 | Bars before expiry; 0 = good-till-cancelled |
| StopTriggered | bool | false | Whether the stop condition has already been triggered (used by `StopLimit` conversion) |

## RiskLayer Enforcement

The `OrderEvent.RiskApproved` flag ensures no order reaches the ExecutionHandler without passing through the RiskLayer. The dispatch table pattern-matches on this flag.

### Flat Signal Handling

When `DefaultRiskLayer.ConvertSignal` receives a `SignalEvent` with `Direction.Flat`, it checks whether the portfolio holds an open position for that symbol. If a position exists (quantity > 0), it emits a closing `OrderEvent` (direction `Flat`, quantity equal to the full position size, `OrderType.Market`, `RiskApproved = true`). If no position exists, it returns `null` and no order is generated.

## Error Handling

- Strategy exceptions are caught, logged, and result in `Status = Failed`
- Unrecognised event types are logged as warnings and discarded
- Malformed data records are skipped with a counter increment
- Cash going negative triggers a `MarginBreachWarning` log and clamp to zero

## Engine Construction

`BacktestEngine` is not registered in DI. `RunScenarioUseCase` constructs it manually per run, wiring the resolved data provider, strategy, risk layer, and execution handler directly. This avoids a DI-scoped engine lifetime that would conflict with the one-queue-per-run ownership model and allows each run to receive scenario-specific component instances.

## Data Provider Resolution

`RunScenarioUseCase` resolves the `IDataProvider` based on `ScenarioConfig.DataProviderType` at run time rather than pulling a single provider from DI. This allows each scenario to specify its own data source (e.g. `csv`, `http`, `dukascopy`) and pass provider-specific settings via `DataProviderOptions`.

### CSV Relative Path Resolution

When `DataProviderType` is `csv`, the `DataProviderFactory` resolves relative file paths using a walk-up strategy. If the path is not rooted and does not exist relative to the current working directory, the factory walks up the directory tree (up to 6 levels) looking for a match. This handles the common case where the Blazor Web project runs from `src/TradingResearchEngine.Web/` but CSV paths in `ScenarioConfig` are relative to the solution root (e.g. `samples/data/spy-daily.csv`). If no match is found, the original relative path is passed through unchanged and the provider will report a file-not-found error at read time.

### DukascopyHelpers

`DukascopyHelpers` (`Infrastructure/DataProviders/DukascopyHelpers.cs`) is a shared static helper class that centralises all Dukascopy-specific logic: LZMA decompression, binary candle parsing, bar aggregation, point-size lookups, URL construction, weekend filtering, and canonical CSV read/write. Both `DukascopyDataProvider` (inline backtest) and `DukascopyImportProvider` (market data import) delegate to these helpers.

| Method | Description |
|---|---|
| `Decompress(byte[])` | Decompresses LZMA-compressed `.bi5` data using the standard 13-byte header (5-byte properties + 8-byte uncompressed size + compressed stream) |
| `ParseCandles(byte[], DateTime, string, decimal)` | Parses decompressed 24-byte binary candle records into `BarRecord` instances; skips zero-price entries |
| `Aggregate(List<BarRecord>, string, string)` | Aggregates minute bars to a target interval (5m, 15m, 30m, 1H, 4H, Daily) |
| `IntervalToMinutes(string)` | Converts an interval string to minutes; defaults to 1440 (daily) for unknown intervals |
| `BuildTradingDays(DateTime, DateTime)` | Builds a list of weekday dates in a range (skips Saturday/Sunday) |
| `BuildDayUrl(string, DateTime)` | Constructs the Dukascopy datafeed URL for a day's BID minute candles (0-indexed months) |
| `SaveToCsv(string, List<BarRecord>)` | Writes bars to canonical CSV format (`Timestamp,Open,High,Low,Close,Volume`) |
| `LoadFromCsv(string, string, string)` | Loads bars from a canonical CSV file |

`PointSizes` is a static dictionary mapping 15 supported symbols (major forex pairs, gold, silver, equity index CFDs) to their pipette divisors. Unknown symbols should default to 100,000 at the call site.

### DukascopyDataProvider

`DukascopyDataProvider` downloads historical minute-bar data from Dukascopy's free datafeed. It requires no API key. Data is served as LZMA-compressed binary files (`.bi5` format) and decoded into `BarRecord` instances. All decompression, parsing, aggregation, URL construction, and CSV I/O is delegated to `DukascopyHelpers`.

> **Note:** `DukascopyDataProvider` is no longer resolvable via `DataProviderType` in `ScenarioConfig`. It was removed from the `DataProviderFactory` switch. The class still exists in Infrastructure but must be constructed manually if needed.

The provider implements a local CSV cache keyed by symbol, interval, and date range. On each `GetBars` call it checks for a cached file first. If a cache hit is found, bars are loaded from CSV via `DukascopyHelpers.LoadFromCsv` and filtered to the requested range — no HTTP requests are made. On a cache miss, the provider downloads, aggregates, and then saves the result to CSV via `DukascopyHelpers.SaveToCsv` before yielding bars.

When downloading, the provider uses `DukascopyHelpers.BuildTradingDays` to enumerate weekdays, then downloads them in parallel batches of up to 4 concurrent HTTP requests. Each `.bi5` payload is decompressed via `DukascopyHelpers.Decompress` and parsed via `DukascopyHelpers.ParseCandles`. After all batches complete, minute bars are sorted by timestamp and aggregated via `DukascopyHelpers.Aggregate`. `CancellationToken` is checked between batches.

### YahooFinanceDataProvider

`YahooFinanceDataProvider` downloads historical bar data from Yahoo Finance using the CSV download endpoint. It requires no API key. Malformed rows are logged and skipped. Tick data is not supported — `GetTicks` is not implemented.

> **Note:** `YahooFinanceDataProvider` is no longer resolvable via `DataProviderType` in `ScenarioConfig`. It was replaced by `dukascopy` in the `DataProviderFactory` switch. The class still exists in Infrastructure but must be constructed manually if needed.

The provider delegates HTTP fetching to an internal `FetchCsvAsync` method that includes automatic retry with exponential backoff for HTTP 429 (rate-limited) responses.

### InMemoryDataProvider

`InMemoryDataProvider` serves bar and tick records from pre-loaded in-memory lists. Research workflows that partition data into non-contiguous subsets (e.g. `RandomizedOosWorkflow`) set `DataProviderType = "memory"` on the sub-config and pass the filtered bars via `DataProviderOptions["FilteredBars"]`, so the `DataProviderFactory` resolves `InMemoryDataProvider` automatically. Workflows that only adjust the date range (e.g. `WalkForwardWorkflow`) keep the original provider type and modify `From`/`To` in `DataProviderOptions`. `InMemoryDataProvider` is also useful in tests as a fast, deterministic data source. It supports construction from bars only, ticks only, or both, and respects `CancellationToken` on iteration.

## Data File Management

`DataFileService` (Infrastructure) provides CSV file discovery and metadata analysis for the UI Data Files page and any tooling that needs to enumerate available data files.

### DataFileService

- Default data directory: `%LOCALAPPDATA%/TradingResearchEngine/Data` (created automatically if absent)
- Constructor accepts an optional `dataDir` override for testing or custom deployments
- `ListFiles()` scans the data directory for `*.csv` files and also checks a `samples/data` directory relative to the working directory (walk-up resolution), deduplicating by filename
- Returns `List<DataFileInfo>` sorted by filename

### DataFileInfo

| Field | Type | Description |
|---|---|---|
| FileName | string | File name without path |
| FullPath | string | Absolute path to the file |
| FileSizeBytes | long | File size in bytes |
| RowCount | int | Number of data rows (excluding header) |
| DetectedFormat | string | Detected CSV format (e.g. Yahoo, TradingView, MetaTrader, Generic) |
| FirstTimestamp | string? | First timestamp in the file (null if empty or unparseable) |
| LastTimestamp | string? | Last timestamp in the file |
| Headers | string[] | Column headers from the first row |

`DataFileService` is not yet registered in DI — it will be wired when the Data Files page (UI Phase 7, task 34.1) is implemented.

## Market Data Acquisition (In Progress)

A standalone workflow for downloading historical candles from external providers, normalizing them into the engine's canonical CSV format, and registering them as validated Data Files. The model is: **download → normalize → approved CSV → DataFileRecord → validation → analytics consumption**. Market Data is adjacent to Data Files: it is the factory for generating approved CSVs, while Data Files remains the library for previewing, validating, and selecting those files.

### Application Layer — Domain Types (`Application/MarketData/`)

- `MarketDataImportStatus` — enum: `Running`, `Completed`, `Failed`, `Cancelled`
- `MarketDataImportRecord` — persistent record of an import job. Implements `IHasId` via `ImportId`. Fields: `ImportId`, `Source`, `Symbol`, `Timeframe`, `RequestedStart`, `RequestedEnd`, `Status`, optional `OutputFilePath`, `OutputFileId`, `DownloadedChunkCount`, `TotalChunkCount`, `ErrorDetail`, `CandleBasis` (default `"Bid"`), `CreatedAt`, `CompletedAt`
- `MarketSymbolInfo` — describes a symbol supported by a provider. Fields: `Symbol`, `DisplayName`, `SupportedTimeframes`
- `CsvWriteResult` — result of writing a canonical CSV. Fields: `FilePath`, `Symbol`, `Timeframe`, `FirstBar`, `LastBar`, `BarCount`

### Canonical CSV Schema

All imported data is normalized to: `Timestamp,Open,High,Low,Close,Volume` with ISO 8601 UTC timestamps (`Z` suffix), strictly ascending rows, positive OHLC values, and `0` for unavailable volume. This matches the existing engine format used by `CsvFormatConverter.SourceFormat.Engine`.

### Application Layer — Interfaces (`Application/MarketData/`)

- `IMarketDataProvider` — provider-agnostic download interface. Defines `SourceName` (string property), `GetSupportedSymbolsAsync` (returns `IReadOnlyList<MarketSymbolInfo>`), and `DownloadToFileAsync` (downloads and normalizes data to canonical CSV at a given output path, accepts `IProgressReporter?` and `CancellationToken`, returns `CsvWriteResult`). `requestedStart` is inclusive, `requestedEnd` is exclusive. Implementations live in Infrastructure.

### Application Layer — Persistence Interface (`Application/MarketData/`)

- `IMarketDataImportRepository` — CRUD for import records. Methods: `GetAsync`, `ListAsync`, `SaveAsync`, `DeleteAsync`.

### Infrastructure — DukascopyImportProvider (`Infrastructure/MarketData/`)

`DukascopyImportProvider` implements `IMarketDataProvider` with `SourceName = "Dukascopy"`. It downloads minute BID candles day-by-day (sequential, not batched), aggregates to the requested timeframe via `DukascopyHelpers.Aggregate`, filters to the requested range (start inclusive, end exclusive), and writes canonical CSV via `DukascopyHelpers.SaveToCsv`.

- Supports all 15 symbols from `DukascopyHelpers.PointSizes` across 7 timeframes (`1m`, `5m`, `15m`, `30m`, `1H`, `4H`, `Daily`)
- Reports progress per day chunk via `IProgressReporter`
- Retries transient HTTP failures up to 3 times with exponential backoff
- Skips weekends via `DukascopyHelpers.BuildTradingDays`
- Rejects unsupported symbols with `ArgumentException`

### Infrastructure — JsonMarketDataImportRepository (`Infrastructure/MarketData/`)

`JsonMarketDataImportRepository` persists `MarketDataImportRecord` as individual JSON files at `{baseDir}/{importId}.json`. Implements `IMarketDataImportRepository`. Constructor accepts a base directory path.

### Application Layer — MarketDataImportService (`Application/MarketData/`)

`MarketDataImportService` is a singleton orchestrator that manages the full import lifecycle: validate → download → normalize → register `DataFileRecord` → update import record. Only one import may run at a time (concurrency guard via `lock`). Implements `IDisposable`.

#### Supporting Records

- `ImportProgressUpdate(ImportId, Current, Total, Label)` — raised on each progress step
- `ImportCompletionUpdate(ImportId, Status, ErrorMessage?)` — raised when an import finishes (success, failure, or cancellation)
- `ActiveImport(ImportId, Source, Symbol, Timeframe, Current, Total, StartedAt)` — snapshot of the running import

#### Public API

| Method | Description |
|---|---|
| `StartImportAsync(source, symbol, timeframe, start, end, ct)` | Validates parameters, creates a `Running` import record, launches a background download task, returns the import ID immediately. Throws `InvalidOperationException` if an import is already running, `ArgumentException` for invalid parameters. |
| `CancelImport(importId)` | Cancels the running import if it matches the given ID. |
| `GetActiveImport()` | Returns the `ActiveImport` snapshot, or `null` if idle. |
| `FindDuplicateAsync(source, symbol, timeframe, start, end, ct)` | Checks for an existing `Completed` import with matching parameters. Returns the record if found, `null` otherwise. Used for duplicate detection and `DataFileRecord` reuse. |
| `RecoverOnStartupAsync(ct)` | Called on Web startup. Resets orphaned `Running` records to `Failed` and deletes `.tmp` files in the data directory. |

#### Events

- `OnProgress` (`Action<ImportProgressUpdate>?`) — per-step progress updates
- `OnCompleted` (`Action<ImportCompletionUpdate>?`) — completion notification

#### Background Task Flow

1. Provider's `DownloadToFileAsync` writes to a `.tmp` file, reporting progress via an internal `ServiceProgressReporter` that bridges `IProgressReporter` to the service's events.
2. On success: atomic rename (delete existing → move temp to final), create or update `DataFileRecord` (reuses `FileId` from a previous duplicate import if found), update import record to `Completed`.
3. On cancellation: delete temp file, set import to `Cancelled`.
4. On failure: delete temp file, set import to `Failed` with error detail.
5. Finally: clear `_activeImport` and dispose the `CancellationTokenSource`.

#### Startup Recovery (Web Host)

`Program.cs` calls `RecoverOnStartupAsync` on startup inside a try/catch so recovery failures don't prevent the app from starting.

### Planned Components (Not Yet Implemented)

- `MarketData.razor` — Web UI screen for configuring and monitoring imports

### Design Decisions

- Core is untouched. All new types live in Application, Infrastructure, and Web.
- Only one import job may run at a time (concurrency guard).
- Output file naming: `{source}_{symbol}_{timeframe}_{startYYYYMMDD}_{endYYYYMMDD}.csv`
- Temp-file-then-rename write pattern prevents corrupting existing files.
- On startup, orphaned `Running` records are reset to `Failed`.
- Duplicate imports reuse the existing `DataFileRecord.FileId` so the Data Files library doesn't accumulate stale entries.

## Metrics Pipeline

`MetricsCalculator` is a static utility in Core that computes all performance metrics from closed trades and the equity curve. `BacktestEngine.BuildResult` calls each method and populates `BacktestResult`.

| Metric | Input | Notes |
|---|---|---|
| MaxDrawdown | equity curve | Peak-to-trough percentage decline |
| SharpeRatio | closed trades | Annualised, risk-free rate from config |
| SortinoRatio | closed trades | Downside deviation only |
| CalmarRatio | equity curve + start/end equity | Annualised return / max drawdown; null when drawdown is zero |
| ReturnOnMaxDrawdown (RoMaD) | equity curve + start/end equity | Total return / max drawdown |
| WinRate | closed trades | Wins / total trades |
| ProfitFactor | closed trades | Gross profit / gross loss |
| AverageWin / AverageLoss | closed trades | Mean P&L of winning / losing trades |
| Expectancy | closed trades | (WinRate × AvgWin) − ((1 − WinRate) × AvgLoss) |
| AverageHoldingPeriod | closed trades | Mean duration between entry and exit |
| EquityCurveSmoothness | equity curve | R² of linear regression; 1.0 = perfectly linear |
| MaxConsecutiveWins / Losses | closed trades | Longest streak |
| DeflatedSharpeRatio | Sharpe + trial count | V4: Bailey & López de Prado 2014 adjustment for multiple testing bias; null when Sharpe or trial count unavailable |

All methods return `null` when inputs are insufficient (e.g. zero trades, fewer than 2–3 equity points).

## Equity Curve Ownership

Equity curve points are appended by `Portfolio.MarkToMarket`, not by `Portfolio.Update`. When the engine calls `MarkToMarket(symbol, price, timestamp)` on each bar, the portfolio recalculates unrealised P&L and appends an enriched `EquityCurvePoint` containing `TotalEquity`, `CashBalance`, `UnrealisedPnl`, `RealisedPnl`, and `OpenPositionCount`. `Portfolio.Update(FillEvent)` updates positions and cash but does not append to the equity curve — this separation ensures equity snapshots are driven by market prices rather than fill events.

## Benchmark Comparison Workflow

`BenchmarkComparisonWorkflow` compares a strategy's equity curve against a buy-and-hold benchmark on the same data. It implements `IResearchWorkflow<BenchmarkOptions, BenchmarkComparisonResult>`.

### Process

1. Runs the strategy via `RunScenarioUseCase`.
2. Fetches raw bar data from the same `IDataProvider` configured in the scenario.
3. Constructs a buy-and-hold equity curve: buys as many whole shares as `InitialCash` allows at the first bar's close, then tracks equity at each subsequent bar.
4. Computes strategy vs. benchmark period returns and derives alpha, beta, tracking error, and information ratio.

### Output — `BenchmarkComparisonResult`

| Field | Type | Description |
|---|---|---|
| StrategyReturn | decimal | Total return of the strategy |
| BenchmarkReturn | decimal | Total return of buy-and-hold |
| Alpha | decimal | Strategy return minus benchmark return |
| Beta | decimal? | Covariance of strategy/benchmark returns over benchmark variance; null when fewer than 2 periods or zero benchmark variance |
| InformationRatio | decimal? | Mean excess return / tracking error; null when tracking error is zero or fewer than 2 periods |
| TrackingError | decimal | Standard deviation of per-period return differences (sample variance, n−1) |
| BenchmarkEquityCurve | IReadOnlyList\<EquityCurvePoint\> | Bar-by-bar equity of the buy-and-hold portfolio |

### Configuration — `BenchmarkOptions`

| Property | Default | Description |
|---|---|---|
| InitialCash | 100,000 | Starting cash for the benchmark buy-and-hold simulation |

### Edge Cases

- Throws `InvalidOperationException` if the strategy run fails or returns no bar data.
- Beta and information ratio are null when fewer than 2 return periods are available.

## Progress Reporting

Long-running research workflows report progress via `IProgress<ProgressUpdate>`. The `ProgressUpdate` record lives in `Core.Engine` and carries:

- `CurrentStep` / `TotalSteps` — integer counters (e.g. simulation 42 of 1000)
- `Message` — human-readable status text
- `Fraction` — computed property (`CurrentStep / TotalSteps`, clamped to 0 when `TotalSteps` is zero)

`IResearchWorkflow<TOptions, TResult>` exposes an overload that accepts `IProgress<ProgressUpdate>?`. The default implementation delegates to the progress-less overload, so existing callers are unaffected. Workflow implementations that support progress (parameter sweep, Monte Carlo, walk-forward, etc.) report updates as they complete each step.

## ScenarioConfig Persistence

`ScenarioConfig` implements `IHasId` (mapping `Id` to `ScenarioId`), which makes it a valid entity for `IRepository<ScenarioConfig>`. This enables save/load/delete of scenario configurations through the same `JsonFileRepository<T>` infrastructure used for `BacktestResult`. The CLI and API can persist configs for reuse, and the planned Blazor UI relies on this for its strategy editor and saved-config workflows.

## FirmRuleSet Persistence

`FirmRuleSet` implements `IHasId` (mapping `Id` to `FirmName`), enabling CRUD via `IRepository<FirmRuleSet>` and `JsonFileRepository<T>`. This allows prop-firm rule sets to be saved, loaded, and managed through the same persistence infrastructure as `BacktestResult` and `ScenarioConfig`. The Blazor UI Rule Set Editor and the CLI/API can persist firm configurations for reuse across evaluations.

### Repository Directory Resolution

`JsonFileRepository<T>` resolves its storage directory using the following logic:

1. If `RepositoryOptions.BaseDirectory` is configured (non-empty), use that path.
2. Otherwise, fall back to `%LOCALAPPDATA%/TradingResearchEngine/{TypeName}` (e.g. `BacktestResult`, `ScenarioConfig`).

The directory is created automatically if it does not exist. This means the repository works out of the box with zero configuration — useful for first-run and local development scenarios where no `appsettings.json` is present.

## Monte Carlo Workflow

`MonteCarloWorkflow` bootstrap-resamples the closed-trade return sequence to produce a distribution of outcomes. It implements `IResearchWorkflow<MonteCarloOptions, MonteCarloResult>`.

### Process

1. Runs the base scenario via `RunScenarioUseCase` (or accepts a pre-computed `BacktestResult`).
2. Extracts the `NetPnl` from each closed trade as the return sequence.
3. For each simulation: shuffles the return sequence (with replacement), walks the equity forward from `StartEquity`, and tracks peak, drawdown, ruin, and consecutive win/loss streaks.
4. Records the full equity path per simulation (`MonteCarloPath`) and collects per-step equity values across all simulations.
5. After all simulations complete, computes P10/P50/P90 percentile bands at each trade step.

### Output — `MonteCarloResult`

| Field | Type | Description |
|---|---|---|
| P10EndEquity | decimal | 10th percentile end equity |
| P50EndEquity | decimal | 50th percentile end equity |
| P90EndEquity | decimal | 90th percentile end equity |
| RuinProbability | decimal | Fraction of simulations that hit the ruin threshold |
| MedianMaxDrawdown | decimal | Median of per-simulation max drawdown |
| EndEquityDistribution | IReadOnlyList\<decimal\> | Sorted end equity values across all simulations |
| P90MaxConsecutiveLosses | int | 90th percentile max consecutive losing trades |
| P90MaxConsecutiveWins | int | 90th percentile max consecutive winning trades |
| SampledPaths | IReadOnlyList\<MonteCarloPath\> | Full equity path for every simulation (one entry per trade step + initial equity) |
| PercentileBands | IReadOnlyList\<MonteCarloPercentileBand\> | P10/P50/P90 equity at each trade step across all simulations |

### Supporting Types

- `MonteCarloPath(IReadOnlyList<decimal> EquityValues)` — a single simulation's equity trajectory, length = trade count + 1.
- `MonteCarloPercentileBand(int Step, decimal P10, decimal P50, decimal P90)` — cross-simulation percentiles at a given trade step.

### Configuration — `MonteCarloOptions`

| Property | Description |
|---|---|
| SimulationCount | Number of bootstrap simulations (must be ≥ 1) |
| Seed | Optional RNG seed for reproducibility |
| RuinThresholdPercent | Drawdown fraction at which a simulation is marked as ruined |

### Edge Cases

- Zero trades: returns a degenerate result with the source end equity as P10/P50/P90, empty paths, and empty bands.
- `SimulationCount < 1`: throws `ArgumentException`.
- Base scenario failure: throws `InvalidOperationException` with the validation errors.

## Fill Mode and Annualisation

`ScenarioConfig` exposes two V2 fields that control execution realism and metric accuracy:

- `FillMode` (default `NextBarOpen`) — determines when risk-approved orders are filled relative to the bar that generated the signal.
  - `NextBarOpen`: orders queue as pending and fill at the next bar's Open price. This is the correct default that eliminates look-ahead bias.
  - `SameBarClose`: V1 legacy mode — orders fill immediately at the same bar's Close price. Introduces look-ahead bias; use only for backward-compatible test fixtures.
- `BarsPerYear` (default `252`) — the canonical annualisation constant used by Sharpe, Sortino, and any other metric that converts per-bar returns to annual figures. Must be positive.

Both values are defined in `Core/Configuration/` (`FillMode.cs` enum and the `ScenarioConfig` record). `RunScenarioUseCase` rejects `BarsPerYear <= 0` during validation.

## Execution Realism (V2.1)

`ScenarioConfig` exposes additional V2.1 fields for execution realism and diagnostics:

### ExecutionRealismProfile

`RealismProfile` (default `StandardBacktest`) selects a preset bundle of execution defaults. Defined in `Core/Configuration/ExecutionRealismProfile.cs`.

| Profile | Fill Mode | Slippage | Partial Fills | Notes |
|---|---|---|---|---|
| `FastResearch` | SameBarClose | Zero | Disabled | Maximum speed for parameter sweeps |
| `StandardBacktest` | NextBarOpen | Fixed spread | Disabled | Default for backtesting |
| `BrokerConservative` | NextBarOpen | ATR-scaled | Enabled | Session-aware spread widening, pessimistic stop fills |

### ExecutionOptions

`ExecutionOptions` (optional, default `null`) allows overriding individual profile defaults. Any non-null field takes precedence over the profile's default. Defined in `Core/Configuration/ExecutionOptions.cs`.

| Field | Type | Description |
|---|---|---|
| FillModeOverride | FillMode? | Overrides the profile's fill mode |
| SlippageModelOverride | string? | Overrides the profile's slippage model type |
| SlippageModelOptions | Dictionary\<string, object\>? | Model-specific slippage parameters |
| EnablePartialFills | bool? | Overrides the profile's partial fill setting |
| DefaultMaxBarsPending | int? | Overrides the profile's order expiry bar count |

The computed property `ScenarioConfig.EffectiveFillMode` resolves the active fill mode: `ExecutionOptions.FillModeOverride` takes precedence over the top-level `FillMode` field.

### SessionOptions, TraceOptions, and Timeframe

Two additional optional records and one optional string on `ScenarioConfig`:

- `SessionOptions` — configures session calendar filtering (`SessionCalendarType`, `SessionFilterOptions`). When set, the engine filters bars outside the active trading session.
- `TraceOptions` — enables event trace recording (`EnableEventTrace`). The computed property `ScenarioConfig.EnableEventTrace` resolves this.
- `Timeframe` (V4, default `null`) — an explicit timeframe label (e.g. `"Daily"`, `"H4"`, `"M15"`). When set, it provides a human-readable timeframe tag for the scenario. `null` for legacy configs that predate V4.

## Session Calendar Support

The engine supports session-aware bar filtering via the `ISessionCalendar` interface (defined in `Core/Sessions/`). When `SessionOptions` is configured on `ScenarioConfig`, the engine skips bars that fall outside tradable hours and classifies each bar into a named session bucket for regime analysis.

### Core Types

- `TradingSession` (`Core/Sessions/TradingSession.cs`) — a `readonly record struct` with `Name`, `Start` (`TimeOnly`), `End` (`TimeOnly`), and `TimeZoneId`.
- `ISessionCalendar` (`Core/Sessions/ISessionCalendar.cs`) — interface with `IsTradable(DateTimeOffset)`, `ClassifySession(DateTimeOffset)`, and `Sessions` property.

### ForexSessionCalendar

Covers the 24-hour forex market (Sunday 17:00 ET to Friday 17:00 ET). All times are UTC.

| Session | Start | End | Notes |
|---|---|---|---|
| Asia | 00:00 | 09:00 | Tokyo/Sydney |
| London | 07:00 | 16:00 | European session |
| NewYork | 12:00 | 21:00 | US session |
| Overlap | 12:00 | 16:00 | London/NewYork overlap (highest liquidity) |

- Weekends (Saturday and Sunday UTC) are not tradable.
- `ClassifySession` returns `"Overlap"` when both London and NewYork are active, otherwise the single active session name, or `"AfterHours"` for gaps between named sessions.

### UsEquitySessionCalendar

Covers US equity market hours in Eastern Time.

| Session | Start (ET) | End (ET) | Notes |
|---|---|---|---|
| PreMarket | 04:00 | 09:30 | Extended hours |
| Regular | 09:30 | 16:00 | NYSE/NASDAQ regular trading |
| AfterHours | 16:00 | 20:00 | Extended hours |

- Weekends (Saturday and Sunday) are not tradable.
- `IsTradable` returns `true` for any time within the 04:00–20:00 ET window on weekdays.
- `ClassifySession` returns `"Regular"`, `"PreMarket"`, `"AfterHours"`, or `"Closed"`.
- Timezone conversion uses `"Eastern Standard Time"` (handles DST automatically on Windows).

### ExecutionOutcome

`FillEvent` carries an `ExecutionOutcome` enum (default `Filled`) along with `RemainingQuantity` and an optional `RejectionReason`. Defined in `Core/Events/ExecutionOutcome.cs`.

| Value | Meaning |
|---|---|
| `Filled` | Order fully filled |
| `PartiallyFilled` | Partially filled; remaining quantity carried forward |
| `Unfilled` | Not filled this bar; remains in pending queue |
| `Rejected` | Rejected (session closed, insufficient capital, invalid stop) |
| `Expired` | Expired after exceeding `MaxBarsPending` |

The default execution path remains simple full fills (`Outcome = Filled`, `RemainingQuantity = 0`) unless partial fills are explicitly enabled via `ExecutionOptions`.

## V3 Product Domain Model

V3 adds a product domain model to the Application layer. Core remains untouched. All new types live in `Application/Strategy/`, `Application/Research/`, and `Application/PropFirm/`.

### Strategy Identity and Versioning

- `StrategyIdentity` (`Application/Strategy/`) — a user-owned, named research concept (e.g. "my EURUSD mean reversion idea"). Implements `IHasId` via `StrategyId`. Fields: `StrategyId`, `StrategyName`, `StrategyType`, `CreatedAt`, optional `Description`, `Stage` (`DevelopmentStage`, default `Exploring`), optional `Hypothesis`.
- `DevelopmentStage` (`Application/Strategy/`) — V4 enum tracking the research lifecycle of a strategy. Values: `Hypothesis`, `Exploring`, `Optimizing`, `Validating`, `FinalTest`, `Retired`. Existing JSON missing this field deserializes to `Exploring` for backwards compatibility.
- `StrategyVersion` (`Application/Strategy/`) — a specific parameter configuration of a strategy. Implements `IHasId` via `StrategyVersionId`. Fields: `StrategyVersionId`, `StrategyId` (parent), `VersionNumber`, `Parameters` dictionary, `BaseScenarioConfig` (full config snapshot), `CreatedAt`, optional `ChangeNote`, `TotalTrialsRun` (int, default 0, incremented per run or sweep), `SealedTestSet` (`DateRangeConstraint?`, default null, locked held-out date range).
- `IStrategyRepository` (`Application/Strategy/`) — persistence interface for strategies and versions. Methods: `GetAsync`, `ListAsync`, `SaveAsync`, `DeleteAsync`, `GetVersionsAsync`, `SaveVersionAsync`, `GetLatestVersionAsync`.

### Strategy Templates

- `StrategyDescriptor` (`Application/Strategy/`) — lightweight metadata for a built-in strategy, surfaced in the Builder and Strategy Detail UX. Fields: `StrategyType`, `DisplayName`, `Family`, `Description`, `Hypothesis`, optional `BestFor`, optional `SuggestedStudies` (string array). Lookup is by `StrategyType` match against `StrategyTemplate.StrategyType`; missing descriptor is non-fatal (UI falls back to raw type name).
- `StrategyFamily` (`Application/Strategy/`) — static class with string constants for strategy family classification: `Trend`, `MeanReversion`, `Breakout`, `RegimeAware`, `Benchmark`.
- `StrategyTemplate` (`Application/Strategy/`) — a pre-built starting point for strategy creation. Implements `IHasId` via `TemplateId`. Fields: `TemplateId`, `Name`, `Description`, `StrategyType`, `TypicalUseCase`, `DefaultParameters`, `RecommendedTimeframe`, `RecommendedProfile`, optional `Descriptor` (`StrategyDescriptor?`, default `null`).
- `DefaultStrategyTemplates.All` provides 6 built-in templates with full `StrategyDescriptor` metadata: Volatility-Scaled Trend, Z-Score Mean Reversion, Donchian Breakout, Stationary Mean Reversion, Macro Regime Rotation, Buy & Hold Baseline.

### Study Records

- `StudyRecord` (`Application/Research/`) — a research workflow execution linked to a strategy version. A Monte Carlo study with 1000 paths is ONE study, not 1000 runs. Implements `IHasId` via `StudyId`. Fields: `StudyId`, `StrategyVersionId`, `Type` (`StudyType` enum), `Status` (`StudyStatus` enum), `CreatedAt`, optional `SourceRunId`, optional `ErrorSummary`, `IsPartial` (bool, default false — true when cancelled before completion), `CompletedCount` (int, default 0 — completed units when partial), `TotalCount` (int, default 0 — total planned units).
- `StudyType` enum: `MonteCarlo`, `WalkForward`, `AnchoredWalkForward` (V4), `CombinatorialPurgedCV` (V4, deferred to V4.1), `Sensitivity`, `ParameterSweep`, `Realism`, `ParameterStability`, `RegimeSegmentation` (V4).
- `StudyStatus` enum: `Running`, `Completed`, `Failed`, `Incomplete`, `Cancelled`.
- `IStudyRepository` (`Application/Research/`) — persistence interface for study records. Methods: `GetAsync`, `ListByVersionAsync`, `ListAsync`, `SaveAsync`, `DeleteAsync`.

### Enriched Prop Firm Model

- `PropFirmRulePack` (`Application/PropFirm/`) — a specific firm's challenge rules with multi-phase support. Richer than `FirmRuleSet`. Implements `IHasId` via `RulePackId`. Fields: `RulePackId`, `FirmName`, `ChallengeName`, `AccountSizeUsd`, `Phases` (list of `ChallengePhase`), optional `PayoutSplitPercent`, `ScalingThresholdPercent`, `UnsupportedRules`, `IsBuiltIn`, `Notes`.
- `ChallengePhase` (`Application/PropFirm/`) — a single phase in a prop firm challenge (e.g. Phase 1, Phase 2, Funded). Fields: `PhaseName`, `ProfitTargetPercent`, `MaxDailyDrawdownPercent`, `MaxTotalDrawdownPercent`, `MinTradingDays`, optional `MaxTradingDays`, `ConsistencyRulePercent`, `TrailingDrawdown`.
- `PhaseEvaluationResult` (`Application/PropFirm/Results/`) — evaluation result for a single challenge phase. Fields: `PhaseName`, `Passed`, `Rules` (list of `RuleResult`).
- `RuleResult` (`Application/PropFirm/Results/`) — result of evaluating a single rule. Fields: `RuleName`, `Status` (`RuleStatus` enum), `ActualValue`, `LimitValue`, `Margin` (positive = within limit, negative = breached).
- `RuleStatus` enum: `Passed`, `NearBreach` (within 20% of limit), `Failed`.

### BacktestResult Amendment

`BacktestResult` gains optional trailing parameters after the V2 fields. All default to `null`/`null`/`null` and are backwards-compatible with existing JSON.

| Field | Type | Version | Description |
|---|---|---|---|
| `StrategyVersionId` | string? | V3 | Links the result to a `StrategyVersion` when launched from a strategy context |
| `FailureDetail` | string? | V4 | Exception message and context when `Status` is `Failed` |
| `DeflatedSharpeRatio` | decimal? | V4 | Deflated Sharpe Ratio adjusted for multiple testing bias (Bailey & López de Prado 2014) |
| `TrialCount` | int? | V4 | Snapshot of `StrategyVersion.TotalTrialsRun` at the time this run completed |

Legacy runs without these fields continue to deserialise unchanged.

### Data File Registration (V4)

- `DataFileRecord` (`Application/DataFiles/`) — metadata for a registered CSV data file. Implements `IHasId` via `FileId`. Fields: `FileId`, `FileName`, `FilePath`, `DetectedSymbol`, `DetectedTimeframe`, `FirstBar`, `LastBar`, `BarCount`, `ValidationStatus`, `ValidationError`, `AddedAt`.
- `ValidationStatus` enum: `Pending`, `Valid`, `Invalid`.
- `IDataFileRepository` (`Application/DataFiles/`) — persistence interface for data file records. Methods: `GetAsync`, `ListAsync`, `SaveAsync`, `DeleteAsync`.
- `JsonDataFileRepository` (`Infrastructure/Persistence/`) — JSON file-based implementation storing records at `datafiles/{fileId}.json`.

### Walk-Forward Mode (V4)

- `WalkForwardMode` (`Application/Research/`) — controls how the training window moves in a walk-forward study.
  - `Rolling`: training window slides forward (both start and end advance each step).
  - `Anchored`: training start stays fixed; training end advances each step, expanding the window.

`WalkForwardOptions` (`Application/Configuration/`) integrates with `WalkForwardMode` via two new properties:

| Property | Type | Description |
|---|---|---|
| `Mode` | `WalkForwardMode?` | V4: explicit walk-forward mode. When set, overrides `AnchoredWindow`. |
| `EffectiveMode` | `WalkForwardMode` | Resolved mode: prefers `Mode` if set, falls back to `AnchoredWindow` (true → Anchored, false → Rolling). |

The existing `AnchoredWindow` boolean is deprecated but retained for backwards compatibility. `InSampleLength` serves as the initial IS length in Anchored mode (the window expands from this starting size as the training end advances).

## V4 Application Services

### DsrCalculator

`DsrCalculator` (`Application/Metrics/`) computes the Deflated Sharpe Ratio following Bailey & López de Prado (2014). It adjusts the observed Sharpe for multiple testing bias: the more trials run, the more likely a high Sharpe is found by chance. Pure static function. Values below 0.95 suggest possible overfitting.

### MinBtlCalculator

`MinBtlCalculator` (`Application/Metrics/`) computes the Minimum Backtest Length — the minimum number of bars required for a Sharpe ratio observation to be statistically significant at the 95% confidence level, accounting for non-normality and multiple testing. Pure static function.

### ResearchChecklistService

`ResearchChecklistService` (`Application/Research/`) computes an 8-item research checklist for a strategy version by querying runs, studies, and evaluations. Registered as scoped in DI.

The 8 checklist items:
1. Initial Backtest — at least one completed run exists
2. Monte Carlo Robustness — a completed Monte Carlo study exists
3. Walk-Forward Validation — a completed WalkForward or AnchoredWalkForward study exists
4. Regime Sensitivity — a completed RegimeSegmentation study exists
5. Realism Impact — a completed Realism study exists
6. Parameter Surface — a completed Sensitivity or ParameterSweep study exists
7. Final Held-Out Test — strategy stage is `FinalTest`
8. Prop Firm Evaluation — (placeholder, not yet wired)

Confidence level: HIGH (≥7 passed), MEDIUM (≥4), LOW (<4).

### FinalValidationUseCase

`FinalValidationUseCase` (`Application/Engine/`) runs a single backtest against the sealed held-out test set. This is a one-time action that marks the strategy as `DevelopmentStage.FinalTest`. Registered as scoped in DI.

Process:
1. Loads the `StrategyVersion` and validates that `SealedTestSet` is configured and sealed.
2. Builds a `ScenarioConfig` scoped to the sealed date range.
3. Runs the backtest via `RunScenarioUseCase` (bypasses sealed-set guard).
4. On success, updates the parent `StrategyIdentity.Stage` to `FinalTest`.

### BackgroundStudyService

`BackgroundStudyService` (`Application/Research/`) manages background execution of long-running studies. Registered as singleton in DI — manages study lifecycle across navigations.

- `RegisterStudy` — registers a study as active and returns a `CancellationToken`.
- `ReportProgress` — reports progress for an active study, raises `OnProgress` event.
- `Complete` — marks a study as complete, removes from active tracking, raises `OnCompleted` event.
- `CancelStudy` — cancels a running study via its `CancellationTokenSource`.
- `GetActiveStudies` — returns a snapshot of all currently active studies.

Supporting types: `StudyProgressUpdate`, `StudyCompletionUpdate`, `ActiveStudy`.

The concrete Web host is responsible for actually running studies on background tasks and creating DI scopes per study execution.

### Trial Count Lifecycle in RunScenarioUseCase

`RunScenarioUseCase` enriches `BacktestResult` with trial count and DSR when the result is linked to a `StrategyVersion` (via `StrategyVersionId`):

1. Finds the parent `StrategyVersion` via `IStrategyRepository`.
2. Increments `TotalTrialsRun` for completed, failed, or cancelled-with-bars runs.
3. Snapshots the trial count into `BacktestResult.TrialCount`.
4. Computes DSR for completed runs with a non-null Sharpe, using skewness and excess kurtosis derived from equity curve period returns.

### CpcvStudyHandler (V4 Placeholder)

`CpcvStudyHandler` (`Application/Research/`) — placeholder for Combinatorial Purged Cross-Validation (CPCV). CPCV generates all combinations of training/test splits across N folds and computes the Probability of Backtest Overfitting (PBO). The `StudyType.CombinatorialPurgedCV` enum entry and PBO metric tile are scaffolded, but the full implementation is deferred to V4.1. Calling `RunAsync` throws `NotImplementedException`. The handler is registered in DI but not exposed in study creation flows in the UI.

### IProgressReporter (V4)

`IProgressReporter` (`Application/Research/`) — reports progress for long-running operations. Method: `Report(int current, int total, string label)`. Blazor implementation uses `InvokeAsync(StateHasChanged)` to push UI updates.

### IReportExporter (V4)

`IReportExporter` (`Application/Export/`) — exports a `BacktestResult` to various formats. Methods: `ExportMarkdownAsync`, `ExportTradeCsvAsync`, `ExportEquityCsvAsync`, `ExportJsonAsync`. Each returns the file path of the exported file.

## V4 Migration Service

`MigrationService` (`Infrastructure/Persistence/`) runs once on startup to migrate orphaned V2/V2.1 `BacktestResult` records into the V4 strategy model. Non-destructive: original files are untouched.

### Process

1. Checks for a `migration_v4.lock` file in `%LOCALAPPDATA%/TradingResearchEngine/`. If present, migration is skipped.
2. Queries all `BacktestResult` records and filters for orphaned results (those with a null or empty `StrategyVersionId`).
3. If no orphaned results exist, writes the lock file and returns.
4. Creates a synthetic `StrategyIdentity` (`imported-pre-v4`) and `StrategyVersion` (`imported-pre-v4-v0`) if they do not already exist.
5. Links each orphaned result by setting `StrategyVersionId = "imported-pre-v4-v0"` and re-saving.
6. Writes the lock file on success.

### Design Decisions

- Idempotent: re-running after a partial failure picks up where it left off. The lock file is only written after all results are linked.
- Failure is logged but does not throw — migration failure must not crash the app. The migration will retry on the next startup.
- The lock directory defaults to `%LOCALAPPDATA%/TradingResearchEngine/` but accepts an override via constructor for testing.
- The synthetic strategy uses `StrategyType = "imported"` and a dummy `ScenarioConfig` with zero slippage/commission.

### Startup Integration

`MigrationService.MigrateIfNeededAsync` is called during application startup (after DI is built, before the host starts accepting requests). Both the CLI and Web hosts invoke it.

## Deterministic Replay

When `ScenarioConfig.RandomSeed` is set, all RNG instances are seeded from it. Same config + same data = identical results.

Position entry timestamps are derived from the fill event's `Timestamp` (which originates from the market data), not from wall-clock time. This ensures that `ClosedTrade.EntryTime` and holding-period calculations are fully deterministic across replays.
