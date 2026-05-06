# Requirements Document

## Introduction

V8 of TradingResearchEngine delivers four parallel tracks: an AI Strategy Builder with Google Gemini integration and MT4/MT5/PineScript export, a Paper Trading mode for simulated live execution, a shared Indicator Library backed by Skender.Stock.Indicators, and Parallel Multi-Symbol Portfolio Backtesting with performance optimisations. All work respects the existing hexagonal architecture (`Core ← Application ← Infrastructure ← { Cli, Api, Web }`), immutable record conventions, `CancellationToken` propagation, and deterministic seed requirements.

## Glossary

- **AI_Strategy_Assistant**: Application-layer service that generates or refines strategy drafts using a large language model (Google Gemini).
- **AIStrategyDraft**: Immutable record representing a machine-generated strategy configuration including name, hypothesis, parameters, risk config, rationale, and caveats.
- **Strategy_Exporter**: Application-layer service that converts a validated StrategyVersion into equivalent source code for an external trading platform.
- **ExportFormat**: Enum identifying the target platform language — MQL4, MQL5, or PineScript.
- **ExportResult**: Immutable record containing the generated code, filename, format, and any translation warnings.
- **Paper_Trading_Session**: Core-layer abstraction representing a simulated live trading session that streams bars and trades in real time.
- **PaperTradingStatus**: Enum representing session lifecycle states — Idle, Connecting, Running, Paused, Stopped, Error.
- **PaperTradingResult**: Immutable record produced on session stop containing final portfolio state, closed trades, and computed metrics.
- **PaperSessionRecord**: Persisted Application-layer record tracking paper session metadata and summary.
- **Streaming_Data_Provider**: Core-layer interface extending IMarketDataProvider to emit bars as an asynchronous stream.
- **Polling_Streaming_Provider**: Infrastructure-layer implementation that polls an existing data provider at a configurable interval.
- **Indicator_Series**: Application-layer interface representing a streaming, warm-up-aware indicator computation.
- **Skender_Adapter**: Generic adapter wrapping Skender.Stock.Indicators methods behind the IIndicatorSeries interface.
- **Portfolio_Backtest_Runner**: Application-layer orchestrator that runs multiple single-symbol backtests in parallel and aggregates results.
- **PortfolioBacktestConfig**: Core-layer immutable record defining multi-symbol backtest parameters including portfolio risk constraints.
- **PortfolioBacktestResult**: Immutable record containing per-symbol results, merged portfolio equity curve, correlation matrix, and portfolio-level metrics.
- **BarDataPool**: Core-layer object pool reducing allocations on the hot bar-processing path.
- **GeminiOptions**: Application-layer options record for Gemini API configuration (key, model, retries, prompt path).
- **StrategyRegistry**: Existing Application-layer singleton mapping strategy names to types.

---

## Requirements

### Requirement 1: AI Strategy Generation

**User Story:** As a strategy researcher, I want to describe a trading idea in natural language and receive a structured strategy draft, so that I can rapidly prototype strategies without manual parameter configuration.

#### Acceptance Criteria

1. WHEN a natural-language prompt is submitted, THE AI_Strategy_Assistant SHALL return an AIStrategyDraft containing StrategyName, Hypothesis, StrategyType, Parameters, SuggestedRisk, Rationale, and Caveats.
2. THE AI_Strategy_Assistant SHALL use structured JSON output mode (Gemini response schema) so that the returned AIStrategyDraft is always machine-parseable without free-text extraction.
3. THE AI_Strategy_Assistant SHALL load the system prompt from a configurable file path (default: `Prompts/strategy-assistant-system.md`) specified in GeminiOptions.
4. THE AI_Strategy_Assistant SHALL read the Gemini API key from `GeminiOptions.ApiKey` via `IOptions<GeminiOptions>` and SHALL NOT hardcode credentials.
5. WHEN the returned StrategyType is not present in StrategyRegistry.KnownNames, THE AI_Strategy_Assistant SHALL retry once with a correction prompt containing the list of known strategy names.
6. IF the retry also returns an unknown StrategyType, THEN THE AI_Strategy_Assistant SHALL return the draft with a Caveat indicating the strategy type is unrecognised.
7. THE AI_Strategy_Assistant SHALL accept a CancellationToken and propagate it to all async calls.
8. WHEN generation succeeds, THE AI_Strategy_Assistant SHALL tag the resulting draft with SourceType.AIGenerated.

