# Kiro Spec Prompt: V8 — AI Strategy Builder, Export Engine, Paper Trading, Indicator Library & Parallel Portfolio Backtesting

You are a senior software engineer and quant trading expert working on TradingResearchEngine — a C# 12 / .NET 8 event-driven backtesting engine with clean hexagonal architecture (`Core ← Application ← Infrastructure ← { Cli, Api, Web }`).

Please generate a complete Kiro spec (requirements.md, design.md, tasks.md) for the **V8 feature set** described below. All work must respect the existing architecture, naming conventions, testing standards, and NuGet package governance defined in `.kiro/steering/`. Every new public type must carry XML doc comments. All new domain types must be immutable records. No magic numbers — use named constants or `IOptions<T>`. All async methods accept `CancellationToken`. All stochastic workflows accept explicit seeds for determinism.

---

## Feature Overview

V8 delivers four parallel tracks:

1. **AI Strategy Builder (Google Gemini) + Strategy Export (MT4/MT5/PineScript)**
2. **Paper Trading Mode**
3. **Indicator Library (via Skender.Stock.Indicators integration)**
4. **Parallel Multi-Symbol Portfolio Backtesting + Performance**

---

## Track 1 — AI Strategy Builder & Export Engine

### Background

The engine already has `StrategyIdentity`, `StrategyVersion`, `ConfigDraft`, `StrategyParameterSchema`, and the 5-step `StrategyBuilder` wizard (V5). The `SourceType` enum already includes values for tracking how a strategy was created. There is currently no mechanism to use an LLM to help create strategies, and no mechanism to export a validated strategy to MT4 MQL4, MT5 MQL5, or TradingView PineScript.

### Requirements

**R1 — Google Gemini AI Strategy Assistant**

- Introduce `IAIStrategyAssistant` in Application layer with a single method: `Task<AIStrategyDraft> GenerateStrategyAsync(string naturalLanguagePrompt, CancellationToken ct)`.
- `AIStrategyDraft` is an immutable record containing: `string StrategyName`, `string Hypothesis`, `string StrategyType` (maps to `StrategyRegistry.KnownNames`), `IReadOnlyDictionary<string, object> Parameters`, `RiskConfig SuggestedRisk`, `string Rationale`, `IReadOnlyList<string> Caveats`.
- Implement `GeminiStrategyAssistant : IAIStrategyAssistant` in Infrastructure using the `Mscc.GenerativeAI` NuGet package (add to Infrastructure project).
- The assistant MUST use **structured JSON output** (Gemini JSON mode / response schema) so that the returned `AIStrategyDraft` is always machine-parseable — no free-text parsing.
- The system prompt for the assistant must be loaded from a configurable file path (default: `Prompts/strategy-assistant-system.md`) so users can customise it without recompiling.
- The Gemini API key is read from `GeminiOptions.ApiKey` via `IOptions<GeminiOptions>` — never hardcoded.
- The assistant validates that `StrategyType` is a known strategy name via `StrategyRegistry` before returning; if not, it retries once with a correction prompt.
- Add a `GeminiOptions` record to Application (api key, model name defaulting to `"gemini-2.0-flash"`, max retries defaulting to 2, system prompt file path).
- Add `SourceType.AIGenerated` to the `SourceType` enum (Application layer).
- Add an "AI Assistant" button to the `StrategyBuilder` wizard (Web layer) on Step 1 (Template selection). Clicking it opens a `MudDialog` with a natural-language text area and a "Generate" button. On success the draft is injected into the builder's form fields. The rationale and caveats are displayed as an advisory panel beneath the form.
- The AI assistant is **additive** — all existing manual builder flows remain unchanged.
- Add `GeminiStrategyAssistant` unit tests with a mocked `IGenerativeAI` client; test JSON parse, unknown strategy retry, and cancellation.

**R2 — Iterative AI Refinement**

