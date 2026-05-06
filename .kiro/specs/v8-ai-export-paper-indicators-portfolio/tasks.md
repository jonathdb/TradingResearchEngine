# Implementation Plan: V8 — AI Strategy Builder, Export Engine, Paper Trading, Indicator Library & Portfolio Backtesting

## Overview

This plan implements four parallel capability tracks across the existing hexagonal architecture. Tasks are ordered Core → Application → Infrastructure → Hosts (Cli/Api/Web) → Tests → Benchmarks, grouped by track where possible. Each task builds incrementally on previous work, with checkpoints at natural integration boundaries.

## Tasks

- [x] 1. Update steering files and add new NuGet packages
  - [x] 1.1 Update `.kiro/steering/tech.md` with new NuGet packages
    - Add `Microsoft.Extensions.ObjectPool` → Core
    - Add `Skender.Stock.Indicators` (2.x) → Application
    - Add `Mscc.GenerativeAI` → Infrastructure
    - Add `BenchmarkDotNet` → Benchmarks
    - Add `System.Reactive` → Application (for IObservable in paper trading)
    - _Requirements: 14.1, 20.1, 21.1, 24.1_

  - [x] 1.2 Create `.kiro/steering/ai-standards.md`
    - Document AI assistant conventions: structured JSON output mode, retry semantics, system prompt file path, API key handling, SourceType tagging
    - _Requirements: 1.2, 1.3, 1.4, 24.3_

- [x] 2. Core layer — Paper Trading types and interfaces
  - [x] 2.1 Create `Core/PaperTrading/PaperTradingStatus.cs`
    - Define enum: Idle, Connecting, Running, Paused, Stopped, Error
    - _Requirements: 9.2_

  - [x] 2.2 Create `Core/PaperTrading/PaperBarEvent.cs` and `PaperTradeEvent.cs`
    - Immutable records with BarRecord/ClosedTrade, DateTimeOffset Timestamp, PortfolioSnapshot
    - _Requirements: 9.3, 9.4_

  - [x] 2.3 Create `Core/PaperTrading/PaperTradingResult.cs`
    - Record with FinalPortfolio, ClosedTrades, EquivalentBacktestResult, FinalStatus, StartedAt, StoppedAt
    - _Requirements: 10.3_

  - [x] 2.4 Create `Core/PaperTrading/IPaperTradingSession.cs`
    - Interface with Status, Portfolio, BarStream (IObservable), TradeStream (IObservable), StartAsync, StopAsync, PauseAsync, ResumeAsync
    - _Requirements: 9.1, 9.5_

  - [x] 2.5 Create `Core/DataHandling/IStreamingDataProvider.cs`
    - Interface extending IDataProvider with `StreamAsync(symbol, interval, ct)` returning `IAsyncEnumerable<BarRecord>`
    - _Requirements: 11.1_

- [-] 3. Core layer — Portfolio Backtesting types
  - [x] 3.1 Create `Core/Configuration/PortfolioRebalanceMode.cs`
    - Enum: None, EqualWeight, VolatilityParity
    - _Requirements: 18.2_

  - [x] 3.2 Create `Core/Configuration/PortfolioRiskConfig.cs`
    - Record with MaxPortfolioHeatPercent (default 20m), MaxCorrelationAllowed (default 0.85m), RebalanceMode (default None)
    - _Requirements: 18.2_

  - [x] 3.3 Create `Core/Configuration/PortfolioBacktestConfig.cs`
    - Record with Symbols (IReadOnlyList<DataConfig>), Strategies (IReadOnlyList<StrategyConfig>), PortfolioRisk, Execution, InitialCash, Seed, Timeframe
    - _Requirements: 18.1, 18.3, 18.4_

  - [x] 3.4 Create `Core/DataHandling/BarDataPool.cs`
    - Object pool using Microsoft.Extensions.ObjectPool and System.Buffers.ArrayPool<decimal>
    - Transparent to callers — IStrategy.OnMarketData signature unchanged
    - _Requirements: 21.1, 21.2_

- [x] 4. Checkpoint — Core layer complete
  - Ensure all Core types compile, run `dotnet build` on Core project. Ask the user if questions arise.