### Requirement 2: Iterative AI Refinement

**User Story:** As a strategy researcher, I want to refine an AI-generated strategy using backtest results and a follow-up prompt, so that I can iteratively improve strategy performance without starting from scratch.

#### Acceptance Criteria

1. WHEN a current AIStrategyDraft, a BacktestResult, and a refinement prompt are submitted, THE AI_Strategy_Assistant SHALL return a revised AIStrategyDraft incorporating the feedback.
2. THE AI_Strategy_Assistant SHALL include key metrics from the BacktestResult (Sharpe, MaxDrawdown, WinRate, TradeCount, DSR) in the refinement context sent to the model.
3. THE AI_Strategy_Assistant SHALL preserve the structured JSON output requirement for refinement responses.
4. THE AI_Strategy_Assistant SHALL accept a CancellationToken and propagate it to all async calls during refinement.

### Requirement 3: AI Strategy Builder UI

**User Story:** As a user of the web application, I want an AI assistant button in the Strategy Builder wizard, so that I can generate strategy drafts directly within the existing workflow.

#### Acceptance Criteria

1. WHEN the user clicks "AI Assistant" on Step 1 of the StrategyBuilder wizard, THE Web_Application SHALL open a dialog with a natural-language text area and a "Generate" button.
2. WHEN generation succeeds, THE Web_Application SHALL inject the AIStrategyDraft fields into the builder form and display the Rationale and Caveats in an advisory panel.
3. THE Web_Application SHALL preserve all existing manual builder flows without modification.
4. WHEN the result's StrategyVersion.SourceType equals AIGenerated, THE Web_Application SHALL display a "Refine with AI" button on the Backtest Result Detail page.

### Requirement 4: MQL4 Strategy Export

**User Story:** As a strategy researcher, I want to export a validated strategy as an MT4 Expert Advisor, so that I can deploy it on the MetaTrader 4 platform.

#### Acceptance Criteria

1. WHEN a StrategyVersion and ExportFormat.MQL4 are provided, THE Strategy_Exporter SHALL generate a valid `.mq4` file containing input parameters, `OnInit()`, `OnTick()`, and `OnDeinit()` functions.
2. THE Strategy_Exporter SHALL map all 6 built-in strategy types to their closest MQL4 equivalent logic.
3. WHEN exact equivalence is impossible, THE Strategy_Exporter SHALL emit a `// NOTE:` comment in the generated code and add a Warning to ExportResult.
4. IF the strategy type is unsupported, THEN THE Strategy_Exporter SHALL return an ExportResult with empty Code and a single Warning explaining the gap.

### Requirement 5: MQL5 Strategy Export

**User Story:** As a strategy researcher, I want to export a validated strategy as an MT5 Expert Advisor, so that I can deploy it on the MetaTrader 5 platform.

#### Acceptance Criteria

1. WHEN a StrategyVersion and ExportFormat.MQL5 are provided, THE Strategy_Exporter SHALL generate a valid `.mq5` file using the CTrade class pattern with `OnTick()` and `OnTrade()` functions.
2. THE Strategy_Exporter SHALL map all 6 built-in strategy types to their closest MQL5 equivalent logic.
3. WHEN exact equivalence is impossible, THE Strategy_Exporter SHALL emit a `// NOTE:` comment in the generated code and add a Warning to ExportResult.
4. IF the strategy type is unsupported, THEN THE Strategy_Exporter SHALL return an ExportResult with empty Code and a single Warning explaining the gap.

