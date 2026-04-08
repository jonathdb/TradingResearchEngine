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

### DukascopyDataProvider

`DukascopyDataProvider` downloads historical minute-bar data from Dukascopy's free datafeed. It requires no API key. Data is served as LZMA-compressed binary files (`.bi5` format) and decoded into `BarRecord` instances.

> **Note:** `DukascopyDataProvider` is no longer resolvable via `DataProviderType` in `ScenarioConfig`. It was removed from the `DataProviderFactory` switch. The class still exists in Infrastructure but must be constructed manually if needed.

The provider implements a local CSV cache keyed by symbol, interval, and date range. On each `GetBars` call it checks for a cached file first. If a cache hit is found, bars are loaded from CSV and filtered to the requested range — no HTTP requests are made. On a cache miss, the provider downloads, aggregates, and then saves the result to CSV before yielding bars.

When downloading, the provider builds a list of trading days (skipping weekends) for the requested date range, then downloads them in parallel batches of up to 4 concurrent HTTP requests. Each `.bi5` payload is decompressed using SharpCompress (`LzmaStream`) with the standard 13-byte header (5-byte properties + 8-byte uncompressed size + compressed stream), and raw pipette values are converted to decimal prices using a per-symbol point size. Supported instruments include major forex pairs, gold, silver, and equity index CFDs. Unknown symbols default to a point size of 100,000.

After all batches complete, minute bars are sorted by timestamp (parallel downloads may arrive out of order) and then aggregated to the requested interval. The aggregated bars are saved to the local CSV cache before being yielded. `CancellationToken` is checked between batches.

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

## Deterministic Replay

When `ScenarioConfig.RandomSeed` is set, all RNG instances are seeded from it. Same config + same data = identical results.

Position entry timestamps are derived from the fill event's `Timestamp` (which originates from the market data), not from wall-clock time. This ensures that `ClosedTrade.EntryTime` and holding-period calculations are fully deterministic across replays.