- [x] 5. Application layer — AI Strategy Assistant
  - [x] 5.1 Create `Application/Configuration/GeminiOptions.cs`
    - Record with ApiKey, ModelName (default "gemini-2.0-flash"), MaxRetries (default 2), SystemPromptFilePath (default "Prompts/strategy-assistant-system.md")
    - Bind via IOptions pattern
    - _Requirements: 24.1, 24.2, 24.3_

  - [x] 5.2 Create `Application/AI/AIStrategyDraft.cs`
    - Record with StrategyName, Hypothesis, StrategyType, Parameters (IReadOnlyDictionary<string, object>), SuggestedRisk, Rationale, Caveats (IReadOnlyList<string>), SourceType
    - _Requirements: 1.1, 1.8_

  - [x] 5.3 Create `Application/AI/IAIStrategyAssistant.cs`
    - Interface with GenerateStrategyAsync(prompt, ct) and RefineStrategyAsync(current, lastResult, refinementPrompt, ct)
    - _Requirements: 1.1, 2.1_

- [x] 6. Application layer — Strategy Export
  - [x] 6.1 Create `Application/Export/ExportFormat.cs`
    - Enum: MQL4, MQL5, PineScript
    - _Requirements: 4.1, 5.1, 6.1_

  - [x] 6.2 Create `Application/Export/ExportResult.cs`
    - Record with Format, FileName, Code, Warnings (IReadOnlyList<string>)
    - _Requirements: 4.3, 5.3, 6.3_

  - [x] 6.3 Create `Application/Export/IStrategyExporter.cs`
    - Interface with Format property and ExportAsync(StrategyVersion, ct) method
    - _Requirements: 4.1, 5.1, 6.1_

- [x] 7. Application layer — Indicator Library
  - [x] 7.1 Create `Application/Indicators/IIndicatorSeries.cs`
    - Generic interface IIndicatorSeries<TResult> with Add(BarRecord), Reset(), Results (IReadOnlyList<TResult>), IsWarm
    - _Requirements: 14.2, 14.3_

  - [x] 7.2 Create `Application/Indicators/SkenderIndicatorAdapter.cs`
    - Generic base class implementing bounded Queue<Quote> with capacity WarmupPeriod × 2
    - On each Add(): convert BarRecord → Quote, enqueue (dequeue oldest if at capacity), call Skender batch method on windowed contents
    - O(WarmupPeriod) per-bar cost
    - _Requirements: 14.3, 14.4_

  - [x] 7.3 Create indicator wrappers: SmaIndicator, EmaIndicator, RsiIndicator, MacdIndicator, BollingerBandsIndicator, AtrIndicator, StochasticIndicator, DonchianIndicator
    - Each extends SkenderIndicatorAdapter with correct warm-up period and Skender method delegation
    - Each carries XML doc comments describing formula and typical use
    - _Requirements: 15.1, 15.2, 15.3, 15.4_

  - [x] 7.4 Write property tests for indicator streaming vs batch equivalence
    - **Property 7: Indicator streaming matches batch computation**
    - **Validates: Requirements 14.3, 14.4, 15.4**

  - [x] 7.5 Write property test for indicator IsWarm transition
    - **Property 8: Indicator IsWarm transition**
    - **Validates: Requirements 15.2**

- [x] 8. Application layer — Paper Trading Session
  - [x] 8.1 Create `Application/PaperTrading/PaperSessionRecord.cs`
    - Record implementing IHasId with Id, StrategyVersionId, StartedAt, StoppedAt, Status, FinalPnl, TradeCount
    - _Requirements: 10.4_

  - [x] 8.2 Create `Application/PaperTrading/SimulatedPaperTradingSession.cs`
    - Implements IPaperTradingSession
    - Constructor: IStreamingDataProvider, IStrategy, IRiskLayer, IExecutionHandler, ISlippageModel, ICommissionModel, IRepository<PaperSessionRecord>, ILogger
    - State machine: Idle → Connecting → Running ⇄ Paused → Stopped | Error
    - Reuses same execution pipeline as BacktestEngine
    - Mark-to-market on every bar, emit PaperBarEvent/PaperTradeEvent via IObservable
    - StopAsync computes metrics via MetricsCalculator
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6_

  - [x] 8.3 Write property test for paper trading state machine validity
    - **Property 4: Paper trading state machine validity**
    - **Validates: Requirements 9.2, 10.6**

  - [x] 8.4 Write property test for paper trading metric equivalence
    - **Property 5: Paper trading metric equivalence**
    - **Validates: Requirements 10.1, 28.1**

  - [x] 8.5 Write property test for Paper StopAsync produces valid result
    - **Property 6: Paper StopAsync produces valid result**
    - **Validates: Requirements 10.3, 28.2**