### Requirement 6: PineScript Strategy Export

**User Story:** As a strategy researcher, I want to export a validated strategy as a TradingView Pine Script, so that I can visualise and share it on TradingView.

#### Acceptance Criteria

1. WHEN a StrategyVersion and ExportFormat.PineScript are provided, THE Strategy_Exporter SHALL generate a valid Pine Script v6 strategy using `strategy()`, `ta.*` functions, `strategy.entry()`, and `strategy.close()`.
2. THE Strategy_Exporter SHALL map all 6 built-in strategy types to their closest PineScript equivalent logic.
3. WHEN exact equivalence is impossible, THE Strategy_Exporter SHALL emit a `// NOTE:` comment in the generated code and add a Warning to ExportResult.
4. IF the strategy type is unsupported, THEN THE Strategy_Exporter SHALL return an ExportResult with empty Code and a single Warning explaining the gap.

### Requirement 7: Strategy Export API Endpoint

**User Story:** As an API consumer, I want to export a strategy via HTTP, so that I can integrate export functionality into automated workflows.

#### Acceptance Criteria

1. WHEN a POST request is sent to `/strategies/{versionId}/export` with a `format` query parameter of MQL4, MQL5, or PineScript, THE Api SHALL return HTTP 200 with Content-Type `text/plain` and the generated code.
2. IF the versionId does not exist, THEN THE Api SHALL return HTTP 400 with a structured error response.
3. IF the format parameter is missing or invalid, THEN THE Api SHALL return HTTP 400 with a structured error response.
4. THE Api endpoint SHALL have `.WithName("ExportStrategy")` and `.WithTags("Strategies")` for OpenAPI registration.

### Requirement 8: Strategy Export UI

**User Story:** As a web application user, I want an export panel on the Strategy Detail page, so that I can generate, view, copy, and download exported strategy code.

#### Acceptance Criteria

1. THE Web_Application SHALL display an "Export Strategy" panel on the Strategy Detail page with a format selector and "Export" button.
2. WHEN export succeeds, THE Web_Application SHALL display the generated code in a read-only text field with a "Copy to Clipboard" button.
3. THE Web_Application SHALL provide a "Download" button that triggers a file download with the appropriate extension (.mq4, .mq5, or .pine).
4. WHEN the ExportResult contains Warnings, THE Web_Application SHALL display them in an advisory panel above the code.

### Requirement 9: Paper Trading Session Interface

**User Story:** As a strategy researcher, I want a paper trading session abstraction, so that I can simulate live trading using the same execution pipeline as backtesting.

#### Acceptance Criteria

1. THE Paper_Trading_Session SHALL expose StartAsync, StopAsync, PauseAsync, ResumeAsync, Status, Portfolio, BarStream, and TradeStream members.
2. THE Paper_Trading_Session SHALL report status via PaperTradingStatus enum values: Idle, Connecting, Running, Paused, Stopped, Error.
3. THE Paper_Trading_Session SHALL emit PaperBarEvent records on BarStream containing the bar data, timestamp, and portfolio snapshot.
4. THE Paper_Trading_Session SHALL emit PaperTradeEvent records on TradeStream containing the closed trade, timestamp, and portfolio snapshot.
5. THE Paper_Trading_Session SHALL accept a CancellationToken on StartAsync and ResumeAsync and propagate it throughout the session lifecycle.

### Requirement 10: Simulated Paper Trading Execution

**User Story:** As a strategy researcher, I want paper trading to reuse the same execution pipeline as backtesting, so that simulated live results are directly comparable to historical backtests.

#### Acceptance Criteria

