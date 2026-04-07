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
| FillEvent | Portfolio.Update | Equity curve point |

## RiskLayer Enforcement

The `OrderEvent.RiskApproved` flag ensures no order reaches the ExecutionHandler without passing through the RiskLayer. The dispatch table pattern-matches on this flag.

### Flat Signal Handling

When `DefaultRiskLayer.ConvertSignal` receives a `SignalEvent` with `Direction.Flat`, it checks whether the portfolio holds an open position for that symbol. If a position exists (quantity > 0), it emits a closing `OrderEvent` (direction `Short`, quantity equal to the full position size, `OrderType.Market`, `RiskApproved = true`). If no position exists, it returns `null` and no order is generated.

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

`InMemoryDataProvider` serves bar and tick records from pre-loaded in-memory lists. It is not resolved via `DataProviderType` — instead, research workflows that partition data (RandomizedOOS, WalkForward) construct it directly with the relevant data slice. It is also useful in tests as a fast, deterministic data source. It supports construction from bars only, ticks only, or both, and respects `CancellationToken` on iteration.

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

### Repository Directory Resolution

`JsonFileRepository<T>` resolves its storage directory using the following logic:

1. If `RepositoryOptions.BaseDirectory` is configured (non-empty), use that path.
2. Otherwise, fall back to `%LOCALAPPDATA%/TradingResearchEngine/{TypeName}` (e.g. `BacktestResult`, `ScenarioConfig`).

The directory is created automatically if it does not exist. This means the repository works out of the box with zero configuration — useful for first-run and local development scenarios where no `appsettings.json` is present.

## Deterministic Replay

When `ScenarioConfig.RandomSeed` is set, all RNG instances are seeded from it. Same config + same data = identical results.

Position entry timestamps are derived from the fill event's `Timestamp` (which originates from the market data), not from wall-clock time. This ensures that `ClosedTrade.EntryTime` and holding-period calculations are fully deterministic across replays.