- [x] 9. Application layer — Portfolio Backtest Runner
  - [x] 9.1 Create `Application/Portfolio/PortfolioBacktestResult.cs`
    - Record with SymbolResults, PortfolioResult, CorrelationMatrix, AnnualisedTurnover, RebalanceMode
    - _Requirements: 19.3, 19.4, 19.5, 19.6_

  - [x] 9.2 Create `Application/Portfolio/PortfolioBacktestRunner.cs`
    - Parallel per-symbol execution via Parallel.ForEachAsync with SemaphoreSlim cap (ProcessorCount - 1, min 1)
    - MergeEquityCurves: EqualWeight (1/N scaling), VolatilityParity (inverse-σ weighting), None (simple sum)
    - ComputeCorrelationMatrix: Pearson correlation, N×N symmetric, diagonal = 1.0
    - ComputeTurnover: annualised monthly position changes
    - Accepts CancellationToken and optional Seed for determinism
    - _Requirements: 19.1, 19.2, 19.3, 19.4, 19.5, 19.6, 19.7, 19.8_

  - [x] 9.3 Write property test for portfolio strategy-to-symbol mapping
    - **Property 10: Portfolio strategy-to-symbol mapping**
    - **Validates: Requirements 18.4**

  - [x] 9.4 Write property test for equity curve merge weight invariants
    - **Property 11: Equity curve merge weight invariants**
    - **Validates: Requirements 19.3**

  - [x] 9.5 Write property test for correlation matrix mathematical properties
    - **Property 12: Correlation matrix mathematical properties**
    - **Validates: Requirements 19.4, 27.2**

  - [x] 9.6 Write property test for portfolio turnover non-negative
    - **Property 13: Portfolio turnover non-negative**
    - **Validates: Requirements 19.6**

  - [x] 9.7 Write property test for portfolio determinism
    - **Property 14: Portfolio determinism**
    - **Validates: Requirements 19.8, 27.1**

  - [x] 9.8 Write property test for portfolio Sharpe diversification bound
    - **Property 15: Portfolio Sharpe diversification bound**
    - **Validates: Requirements 27.3**

- [x] 10. Checkpoint — Application layer complete
  - Ensure all Application types compile, run `dotnet build` on Application project. Ask the user if questions arise.

- [x] 11. Infrastructure layer — AI Strategy Assistant implementation
  - [x] 11.1 Create `Infrastructure/AI/GeminiStrategyAssistant.cs`
    - Implements IAIStrategyAssistant using Mscc.GenerativeAI client
    - Constructor: IOptions<GeminiOptions>, StrategyRegistry, ILogger
    - Loads system prompt from GeminiOptions.SystemPromptFilePath
    - Uses structured JSON output mode for reliable parsing
    - Retries once on unknown StrategyType with correction prompt containing KnownNames
    - Includes key metrics (Sharpe, MaxDrawdown, WinRate, TradeCount, DSR) in refinement context
    - Propagates CancellationToken to all async calls
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 2.1, 2.2, 2.3, 2.4_

  - [x] 11.2 Write unit tests for GeminiStrategyAssistant
    - Valid JSON → correct AIStrategyDraft deserialization
    - Unknown StrategyType → exactly one retry with correction prompt
    - CancellationToken → OperationCanceledException
    - Empty API key → graceful disable
    - Mock Gemini client via Moq
    - _Requirements: 25.1, 25.2, 25.3, 25.4_

  - [x] 11.3 Write property test for AI Strategy Draft JSON round-trip
    - **Property 1: AI Strategy Draft JSON round-trip**
    - **Validates: Requirements 1.1, 1.2**

  - [x] 11.4 Write property test for unknown strategy type retry
    - **Property 2: Unknown strategy type triggers exactly one retry**
    - **Validates: Requirements 1.5, 1.6**