- `IAIStrategyAssistant` also exposes `Task<AIStrategyDraft> RefineStrategyAsync(AIStrategyDraft current, BacktestResult lastResult, string refinementPrompt, CancellationToken ct)`.
- The refinement call includes: the current draft, the key metrics from the last `BacktestResult` (Sharpe, MaxDrawdown, WinRate, TradeCount, DSR), and the user's free-text refinement request.
- A "Refine with AI" button appears on the Backtest Result Detail page (Web) when the result's `StrategyVersion.SourceType == AIGenerated`.

**R3 — MT4/MT5/PineScript Export**

- Introduce `IStrategyExporter` in Application: `Task<ExportResult> ExportAsync(StrategyVersion version, ExportFormat format, CancellationToken ct)`.
- `ExportFormat` enum: `{ MQL4, MQL5, PineScript }`.
- `ExportResult` record: `ExportFormat Format`, `string FileName`, `string Code`, `IReadOnlyList<string> Warnings`.
- Implement three exporters as `IStrategyExporter` implementations in Infrastructure (one per format). Each maps the `StrategyVersion`'s `StrategyType` and `Parameters` to the target language's syntax:
  - `MQL4StrategyExporter` — generates an MT4 Expert Advisor `.mq4` file with input parameters, `OnInit()`, `OnTick()`, and `OnDeinit()`.
  - `MQL5StrategyExporter` — generates an MT5 Expert Advisor `.mq5` file using the `CTrade` class pattern with `OnTick()` and `OnTrade()`.
  - `PineScriptExporter` — generates a TradingView Pine Script v6 strategy (not indicator) with `strategy()`, `ta.*` function calls, `strategy.entry()`, and `strategy.close()`.
- The exporter maps engine strategy types to their closest equivalent logic in the target language. Where exact equivalence is impossible, a `// NOTE:` comment is emitted and a `Warning` is added to `ExportResult`.
- Export is available for all 6 built-in strategies. Unsupported strategy types return an `ExportResult` with code `""` and a single `Warning` explaining the gap.
- Add an "Export Strategy" panel to the Strategy Detail page (Web) with a format selector (`MudSelect<ExportFormat>`) and "Export" button. The generated code is displayed in a `MudTextField` with `ReadOnly=true`, `Lines=20` and a "Copy to Clipboard" JS interop button. A "Download .mq4/.mq5/.pine" button triggers file download via `IJSRuntime.InvokeVoidAsync("downloadFile", ...)`.
- Add a `POST /strategies/{versionId}/export` endpoint to the API that accepts `?format=MQL4|MQL5|PineScript` and returns `Content-Type: text/plain` with the appropriate file extension.
- Add unit tests for each exporter covering all 6 built-in strategies and edge cases (unknown strategy type, missing parameters using defaults).

---

## Track 2 — Paper Trading Mode

### Background

The engine has no live or paper trading. The `IExecutionHandler` and `IStrategy` interfaces in Core are already clean abstractions. `CancellationToken` propagation and `async Task<BacktestResult>` are already standard. The `IMarketDataProvider` interface can provide streaming data as well as historical.

### Requirements

**R4 — IPaperTradingSession Interface**

- Introduce `IPaperTradingSession` in Core: `StartAsync(ScenarioConfig config, CancellationToken ct)`, `StopAsync()`, `PaperTradingStatus Status`, `Portfolio Portfolio`, `IObservable<PaperBarEvent> BarStream`, `IObservable<PaperTradeEvent> TradeStream`.
- `PaperTradingStatus` enum: `{ Idle, Connecting, Running, Paused, Stopped, Error }`.
- `PaperBarEvent` record: `BarData Bar`, `DateTimeOffset Timestamp`, `PortfolioSnapshot Snapshot`.
- `PaperTradeEvent` record: `ClosedTrade Trade`, `DateTimeOffset Timestamp`, `PortfolioSnapshot Snapshot`.

**R5 — SimulatedPaperTradingSession (Application)**