1. THE Simulated_Paper_Trading_Session SHALL use the same IStrategy, IRiskLayer, IExecutionHandler, ISlippageModel, and ICommissionModel pipeline as the backtest engine.
2. THE Simulated_Paper_Trading_Session SHALL maintain a live Portfolio with mark-to-market updated on every received bar.
3. WHEN StopAsync is called, THE Simulated_Paper_Trading_Session SHALL produce a PaperTradingResult containing the final Portfolio, closed trades, and BacktestResult-equivalent metrics computed by MetricsCalculator.
4. THE Simulated_Paper_Trading_Session SHALL persist session metadata as a PaperSessionRecord via IRepository.
5. THE Simulated_Paper_Trading_Session SHALL receive data from an IStreamingDataProvider at a configurable tick interval.
6. WHEN PauseAsync is called, THE Simulated_Paper_Trading_Session SHALL halt bar consumption and preserve Portfolio state until ResumeAsync is called.

### Requirement 11: Streaming Data Provider

**User Story:** As a paper trading session, I want a streaming data source, so that I can receive bars in real time or at accelerated playback speed.

#### Acceptance Criteria

1. THE Streaming_Data_Provider SHALL extend IMarketDataProvider with a StreamAsync method returning IAsyncEnumerable of BarData.
2. THE Polling_Streaming_Provider SHALL poll an existing IMarketDataProvider at a configurable TimeSpan interval and emit the latest completed bar.
3. THE Polling_Streaming_Provider SHALL support a fast-forward playback mode using historical data at a configurable speed ratio for testing without real-time delays.
4. THE Polling_Streaming_Provider SHALL accept a CancellationToken and terminate streaming when cancellation is requested.

### Requirement 12: Paper Trading Web UI

**User Story:** As a web application user, I want a paper trading section with session setup, live dashboard, and session history, so that I can run and monitor simulated live trading sessions.

#### Acceptance Criteria

1. THE Web_Application SHALL display a "Paper Trading" section in the sidebar navigation below "Prop-Firm".
2. THE Web_Application SHALL provide a Session Setup page with strategy selector, data source configuration (symbol + timeframe), initial cash, realism profile, and polling interval inputs.
3. WHEN a session is running, THE Web_Application SHALL display a Live Dashboard with a streaming equity curve chart, open positions table, recent trades table, and key metrics card (PnL, Sharpe, win rate, trade count).
4. THE Web_Application SHALL provide Pause and Stop buttons on the Live Dashboard.
5. THE Web_Application SHALL provide a Session History page listing past sessions with start/stop times, final PnL, and a "Compare to Backtest" action.
6. THE Web_Application SHALL tag paper trading sessions with a 🧪 badge to distinguish them from historical backtests.

### Requirement 13: Paper Trading CLI

**User Story:** As a CLI user, I want a `paper` subcommand, so that I can run paper trading sessions from the terminal with configurable playback speed.

#### Acceptance Criteria

1. WHEN the `paper` subcommand is invoked with `--scenario` and optional `--speed` flags, THE Cli SHALL start a paper trading session using the specified scenario configuration.
2. THE Cli SHALL default the `--speed` flag to 1.0 (real-time playback) when not specified.
3. WHILE the paper session is running, THE Cli SHALL print live bar updates to console showing symbol, bar close, current equity, and open positions.
4. WHEN the session stops, THE Cli SHALL write a Markdown report summarising the session results.

### Requirement 14: Skender.Stock.Indicators Integration

**User Story:** As a strategy developer, I want a shared indicator library backed by Skender.Stock.Indicators, so that I can use tested, standard indicators without reimplementing them inline.

#### Acceptance Criteria

1. THE Application project SHALL reference the `Skender.Stock.Indicators` NuGet package (latest stable 2.x).
2. THE Indicator_Series interface SHALL expose Add(BarData), Reset(), and Results members for streaming indicator computation.
3. THE Skender_Adapter SHALL wrap Skender indicator invocations in a streaming-friendly, warm-up-aware implementation of IIndicatorSeries.
4. THE Skender_Adapter SHALL maintain a bounded internal queue (capacity WarmupPeriod × 2) and recompute the indicator on each Add call using Skender's standard extension methods on only the windowed contents, ensuring O(WarmupPeriod) per-bar cost rather than O(n).
5. THE Core project SHALL NOT reference Skender.Stock.Indicators — the integration lives entirely in Application.