- [x] 12. Infrastructure layer — Strategy Exporters
  - [x] 12.1 Create `Infrastructure/Export/MQL4StrategyExporter.cs`
    - Implements IStrategyExporter for ExportFormat.MQL4
    - Template-based code generation with OnInit(), OnTick(), OnDeinit()
    - Maps all 6 built-in strategies: MovingAverageCrossover, VolatilityScaledTrend, ZScoreMeanReversion, StationaryMeanReversion, DonchianBreakout, MacroRegime
    - Emits `// NOTE:` comments where exact equivalence is impossible
    - Returns empty Code with Warning for unsupported types
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [x] 12.2 Create `Infrastructure/Export/MQL5StrategyExporter.cs`
    - Implements IStrategyExporter for ExportFormat.MQL5
    - CTrade class pattern with OnTick() and OnTrade()
    - Maps all 6 built-in strategies
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 12.3 Create `Infrastructure/Export/PineScriptExporter.cs`
    - Implements IStrategyExporter for ExportFormat.PineScript
    - Pine Script v6 with strategy(), ta.* functions, strategy.entry(), strategy.close()
    - Maps all 6 built-in strategies
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [x] 12.4 Write unit tests for all 3 exporters × 6 strategies
    - Verify non-empty code for all 6 built-in strategy types per exporter (18 test cases)
    - Verify unsupported strategy type → empty Code + Warning
    - Verify missing parameters → defaults without exception
    - _Requirements: 26.1, 26.2, 26.3_

  - [x] 12.5 Write property test for export platform-specific structure
    - **Property 3: Export produces valid platform-specific structure**
    - **Validates: Requirements 4.1, 5.1, 6.1**

- [x] 13. Infrastructure layer — Streaming Data Provider
  - [x] 13.1 Create `Infrastructure/DataProviders/PollingStreamingDataProvider.cs`
    - Implements IStreamingDataProvider
    - Constructor: IDataProvider inner, TimeSpan pollInterval, double speedRatio
    - StreamAsync yields bars from inner provider at pollInterval / speedRatio
    - speedRatio = 1.0 → real-time; 10.0 → 10× faster
    - Accepts CancellationToken and terminates on cancellation
    - _Requirements: 11.2, 11.3, 11.4_

- [x] 14. Refactor built-in strategies to use indicator wrappers
  - [x] 14.1 Refactor MovingAverageCrossoverStrategy to use SmaIndicator/EmaIndicator
    - Replace inline circular-buffer computations with IIndicatorSeries wrappers
    - Call Add(bar) on indicator instances before computing signals
    - _Requirements: 16.1, 16.2_

  - [x] 14.2 Refactor VolatilityScaledTrendStrategy to use AtrIndicator and EmaIndicator
    - _Requirements: 16.1, 16.2_

  - [x] 14.3 Refactor ZScoreMeanReversionStrategy to use SmaIndicator and BollingerBandsIndicator
    - _Requirements: 16.1, 16.2_

  - [x] 14.4 Refactor StationaryMeanReversionStrategy to use SmaIndicator and BollingerBandsIndicator
    - _Requirements: 16.1, 16.2_

  - [x] 14.5 Refactor DonchianBreakoutStrategy to use DonchianIndicator
    - _Requirements: 16.1, 16.2_

  - [x] 14.6 Refactor MacroRegimeStrategy to use EmaIndicator and RsiIndicator
    - _Requirements: 16.1, 16.2_

  - [x] 14.7 Write regression integration tests for all 6 strategies
    - Run each strategy on fixed-seed dataset
    - Assert result metrics match pre-refactor values to 4 decimal places (1e-4 tolerance)
    - **Property 9: Strategy refactor regression equivalence**
    - **Validates: Requirements 16.3, 16.4**

- [x] 15. Checkpoint — Infrastructure and strategy refactor complete
  - Ensure all projects compile, run existing unit tests to verify no regressions. Ask the user if questions arise.