- Implement `SimulatedPaperTradingSession : IPaperTradingSession` in Application.
- Paper mode uses the **same `IStrategy`, `IRiskLayer`, `IExecutionHandler`, `ISlippageModel`, `ICommissionModel` pipeline** as the backtest engine — zero duplication of execution logic.
- Data is fed from a **real-time polling `IStreamingDataProvider`** (see R6) at a configurable tick interval.
- The session maintains a live `Portfolio` with mark-to-market updated on every bar.
- On `StopAsync()`, the session produces a `PaperTradingResult` record containing the final `Portfolio`, `IReadOnlyList<ClosedTrade>`, and a full `BacktestResult`-equivalent set of metrics computed by `MetricsCalculator` — enabling direct comparison to a historical backtest.
- Paper sessions are persisted as `PaperSessionRecord` (Application) with fields: `string Id`, `string StrategyVersionId`, `DateTimeOffset StartedAt`, `DateTimeOffset? StoppedAt`, `PaperTradingStatus Status`, and a summary snapshot. Persist via `IRepository<PaperSessionRecord>`.

**R6 — IStreamingDataProvider (Core)**

- Introduce `IStreamingDataProvider : IMarketDataProvider` in Core with an additional method: `IAsyncEnumerable<BarData> StreamAsync(string symbol, Timeframe timeframe, CancellationToken ct)`.
- Implement `PollingStreamingDataProvider : IStreamingDataProvider` in Infrastructure — polls any existing `IMarketDataProvider` at a configurable `TimeSpan` interval and emits the latest completed bar. Supports simulation (fast-forward mode using historical data at a configurable playback speed ratio, for testing paper mode without waiting for real time).
- `PollingStreamingDataProvider` is the only `IStreamingDataProvider` in V8. Live broker streaming adapters are out of scope and explicitly deferred.

**R7 — Paper Trading UI (Web)**

- Add a "Paper Trading" top-level section to the Blazor sidebar navigation (below "Prop-Firm").
- **Session Setup page**: Strategy selector (existing strategies), data source (symbol + timeframe), initial cash, realism profile, polling interval. "Start Session" button.
- **Live Dashboard page**: Real-time equity curve (Plotly.Blazor line chart, streaming updates via `IObservable` subscription), open positions table, recent trades table, key metrics card (PnL, Sharpe so far, win rate, trade count). "Pause" and "Stop" buttons. All Blazor components use `StateHasChanged()` on `IObservable` notifications via `SynchronizationContext` dispatch.
- **Session History page**: List of past paper sessions with start/stop times, final PnL, and a "Compare to Backtest" action that opens the existing ScenarioComparisonUseCase side-by-side view.
- Paper trading sessions are tagged in the Session History with a 🧪 badge to distinguish them from historical backtests.

**R8 — Paper Trading CLI**

- Add a `paper` subcommand to the CLI: `dotnet run --project src/TradingResearchEngine.Cli -- paper --scenario path/to/scenario.json [--speed 1.0]`.
- `--speed` is the playback ratio for historical data simulation (1.0 = real time, 10.0 = 10× faster). Defaults to 1.0.
- CLI prints live bar updates to console (symbol, bar close, current equity, open positions) and writes a Markdown report on `StopAsync()`.

---

## Track 3 — Indicator Library

### Background

Built-in strategies currently compute indicators inline (rolling SMA via `O(1)` circular buffers). There is no shared, tested indicator library. This makes it hard to author new strategies and impossible to display indicators as chart overlays in the UI.

### Requirements

**R9 — Skender.Stock.Indicators Integration**