### Requirement 15: Standard Indicator Wrappers

**User Story:** As a strategy developer, I want pre-built indicator wrappers for SMA, EMA, RSI, MACD, Bollinger Bands, ATR, Stochastic, and Donchian, so that I can compose strategies from standard building blocks.

#### Acceptance Criteria

1. THE Application project SHALL provide SmaIndicator, EmaIndicator, RsiIndicator, MacdIndicator, BollingerBandsIndicator, AtrIndicator, StochasticIndicator, and DonchianIndicator classes implementing IIndicatorSeries.
2. Each indicator wrapper SHALL expose a bool IsWarm property that returns true when Results.Count is greater than or equal to the indicator's warm-up period.
3. Each indicator wrapper SHALL carry XML doc comments describing the indicator formula and typical use.
4. Each indicator wrapper SHALL delegate computation to the corresponding Skender.Stock.Indicators method.

### Requirement 16: Strategy Refactor to Indicator Wrappers

**User Story:** As a maintainer, I want all 6 built-in strategies to use the shared indicator wrappers, so that indicator logic is centralised and testable.

#### Acceptance Criteria

1. THE 6 built-in strategies SHALL replace inline circular-buffer indicator computations with the appropriate IIndicatorSeries wrappers.
2. Each strategy's OnMarketData method SHALL call Add(bar) on its indicator instances before computing signals.
3. THE refactored strategies SHALL produce functionally identical results — existing backtest outputs SHALL NOT change.
4. THE project SHALL include regression integration tests that run each strategy on a fixed seed dataset and assert result metrics match pre-refactor values to 4 decimal places (1e-4 tolerance) to account for floating-point differences between Skender EMA and the existing custom implementation.

### Requirement 17: Indicator Overlays on Charts

**User Story:** As a web application user, I want to overlay indicators on the backtest result chart, so that I can visually correlate indicator values with trade signals.

#### Acceptance Criteria

1. THE Web_Application SHALL provide a multi-select control on the Result Detail page populated from available indicator wrappers.
2. WHEN an on-price indicator (SMA, EMA, Bollinger Bands, Donchian) is selected, THE Web_Application SHALL display it as an additional trace on the price chart.
3. WHEN an oscillator indicator (RSI, MACD, Stochastic) is selected, THE Web_Application SHALL display it in a separate subplot beneath the price chart.
4. THE Web_Application SHALL recompute selected indicators from the backtest's bar data before rendering overlays.

### Requirement 18: Portfolio Backtest Configuration

**User Story:** As a portfolio researcher, I want to define multi-symbol backtest configurations with portfolio-level risk constraints, so that I can test diversified strategies.

#### Acceptance Criteria

1. THE PortfolioBacktestConfig SHALL contain Symbols (list of DataConfig), Strategies (list of StrategyConfig), PortfolioRisk, Execution, InitialCash, optional Seed, and optional Timeframe.
2. THE PortfolioRisk SHALL include MaxPortfolioHeatPercent (max total risk across all open positions), MaxCorrelationAllowed (default 0.85), and RebalanceMode (None, EqualWeight, VolatilityParity).
3. THE PortfolioBacktestConfig SHALL be an immutable record in the Core project.
4. THE PortfolioBacktestConfig SHALL support a single strategy applied to all symbols or one strategy per symbol.

### Requirement 19: Portfolio Backtest Runner

**User Story:** As a portfolio researcher, I want parallel multi-symbol backtest execution with aggregated portfolio metrics, so that I can evaluate diversified strategy performance efficiently.

#### Acceptance Criteria