- [x] 16. API layer — Strategy Export endpoint
  - [x] 16.1 Create export endpoint in Api
    - POST `/strategies/{versionId}/export?format=MQL4|MQL5|PineScript`
    - Returns HTTP 200 with Content-Type text/plain and generated code
    - Returns HTTP 400 for invalid versionId or missing/invalid format
    - `.WithName("ExportStrategy")` and `.WithTags("Strategies")`
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 17. API layer — Portfolio Backtest endpoints
  - [x] 17.1 Create portfolio endpoints in Api
    - POST `/portfolios/run` → 200 PortfolioBacktestResult
    - POST `/portfolios/sweep` → 200 list of PortfolioBacktestResult
    - HTTP 400 for validation failures with structured error response
    - `.WithName()` and `.WithTags("Portfolios")` for OpenAPI
    - _Requirements: 23.1, 23.2, 23.3, 23.4_

- [x] 18. CLI layer — Paper Trading subcommand
  - [x] 18.1 Add `paper` subcommand to CLI
    - Accepts `--scenario` and optional `--speed` (default 1.0) flags
    - Starts paper trading session using specified scenario configuration
    - Prints live bar updates: symbol, bar close, current equity, open positions
    - On stop, writes Markdown report summarising session results
    - _Requirements: 13.1, 13.2, 13.3, 13.4_

- [x] 19. DI registration and wiring
  - [x] 19.1 Register new services in Application ServiceCollectionExtensions
    - Register IAIStrategyAssistant, IStrategyExporter (keyed by format), PortfolioBacktestRunner, SimulatedPaperTradingSession factory
    - Bind GeminiOptions from configuration
    - Log warning and disable AI features if ApiKey is null/empty
    - _Requirements: 24.2, 24.4_

  - [x] 19.2 Register Infrastructure implementations
    - Register GeminiStrategyAssistant, MQL4/MQL5/PineScript exporters, PollingStreamingDataProvider
    - _Requirements: 1.4, 11.2_

  - [x] 19.3 Wire BarDataPool into DataHandler and Portfolio.MarkToMarket
    - Ensure pooling is transparent to callers
    - _Requirements: 21.2_

- [x] 20. Checkpoint — All layers wired and compiling
  - Ensure full solution builds, run `dotnet build` on solution. Ask the user if questions arise.

- [x] 21. Web layer — AI Strategy Builder UI
  - [x] 21.1 Add "AI Assistant" button to Strategy Builder wizard Step 1
    - Opens dialog with natural-language text area and "Generate" button
    - On success, injects AIStrategyDraft fields into builder form
    - Displays Rationale and Caveats in advisory panel
    - Preserves all existing manual builder flows
    - _Requirements: 3.1, 3.2, 3.3_

  - [x] 21.2 Add "Refine with AI" button on Backtest Result Detail page
    - Visible when StrategyVersion.SourceType == AIGenerated
    - _Requirements: 3.4_

- [x] 22. Web layer — Strategy Export UI
  - [x] 22.1 Add "Export Strategy" panel to Strategy Detail page
    - Format selector (MQL4, MQL5, PineScript) and "Export" button
    - Display generated code in read-only text field with "Copy to Clipboard"
    - "Download" button with appropriate extension (.mq4, .mq5, .pine)
    - Display ExportResult.Warnings in advisory panel above code
    - _Requirements: 8.1, 8.2, 8.3, 8.4_

- [x] 23. Web layer — Paper Trading UI
  - [x] 23.1 Add "Paper Trading" section to sidebar navigation below "Prop-Firm"
    - _Requirements: 12.1_

  - [x] 23.2 Create Session Setup page
    - Strategy selector, data source config (symbol + timeframe), initial cash, realism profile, polling interval
    - _Requirements: 12.2_

  - [x] 23.3 Create Live Dashboard
    - Streaming equity curve chart, open positions table, recent trades table, key metrics card (PnL, Sharpe, win rate, trade count)
    - Pause and Stop buttons
    - 🧪 badge to distinguish from historical backtests
    - _Requirements: 12.3, 12.4, 12.6_

  - [x] 23.4 Create Session History page
    - List past sessions with start/stop times, final PnL, "Compare to Backtest" action
    - _Requirements: 12.5_