- Add `Skender.Stock.Indicators` (NuGet: `Skender.Stock.Indicators`, latest stable `2.x`) as a dependency of the `TradingResearchEngine.Application` project. Do NOT add it to Core (Core must remain dependency-free of external packages beyond Microsoft.Extensions).
- Create `TradingResearchEngine.Application/Indicators/` folder.
- Introduce `IIndicatorSeries<TResult>` interface in Application: `void Add(BarData bar)`, `void Reset()`, `IReadOnlyList<TResult> Results`.
- Create `SkenderIndicatorAdapter<TQuote, TResult>` — a generic adapter that wraps a `Skender.Stock.Indicators` indicator invocation in a streaming-friendly, warm-up-aware `IIndicatorSeries<TResult>`. The adapter maintains an internal `List<TQuote>` and recomputes the indicator on each `Add()` call using Skender's standard `GetXxx(IEnumerable<TQuote>)` extension methods.

**R10 — Standard Indicator Wrappers**

Implement the following concrete `IIndicatorSeries<T>` wrappers in `Application/Indicators/`, each delegating to `Skender.Stock.Indicators`:

| Class | Skender Method | Parameters |
|---|---|---|
| `SmaIndicator` | `GetSma` | `int period` |
| `EmaIndicator` | `GetEma` | `int period` |
| `RsiIndicator` | `GetRsi` | `int period` |
| `MacdIndicator` | `GetMacd` | `int fastPeriod, int slowPeriod, int signalPeriod` |
| `BollingerBandsIndicator` | `GetBollingerBands` | `int period, double stdDevMultiplier` |
| `AtrIndicator` | `GetAtr` | `int period` |
| `StochasticIndicator` | `GetStoch` | `int period, int signalPeriod` |
| `DonchianIndicator` | `GetDonchian` | `int period` (wraps `GetDonchian(lookbackPeriods)`) |

Each wrapper: exposes a `bool IsWarm` property (true when `Results.Count >= WarmupPeriod`), uses `WarmupPeriod` from Skender metadata where available, and carries XML doc comments describing the indicator's formula and typical use.

**R11 — Strategy Refactor to Use Indicator Wrappers**

- Refactor all 6 built-in strategies to replace inline circular-buffer indicator computations with the appropriate `IIndicatorSeries<T>` wrappers from R10.
- The `OnMarketData(BarData bar)` method on each strategy calls `.Add(bar)` on its indicator instances before computing signals.
- All strategy logic and signal emission must remain functionally identical — existing backtest results must not change. Add regression integration tests that run each strategy on a fixed seed dataset and assert the result metrics match pre-refactor values to 6 decimal places.

**R12 — Indicator Overlays on Charts (Web)**

- Extend the Backtest Result Detail page to display configurable indicator overlays on the equity/price chart.
- Add a multi-select `MudSelect<string>` ("Add Indicator") on the Result Detail page populated from the available indicator wrappers.
- When an indicator is selected, recompute it from the backtest's bar data and add it as an additional Plotly.Blazor trace on the equity curve chart.
- Only overlays that are "on price" (SMA, EMA, Bollinger Bands, Donchian) are shown on the price chart. Oscillators (RSI, MACD, Stochastic) are shown in a separate subplot beneath the price chart.

---

## Track 4 — Parallel Multi-Symbol Portfolio Backtesting

### Background

The engine currently runs one symbol per backtest. V6 already added `Parallel.ForEachAsync` for walk-forward windows and parameter sweeps. The `Portfolio` class tracks multiple positions but is only ever fed one symbol's data. `MetricsCalculator` operates on the equity curve, which is portfolio-agnostic.

### Requirements

**R13 — PortfolioBacktestConfig**

- Introduce `PortfolioBacktestConfig` in Core as an immutable record containing: `IReadOnlyList<DataConfig> Symbols`, `IReadOnlyList<StrategyConfig> Strategies` (one per symbol, or a single strategy applied to all), `RiskConfig PortfolioRisk`, `ExecutionConfig Execution`, `decimal InitialCash`, `int? Seed`, `string? Timeframe`. 
- `PortfolioBacktestConfig` is a first-class alternative to `ScenarioConfig` — `ScenarioConfig` remains for single-symbol runs.
- The `PortfolioRisk` extends the existing `RiskConfig` with: `decimal MaxPortfolioHeatPercent` (max total risk across all open positions), `decimal MaxCorrelationAllowed` (positions with rolling 60-bar return correlation above this threshold are blocked; default 0.85), `PortfolioRebalanceMode RebalanceMode` enum `{ None, EqualWeight, VolatilityParity }`.