1. THE Portfolio_Backtest_Runner SHALL create one BacktestEngine instance per symbol, each with its own EventQueue and Portfolio.
2. THE Portfolio_Backtest_Runner SHALL execute per-symbol runs in parallel using Parallel.ForEachAsync with a SemaphoreSlim concurrency cap of Environment.ProcessorCount minus 1 (minimum 1).
3. WHEN all per-symbol runs complete, THE Portfolio_Backtest_Runner SHALL merge equity curve points into a single portfolio-level equity curve weighted by the configured RebalanceMode.
4. THE Portfolio_Backtest_Runner SHALL compute a correlation matrix across all per-symbol return series.
5. THE Portfolio_Backtest_Runner SHALL compute portfolio-level metrics via MetricsCalculator on the merged equity curve.
6. THE Portfolio_Backtest_Runner SHALL compute portfolio turnover as the average monthly number of position changes across all symbols.
7. THE Portfolio_Backtest_Runner SHALL accept a CancellationToken and propagate it to all per-symbol engine runs.
8. THE Portfolio_Backtest_Runner SHALL accept an optional Seed and produce deterministic results when the same seed and inputs are supplied.

### Requirement 20: Benchmark.NET Performance Suite

**User Story:** As a performance engineer, I want a dedicated benchmark project with standard scenarios, so that I can measure and track engine throughput and memory allocation.

#### Acceptance Criteria

1. THE Benchmarks project SHALL reference BenchmarkDotNet and target net8.0 with OutputType Exe.
2. THE Benchmarks project SHALL include SingleSymbol_1Year_Daily (252 bars), SingleSymbol_1Year_H1 (6048 bars), SingleSymbol_5Year_M15 (120960 bars), PortfolioRun_5Symbols_1Year_Daily, and ParameterSweep_10x10_Daily benchmark methods.
3. THE Benchmarks project SHALL use MemoryDiagnoser, SimpleJob(RuntimeMoniker.Net80), and Orderer(SummaryOrderPolicy.FastestToSlowest) attributes.
4. THE Benchmarks project SHALL export results to `artifacts/benchmarks/` as markdown and JSON.

### Requirement 21: Object Pooling for Hot Path

**User Story:** As a performance engineer, I want object pooling on the hot bar-processing path, so that GC pressure is reduced during large backtests.

#### Acceptance Criteria

1. THE BarDataPool SHALL use System.Buffers.ArrayPool and Microsoft.Extensions.ObjectPool to pool collections used in DataHandler and Portfolio.MarkToMarket.
2. THE object pooling SHALL be transparent to callers — IStrategy.OnMarketData(BarData) signature SHALL remain unchanged.
3. THE SingleSymbol_5Year_M15 benchmark SHALL demonstrate at least a 20% reduction in allocated bytes per operation compared to the pre-pooling baseline.

### Requirement 22: Portfolio Backtest Web UI

**User Story:** As a web application user, I want a portfolio backtest setup and result view, so that I can configure multi-symbol runs and analyse diversification metrics visually.

#### Acceptance Criteria

1. THE Web_Application SHALL provide a "Portfolio (Multi-Symbol)" toggle on the New Run page alongside the existing "Single Symbol" mode.
2. WHEN Portfolio mode is selected, THE Web_Application SHALL display a symbol list with add/remove rows (each with data file and strategy selectors), portfolio risk settings, initial cash, and execution profile inputs.
3. THE Web_Application SHALL display a Portfolio Result Detail page with portfolio-level equity curve, per-symbol performance cards, correlation matrix heatmap, and portfolio turnover metric.
4. THE Web_Application SHALL render the correlation matrix as a colour-coded grid from green (low correlation) to red (high correlation).

### Requirement 23: Portfolio API Endpoints

**User Story:** As an API consumer, I want portfolio backtest endpoints, so that I can run and sweep multi-symbol backtests programmatically.

#### Acceptance Criteria