- [x] 24. Web layer — Portfolio Backtest UI
  - [x] 24.1 Add "Portfolio (Multi-Symbol)" toggle on New Run page
    - Symbol list with add/remove rows (data file + strategy selectors)
    - Portfolio risk settings, initial cash, execution profile inputs
    - _Requirements: 22.1, 22.2_

  - [x] 24.2 Create Portfolio Result Detail page
    - Portfolio-level equity curve, per-symbol performance cards
    - Correlation matrix heatmap (green=low, red=high)
    - Portfolio turnover metric
    - _Requirements: 22.3, 22.4_

- [x] 25. Web layer — Indicator Overlays
  - [x] 25.1 Add indicator overlay controls to Result Detail page
    - Multi-select control populated from available indicator wrappers
    - On-price indicators (SMA, EMA, Bollinger, Donchian) as additional traces on price chart
    - Oscillators (RSI, MACD, Stochastic) in separate subplot beneath price chart
    - Recompute indicators from backtest bar data before rendering
    - _Requirements: 17.1, 17.2, 17.3, 17.4_

- [x] 26. Checkpoint — Web and API layers complete
  - Ensure full solution builds, run all existing tests. Ask the user if questions arise.

- [x] 27. Unit tests — Paper Trading
  - [x] 27.1 Write unit tests for SimulatedPaperTradingSession
    - StopAsync → status Stopped + valid PaperTradingResult
    - CancellationToken → graceful stop
    - PauseAsync → portfolio state frozen
    - ResumeAsync → bar consumption resumes
    - Metric equivalence with BacktestResult for same data via mocked IStreamingDataProvider
    - _Requirements: 28.1, 28.2, 28.3_

- [x] 28. Integration tests — Portfolio Runner
  - [x] 28.1 Write integration tests for PortfolioBacktestRunner
    - Determinism: same seed + inputs → identical PortfolioBacktestResult
    - Correlation matrix symmetry (M[A][B] == M[B][A])
    - Portfolio Sharpe ≤ max(symbol Sharpes) when correlation > 0
    - 3-symbol run completes without error
    - _Requirements: 27.1, 27.2, 27.3_

- [x] 29. Integration tests — API endpoints
  - [x] 29.1 Write integration tests for new API endpoints
    - POST `/strategies/{versionId}/export` → 200 with code
    - POST `/strategies/{versionId}/export` with invalid versionId → 400
    - POST `/portfolios/run` → 200 with PortfolioBacktestResult
    - POST `/portfolios/sweep` → 200 with list
    - _Requirements: 7.1, 7.2, 23.1, 23.2_

- [x] 30. Benchmarks project
  - [x] 30.1 Create `src/TradingResearchEngine.Benchmarks/` project
    - Add to solution file
    - Reference BenchmarkDotNet, target net8.0, OutputType Exe
    - Reference Infrastructure and Application projects
    - _Requirements: 20.1_

  - [x] 30.2 Implement benchmark methods
    - SingleSymbol_1Year_Daily (252 bars)
    - SingleSymbol_1Year_H1 (6048 bars)
    - SingleSymbol_5Year_M15 (120960 bars)
    - PortfolioRun_5Symbols_1Year_Daily
    - ParameterSweep_10x10_Daily
    - Use [MemoryDiagnoser], [SimpleJob(RuntimeMoniker.Net80)], [Orderer(SummaryOrderPolicy.FastestToSlowest)]
    - Export results to `artifacts/benchmarks/` as markdown and JSON
    - _Requirements: 20.2, 20.3, 20.4_

  - [x] 30.3 Validate object pooling improvement
    - Run SingleSymbol_5Year_M15 benchmark before and after BarDataPool integration
    - Verify ≥ 20% reduction in allocated bytes per operation
    - _Requirements: 21.3_

- [x] 31. Final checkpoint — Full solution verification
  - Ensure all tests pass, run full solution build, verify no compiler warnings. Ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (15 properties total)
- Unit tests validate specific examples and edge cases
- The 6 built-in strategies: MovingAverageCrossover, VolatilityScaledTrend, ZScoreMeanReversion, StationaryMeanReversion, DonchianBreakout, MacroRegime
- SkenderIndicatorAdapter uses bounded Queue<Quote> of capacity WarmupPeriod × 2 for O(WarmupPeriod) per-bar cost
- Regression tests use 4 decimal places (1e-4 tolerance)
- StrategyConfig is already a standalone Core record — no extraction needed