**R14 — PortfolioBacktestRunner (Application)**

- Implement `PortfolioBacktestRunner` in Application with `RunAsync(PortfolioBacktestConfig config, IProgressReporter progress, CancellationToken ct)` returning `PortfolioBacktestResult`.
- The runner creates one `BacktestEngine` instance per symbol (each with its own `EventQueue` and `Portfolio`) and runs them in parallel using `Parallel.ForEachAsync` with a `SemaphoreSlim` concurrency cap equal to `Environment.ProcessorCount - 1` (minimum 1).
- After all per-symbol runs complete, the runner aggregates results:
  - Merges equity curve points across all symbols into a single portfolio-level equity curve (weighted by `InitialCash / SymbolCount` for equal weight; by inverse volatility for `VolatilityParity`).
  - Computes correlation matrix across all per-symbol return series.
  - Computes portfolio-level metrics via `MetricsCalculator` on the merged equity curve.
  - Computes portfolio turnover: average monthly number of position changes across all symbols.
- `PortfolioBacktestResult` record: `IReadOnlyList<BacktestResult> SymbolResults`, `BacktestResult PortfolioResult`, `IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>> CorrelationMatrix`, `decimal AnnualisedTurnover`, `PortfolioRebalanceMode RebalanceMode`.

**R15 — Benchmark.NET Performance Suite**

- Create a new project `TradingResearchEngine.Benchmarks` (`.csproj` with `BenchmarkDotNet` NuGet reference, `<OutputType>Exe</OutputType>`, targeting `net8.0`). Add it to `TradingResearchEngine.sln`.
- Add a benchmark class `BacktestEngineBenchmarks` with:
  - `SingleSymbol_1Year_Daily` — runs `VolatilityScaledTrendStrategy` on 252 synthetic daily bars.
  - `SingleSymbol_1Year_H1` — runs on 6048 synthetic H1 bars.
  - `SingleSymbol_5Year_M15` — runs on 120960 synthetic M15 bars.
  - `PortfolioRun_5Symbols_1Year_Daily` — runs `PortfolioBacktestRunner` with 5 symbols × 252 daily bars.
  - `ParameterSweep_10x10_Daily` — runs a 10×10 parameter sweep on daily data.
- Benchmarks use `[MemoryDiagnoser]`, `[SimpleJob(RuntimeMoniker.Net80)]`, `[Orderer(SummaryOrderPolicy.FastestToSlowest)]`.
- Baseline throughput target (informational, not enforced as CI gate): `SingleSymbol_1Year_Daily` < 5ms mean.
- Add a GitHub Actions workflow step that runs benchmarks in `--filter "*"` mode and exports results to `artifacts/benchmarks/` as markdown and JSON. Results are uploaded as workflow artifacts (not compared automatically; baseline comparison is a V9 task).

**R16 — Object Pooling for Hot Path**

- Introduce `BarDataPool` in Core using `System.Buffers.ArrayPool<T>` and `Microsoft.Extensions.ObjectPool.ObjectPool<T>` to reduce allocations on the hot bar-processing path.
- Pool `List<BarData>` instances used in the `DataHandler` buffer. Pool `EquityCurvePoint` collections in `Portfolio.MarkToMarket()`.
- Object pooling must be transparent to callers — `IStrategy.OnMarketData(BarData)` signature is unchanged.
- Add a `[MemoryDiagnoser]` benchmark assertion: `SingleSymbol_5Year_M15` allocated bytes per operation must decrease vs. the pre-pooling baseline by at least 20% (measured by `BenchmarkDotNet` `AllocatedBytes` column; verified manually after implementation, not a CI gate).

**R17 — Portfolio Backtest UI (Web)**