1. WHEN a POST request is sent to `/portfolios/run` with a valid PortfolioBacktestConfig JSON body, THE Api SHALL return HTTP 200 with a PortfolioBacktestResult JSON response.
2. WHEN a POST request is sent to `/portfolios/sweep` with a portfolio config and parameter sweep specification, THE Api SHALL return HTTP 200 with a list of PortfolioBacktestResult.
3. IF the request body fails validation, THEN THE Api SHALL return HTTP 400 with a structured error response.
4. THE Api endpoints SHALL have `.WithName()` and `.WithTags("Portfolios")` for OpenAPI registration.

### Requirement 24: GeminiOptions Configuration

**User Story:** As a system administrator, I want Gemini API settings to be configurable via standard .NET options, so that deployment environments can be configured without code changes.

#### Acceptance Criteria

1. THE GeminiOptions record SHALL contain ApiKey, ModelName (default "gemini-2.0-flash"), MaxRetries (default 2), and SystemPromptFilePath (default "Prompts/strategy-assistant-system.md").
2. THE GeminiOptions SHALL be bound from application configuration via IOptions pattern.
3. THE GeminiOptions.ApiKey SHALL NOT be logged, serialised to responses, or exposed in any API output.
4. IF GeminiOptions.ApiKey is null or empty at startup, THEN THE application SHALL log a warning and disable AI assistant features gracefully without crashing.

### Requirement 25: AI Assistant Unit Tests

**User Story:** As a developer, I want comprehensive unit tests for the AI assistant, so that JSON parsing, retry logic, and cancellation behaviour are verified.

#### Acceptance Criteria

1. THE unit tests SHALL verify that a valid structured JSON response is correctly deserialised into an AIStrategyDraft.
2. THE unit tests SHALL verify that an unknown StrategyType triggers exactly one retry with a correction prompt.
3. THE unit tests SHALL verify that CancellationToken cancellation throws OperationCanceledException.
4. THE unit tests SHALL use Moq to mock the Gemini client dependency.

### Requirement 26: Strategy Exporter Unit Tests

**User Story:** As a developer, I want unit tests for each exporter covering all 6 built-in strategies, so that export correctness is verified across all supported formats.

#### Acceptance Criteria

1. THE unit tests SHALL verify each of the 3 exporters (MQL4, MQL5, PineScript) produces non-empty code for all 6 built-in strategy types.
2. THE unit tests SHALL verify that an unsupported strategy type returns an ExportResult with empty Code and a Warning.
3. THE unit tests SHALL verify that missing parameters fall back to defaults without throwing exceptions.

### Requirement 27: Portfolio Runner Integration Tests

**User Story:** As a developer, I want integration tests for the portfolio runner, so that determinism, correlation symmetry, and portfolio metric bounds are verified.

#### Acceptance Criteria

1. THE integration tests SHALL verify that running with the same seed and inputs produces identical PortfolioBacktestResult.
2. THE integration tests SHALL verify that the correlation matrix is symmetric (CorrelationMatrix[A][B] equals CorrelationMatrix[B][A]).
3. THE integration tests SHALL verify that portfolio Sharpe is less than or equal to the maximum individual symbol Sharpe when correlation is greater than 0.

### Requirement 28: Paper Trading Unit Tests

**User Story:** As a developer, I want unit tests for the paper trading session, so that metric equivalence with backtesting is verified.

#### Acceptance Criteria

1. THE unit tests SHALL verify that PaperTradingResult metrics match an equivalent BacktestResult for the same historical data sequence fed through a mocked IStreamingDataProvider.
2. THE unit tests SHALL verify that StopAsync transitions status to Stopped and produces a valid PaperTradingResult.
3. THE unit tests SHALL verify that CancellationToken cancellation stops the session gracefully.

---

## Out of Scope for V8

- Live broker API adapters (IBKR, OANDA, Alpaca, Binance) — deferred to V9
- Multi-currency portfolio tracking
- Auto-generated strategy code from genetic programming
- Options/derivatives support
- Cloud deployment / multi-user / authentication
- AI strategy generation for custom (non-built-in) strategy types — deferred to V9 once plugin loader is implemented