- Add a "Portfolio Run" entry point on the "New Run" page with a toggle: "Single Symbol" (existing) / "Portfolio (Multi-Symbol)".
- Portfolio mode shows: symbol list (add/remove rows, each with its own data file selector and strategy selector), portfolio risk settings panel (`MaxPortfolioHeat`, `MaxCorrelationAllowed`, `RebalanceMode`), initial cash, execution profile.
- Portfolio Result Detail page shows: portfolio-level equity curve + metrics (same as single-symbol result), per-symbol performance cards (expandable), correlation matrix heatmap (`MudDataGrid` with conditional cell colour from green=low correlation to red=high), and a portfolio turnover metric card.

**R18 — Portfolio API Endpoint**

- Add `POST /portfolios/run` to the API that accepts `PortfolioBacktestConfig` JSON and returns `PortfolioBacktestResult` JSON.
- Add `POST /portfolios/sweep` that accepts a portfolio config with a parameter sweep specification and returns a list of `PortfolioBacktestResult`.

---

## Architecture & Integration Notes

1. **New NuGet packages to add** (update `.kiro/steering/tech.md`):
   - `Mscc.GenerativeAI` → Infrastructure project
   - `Skender.Stock.Indicators` → Application project
   - `BenchmarkDotNet` → Benchmarks project (new)
   - `Microsoft.Extensions.ObjectPool` → Core project

2. **New steering doc** `.kiro/steering/ai-standards.md` must be created covering: Gemini API key handling (never log, never expose in API responses), structured output requirement (no free-text LLM parsing), retry policy (max 2 retries with exponential backoff via Polly), and prompt file governance (prompts in `Prompts/` folder, version-controlled, no prompts hardcoded in C# strings).

3. **Dependency rule stays intact**:
   - `IAIStrategyAssistant`, `IStrategyExporter`, `IPaperTradingSession`, `IStreamingDataProvider`, `IIndicatorSeries<T>`, `PortfolioBacktestRunner` all live in Application or Core.
   - `GeminiStrategyAssistant`, `MQL4StrategyExporter`, `MQL5StrategyExporter`, `PineScriptExporter`, `PollingStreamingDataProvider` all live in Infrastructure.
   - `TradingResearchEngine.Benchmarks` references Infrastructure + Application directly (it is not a composition root — benchmarks need direct access for setup).

4. **Testing requirements**:
   - All new Application services: unit tests with Moq mocks.
   - All exporter implementations: unit tests covering each of the 6 built-in strategy types.
   - Strategy refactor (R11): regression integration tests.
   - `PortfolioBacktestRunner`: integration test with 3 symbols, verifying determinism (same seed = same result), correlation matrix symmetry, and portfolio Sharpe <= max(symbol Sharpes) when correlation is > 0.
   - `SimulatedPaperTradingSession`: unit test with a mocked `IStreamingDataProvider`, verifying that `PaperTradingResult` metrics match an equivalent `BacktestResult` for the same historical data sequence.

5. **Out of scope for V8**:
   - Live broker API adapters (IBKR, OANDA, Alpaca, Binance) — deferred to V9.
   - Multi-currency portfolio tracking — deferred.
   - Auto-generated strategy code from genetic programming — deferred.
   - Options/derivatives support — deferred.
   - Cloud deployment / multi-user / authentication — deferred.
   - AI strategy generation for custom (non-built-in) strategy types — deferred to V9 once plugin loader is implemented.

---

## Suggested Spec File Organization

Create the spec under `.kiro/specs/v8-ai-export-paper-indicators-portfolio/` containing:
- `requirements.md` — formal requirements with user stories and acceptance criteria (derived from above)
- `design.md` — architecture diagram, new type inventory, sequence diagrams for paper trading session lifecycle and AI assistant flow
- `tasks.md` — implementation checklist ordered: Core changes first → Application → Infrastructure → Web/Cli/Api → Tests → Benchmarks

Use the same format as existing specs (checkboxes, `_Requirements: X.Y` back-references, track groupings).

