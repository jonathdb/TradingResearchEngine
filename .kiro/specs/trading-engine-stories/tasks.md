# Implementation Plan: Trading Engine Stories

## Overview

This plan implements 23 stories (P0–P2) for the TradingResearchEngine — covering critical bug fixes in job dispatch, fill logic, and metrics; architecture improvements for parallelism and extensibility; UI/UX enhancements; robustness research features; and strategy creation tooling. Implementation uses .NET 8, C# 12, Blazor Server, MudBlazor, xUnit + FsCheck.Xunit for tests.

## Tasks

- [x] 1. Async Backtest Job Dispatch
  - [x] 1.1 Create JobExecutor singleton in Application/Engine/
    - Implement `JobExecutor.cs` with `EnqueueAsync`, `GetStatusAsync`, `GetResultIdAsync`, `GetErrorAsync` methods
    - Track jobs via `ConcurrentDictionary<Guid, JobStatus>`
    - Execute backtests on background `Task.Run` with `CancellationToken`
    - Clean up jobs older than 24 hours
    - Register as singleton in `ServiceCollectionExtensions.cs`
    - _Requirements: 1.1, 1.6_

  - [x] 1.2 Create JobStatus page (Web)
    - Create `src/TradingResearchEngine.Web/Pages/JobStatus.razor` with route `/job-status/{JobId:guid}`
    - Poll `JobExecutor.GetStatusAsync(JobId)` every 2 seconds using `PeriodicTimer`
    - Show `MudProgressLinear` indeterminate bar while Running
    - Auto-redirect to `/runs/{resultId}` after 1-second delay when Completed
    - Show `MudAlert Severity.Error` with error message and "Edit & Retry" button when Failed
    - Dispose `PeriodicTimer` and `CancellationTokenSource` in `IAsyncDisposable.DisposeAsync`
    - _Requirements: 1.2, 1.3, 1.4, 1.5, 1.7_

  - [x] 1.3 Remove inline RunAsync calls from StrategyBuilder.razor
    - Replace all `await RunUseCase.RunAsync(...)` calls with `JobExecutor.Enqueue(config)` + `NavManager.NavigateTo`
    - Verify zero inline `RunAsync` calls remain
    - _Requirements: 1.8_

  - [x] 1.4 Write unit tests for JobExecutor
    - Test that `Enqueue` returns a Guid immediately without awaiting the backtest
    - Test status transitions: Queued → Running → Completed/Failed
    - Test error message retrieval on failure
    - _Requirements: 1.1, 1.5_

- [x] 2. IStrategyFactory for Parallel Isolation
  - [x] 2.1 Define IStrategyFactory interface in Core/Strategy/
    - Create `IStrategyFactory.cs` in `TradingResearchEngine.Core.Strategy` namespace (singular)
    - Define `IStrategy Create(StrategyConfig config)` method
    - Define `string StrategyType { get; }` property
    - _Requirements: 2.1_

  - [x] 2.2 Update BacktestEngine to accept IStrategyFactory
    - Modify constructor to accept `IStrategyFactory` instead of `IStrategy` directly
    - Call `factory.Create(config)` inside `RunAsync` before the event loop
    - _Requirements: 2.2_

  - [x] 2.3 Update WalkForwardWorkflow and ParameterSweepWorkflow
    - Each parallel iteration calls `factory.Create(iterationConfig)` — never reuse a single IStrategy
    - Update DI registrations in `ServiceCollectionExtensions.cs`
    - _Requirements: 2.4, 2.5_

  - [x] 2.4 Write property test for Factory Isolation (Property 1)
    - **Property 1: Factory Isolation — Concurrent Instances Produce Independent Results**
    - Create N instances concurrently, execute in parallel on same bar data, verify results identical to sequential
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 2.2, 2.5**

- [x] 3. Stop-Limit Triggered State Persistence
  - [x] 3.1 Add TriggeredOrder to ExecutionResult
    - Add `OrderEvent? TriggeredOrder` property to `ExecutionResult` record
    - In `TryFillStopLimit`, when triggered but not filled, return `ExecutionResult` with `TriggeredOrder: order with { StopTriggered = true }`
    - In `ProcessPendingOrders`, use `result?.TriggeredOrder ?? order` when re-queuing
    - _Requirements: 3.1, 3.2, 3.3, 3.4_

  - [x] 3.2 Write unit test for stop-limit triggered state persistence
    - Test that a stop-limit order triggering on bar N but missing the limit fills correctly on bar N+1
    - Test that `StopTriggered = true` persists across bars
    - _Requirements: 3.1, 3.4_

- [x] 4. Synthetic Bar Timeframe Fix
  - [x] 4.1 Fix CreateFillAtPrice to use actual timeframe
    - Replace hardcoded `"1D"` with timeframe from `state.LastMarketEvent` (or current BarEvent)
    - Fall back to `"1D"` only for tick-only data (no prior BarEvent)
    - _Requirements: 4.1, 4.2, 4.3_

  - [x] 4.2 Write unit test for timeframe propagation
    - Test that `CreateFillAtPrice` uses the timeframe from the current `BarEvent`
    - Test fallback to "1D" when no prior BarEvent exists
    - _Requirements: 4.1, 4.2_

- [x] 5. PendingOrders Allocation Optimization
  - [x] 5.1 Implement swap buffer pattern in RunState
    - Add pre-allocated `_pendingSwap` buffer to RunState
    - Rewrite `ProcessPendingOrders` to populate swap buffer as "remaining" list and swap references
    - Zero `new List<OrderEvent>()` allocations per bar
    - _Requirements: 5.1, 5.2, 5.3_

- [x] 6. Checkpoint — Ensure P0 critical bugs pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Historical VaR Small-Sample Guard
  - [x] 7.1 Add minimum sample guard to VaR/CVaR
    - Define `private const int MinSampleForPercentile = 30` in MetricsCalculator
    - Return `null` from `ComputeHistoricalVaR` when returns count < 30
    - Return `null` from `ComputeHistoricalCVaR` when returns count < 30
    - _Requirements: 6.1, 6.2, 6.3, 6.4_

  - [x] 7.2 Write property test for VaR Null Guard (Property 7)
    - **Property 7: VaR/CVaR Small-Sample Null Guard**
    - For any equity curve with fewer than 30 period returns, both VaR and CVaR return null
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 6.1, 6.2**

  - [x] 7.3 Write property test for VaR Correctness (Property 8)
    - **Property 8: VaR Correctness for Sufficient Samples**
    - For any equity curve with 30+ returns, VaR equals negated return at `floor((1-confidence)*count)` index
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 6.3**

- [x] 8. IProgress<T> Surface on IBacktestEngine
  - [x] 8.1 Add IProgress<ProgressUpdate> parameter to RunAsync
    - Update `IBacktestEngine.RunAsync` signature with optional `IProgress<ProgressUpdate>? progress = null`
    - Emit progress every `Math.Max(1, totalBars / 100)` bars (~100 updates per run)
    - Skip reporting with zero overhead when `progress` is null
    - Ensure `ProgressUpdate` record carries `BarsProcessed` and `TotalBars` fields
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 9. IStrategy Lifecycle Hooks
  - [x] 9.1 Add Initialize and Reset methods to IStrategy
    - Add `void Initialize(ScenarioConfig config)` with default empty implementation
    - Add `void Reset()` with default empty implementation
    - No breaking changes to existing `OnMarketData` signature
    - _Requirements: 8.1, 8.2, 8.6_

  - [x] 9.2 Implement lifecycle hooks in BacktestEngine and workflows
    - Call `strategy.Initialize(config)` before the event loop in `RunAsync`
    - Update `WalkForwardWorkflow` to call `strategy.Reset()` before each OOS window
    - Implement `Initialize` and `Reset` in all concrete strategy classes
    - _Requirements: 8.3, 8.4, 8.5_

  - [x] 9.3 Write property test for Strategy Reset Equivalence (Property 9)
    - **Property 9: Strategy Reset Equivalence**
    - Process N bars, call Reset(), process same N bars → output identical to freshly constructed instance
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 8.3**

- [x] 10. Consolidate Strategies/Strategy Namespaces
  - [x] 10.1 Merge Application/Strategy/ into Application/Strategies/
    - Move all files from `Application/Strategy/` into `Application/Strategies/`
    - Update all namespace declarations and `using` directives
    - Delete empty `Application/Strategy/` folder
    - Verify solution builds with zero errors
    - _Requirements: 9.1, 9.2, 9.3_

- [x] 11. Inject LoggerFactory into BacktestEngine
  - [x] 11.1 Add ILoggerFactory to BacktestEngine constructor
    - Accept `ILoggerFactory loggerFactory` parameter in constructor
    - Pass to `Portfolio` and `DataHandler` during construction inside `RunAsync`
    - Update DI registration in `ServiceCollectionExtensions.cs`
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

- [x] 12. Checkpoint — Ensure architecture tasks pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. Live Study Progress Display
  - [x] 13.1 Add progress event mechanism to BackgroundStudyService
    - Add `event Action<Guid, ProgressUpdate>? OnProgress` to BackgroundStudyService
    - Add `event Action<Guid>? OnStudyCompleted` event
    - Emit progress for Monte Carlo, Parameter Sweep, Walk-Forward, and CPCV study types
    - _Requirements: 11.2_

  - [x] 13.2 Subscribe to progress events in StudyDetail.razor
    - Subscribe to `BackgroundStudyService.OnProgress` in `OnInitializedAsync`
    - Show `MudProgressLinear` with "X of N simulations complete" label
    - Call `await InvokeAsync(StateHasChanged)` on each matching studyId update
    - Hide progress bar and render results on completion
    - Unsubscribe in `IAsyncDisposable.DisposeAsync`
    - _Requirements: 11.1, 11.3, 11.4, 11.5_

- [x] 14. Builder Step Persistence on Refresh
  - [x] 14.1 Add step persistence to ConfigDraft and StrategyBuilder
    - Add `CurrentStep` and `MaxVisitedStep` properties to `ConfigDraft`
    - Auto-save `CurrentStep` to draft (debounced at 500ms)
    - On load, restore wizard to saved `CurrentStep`
    - Enforce `CurrentStep <= MaxVisitedStep` to prevent skipping unvisited steps
    - Use existing draft repository — no localStorage/sessionStorage
    - _Requirements: 12.1, 12.2, 12.3, 12.4_

  - [x] 14.2 Write property test for Builder Step Persistence (Property 13)
    - **Property 13: Builder Step Persistence and Navigation Guard**
    - For any ConfigDraft with CurrentStep=S and MaxVisitedStep=M, loading restores to step S, navigation to step > M is prevented
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 12.2, 12.3**

- [x] 15. Robustness Flag Tooltips
  - [x] 15.1 Create RobustnessWarningCatalog and wire tooltips
    - Create `RobustnessWarningCatalog.cs` in Application layer with explanations for all warning types
    - Include at minimum: "High Sharpe", "Low Trades", "K-Ratio < 0"
    - `GetExplanation(label)` returns catalog text or raw label as fallback (never throws)
    - Wrap warning chips in `MudTooltip` in Dashboard.razor and StudyDetail.razor
    - _Requirements: 13.1, 13.2, 13.3, 13.4_

  - [x] 15.2 Write property test for Warning Catalog Fallback (Property 14)
    - **Property 14: Warning Catalog Fallback**
    - For any string label, `GetExplanation(label)` returns non-null without throwing
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 13.2, 13.3**

- [x] 16. Dashboard Checklist Score Badge
  - [x] 16.1 Add checklist badge to strategy cards
    - Call `ChecklistService.Evaluate(run)` for each run after `ListRecentAsync`
    - Display "X/9 checks" badge: green ≥7, yellow 5–6, red <5
    - Show tooltip listing failed check names on hover
    - Display "—" when no runs exist for a strategy
    - _Requirements: 14.1, 14.2, 14.3, 14.4_

- [ ] 17. Dashboard Recent Runs Sorting and Filtering
  - [x] 17.1 Add sorting and filtering to recent runs table
    - Use `MudTable` with `SortLabel` on Sharpe, Max Drawdown, Trade Count columns
    - Add strategy filter `MudChip` buttons for reactive filtering by strategy type
    - Add "Show failed runs" `MudSwitch` toggle
    - All operations on in-memory result set — no additional repository queries
    - _Requirements: 15.1, 15.2, 15.3, 15.4_

  - [x] 17.2 Write property test for Dashboard Sorting (Property 15)
    - **Property 15: Dashboard Sorting Correctness**
    - For any list of BacktestResult items and any sortable column, ascending sort produces monotonically non-decreasing keys
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 15.1**

  - [x] 17.3 Write property test for Dashboard Filtering (Property 16)
    - **Property 16: Dashboard Filtering Correctness**
    - Applying strategy type filter returns only matching items; toggling "Show failed" off excludes failed items
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 15.2, 15.3**

- [x] 18. Strategy Library Empty State
  - [x] 18.1 Implement empty state in StrategyLibrary.razor
    - When strategy list is empty, show structured empty state with research lifecycle explanation
    - Include "Start from Template" button → `/builder` and "Use AI Builder" button → `/builder?mode=ai`
    - Hide empty state when strategies exist
    - _Requirements: 16.1, 16.2, 16.3_

- [x] 19. Checkpoint — Ensure UI/UX tasks pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 20. CPCV Distribution Visualization
  - [x] 20.1 Add CPCV histogram and percentile table
    - Use `OosSharpeDistribution` field (existing canonical name) on `CpcvResult`
    - Create `CpcvResultRenderer.razor` with Plotly histogram of OOS path Sharpe ratios
    - Color bars: red (Sharpe < 0), yellow (0 ≤ Sharpe < 1), green (Sharpe ≥ 1)
    - Add vertical dashed lines at zero and median Sharpe
    - Render percentile table: P10, P25, P50, P75, P90
    - _Requirements: 17.1, 17.2, 17.3, 17.4_

- [x] 21. Parameter Sweep Heatmap Metric Selector
  - [x] 21.1 Add multi-metric support to sweep heatmap
    - Ensure `SweepCell` carries all 5 metrics: SharpeRatio, MaxDrawdown, WinRate, ProfitFactor, TotalTrades
    - Add `MudSelect<string>` metric selector above heatmap with 5 options
    - Invert color scale for Max Drawdown (lower = green)
    - Re-render reactively on metric change without page reload
    - _Requirements: 18.1, 18.2, 18.3, 18.4_

- [x] 22. Fill Delay in Sensitivity Analysis
  - [x] 22.1 Add FillDelayBars perturbation dimension
    - Add `FillDelayBars` to `ExecutionConfig` (default 0)
    - Add `FillDelayBars` as standard perturbation dimension in `SensitivityWorkflow` with values `[0, 1, 2]`
    - Implement order deferral logic: orders not eligible for fill until bar B + D
    - Expose `FillDelayBars` in Advanced Overrides panel of StrategyBuilder.razor (numeric input, 0–5)
    - Include fill-delay results in sensitivity result renderer
    - _Requirements: 19.1, 19.2, 19.3, 19.4_

  - [x] 22.2 Write property test for Fill Delay Deferral (Property 10)
    - **Property 10: Fill Delay Deferral**
    - For any order and FillDelayBars=D>0, order not eligible for fill until bar B+D
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 19.2**

- [x] 23. Pluggable Study Result Renderer Registry
  - [x] 23.1 Create StudyRendererRegistry and refactor StudyDetail.razor
    - Create `StudyRendererRegistry` mapping `StudyType → Type` (Blazor component type)
    - Replace `switch` statement in StudyDetail.razor with `DynamicComponent` pattern
    - Extract each existing switch case into dedicated renderer component
    - Register in `ServiceCollectionExtensions.cs`
    - Ensure fallback "Unknown study type" message when renderer not found
    - _Requirements: 20.1, 20.2, 20.3, 20.4, 20.5_

- [x] 24. AI Strategy Streaming and Refinement
  - [x] 24.1 Add streaming methods to IAIStrategyAssistant
    - Add `IAsyncEnumerable<string> StreamGenerateAsync(string prompt, CancellationToken ct)` method
    - Add `IAsyncEnumerable<string> StreamRefineAsync(AIStrategyDraft current, string feedback, CancellationToken ct)` method
    - Add `RefinementHistory` property to `AIStrategyDraft` record
    - Implement in `GeminiStrategyAssistant` (Infrastructure)
    - _Requirements: 21.1, 21.5, 21.6_

  - [x] 24.2 Wire streaming UI in StrategyBuilder.razor (AI mode)
    - Stream tokens via `StateHasChanged()` on each token
    - Show "Stop generation" button that cancels via `CancellationToken`
    - Parse completed stream as `AIStrategyDraft` and auto-populate form fields
    - Show "Refine with AI feedback" text input when draft exists
    - Display refinement history in collapsible `MudExpansionPanel` with "Revert to this version" buttons
    - _Requirements: 21.2, 21.3, 21.4, 21.7_

  - [x] 24.3 Write property test for AIStrategyDraft JSON Round-Trip (Property 11)
    - **Property 11: AIStrategyDraft JSON Round-Trip**
    - For any valid AIStrategyDraft (including RefinementHistory), serialize → deserialize produces equivalent object
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 21.6**

- [x] 25. Result-Aware Dynamic Study Interpretations
  - [x] 25.1 Create StudyInterpretationService
    - Create `StudyInterpretationService.cs` in `Application/Research/`
    - Implement threshold-based interpretation for Monte Carlo (ruin > 5%), CPCV (overfit > 50%), Walk-Forward (OOS < 50% IS)
    - Include actual numeric values in interpretation text
    - Register as scoped in `ServiceCollectionExtensions.cs`
    - _Requirements: 22.1, 22.2, 22.3, 22.4, 22.5_

  - [x] 25.2 Wire interpretations into study renderer components
    - Inject `StudyInterpretationService` into renderer components
    - Render interpretation text in `MudAlert` below charts
    - Remove all inline static interpretation strings from Razor components
    - _Requirements: 22.1, 22.5_

  - [x] 25.3 Write property test for Interpretation Threshold Warnings (Property 12)
    - **Property 12: Interpretation Service Threshold Warnings**
    - MonteCarloResult with RuinProbability > 0.05 → ruin warning present
    - CpcvResult with ProbabilityOfOverfitting > 0.50 → overfitting warning present
    - WalkForwardResult with OOS Sharpe < 50% IS Sharpe → degradation warning present
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 22.2, 22.3, 22.4**

- [x] 26. Universal Skender Indicator Bridge
  - [x] 26.1 Create SkenderBridgeIndicator with pre-compiled delegates
    - Create `SkenderBridgeIndicator.cs` in `Application/Indicators/`
    - Use pre-compiled `Expression<Func<...>>` delegates — zero reflection during per-bar processing
    - Maintain internal `List<Quote>` window; append Quote per bar and call Skender method
    - Return latest result's specified output field
    - _Requirements: 23.1, 23.2_

  - [x] 26.2 Create SkenderIndicatorCatalog with 40+ entries
    - Create `SkenderIndicatorCatalog.cs` describing 40+ indicators
    - Include: MACD, ADX, Stochastic, Williams %R, OBV, CCI, Supertrend, Keltner Channel, RSI, Bollinger Bands, ATR, EMA, SMA, Donchian, and more
    - Each entry: Name, Category, Description, Parameters (with defaults/min/max), OutputFields
    - Pre-compile all delegates at catalog initialization time
    - Register all catalog indicators in `IndicatorRegistry.All`
    - _Requirements: 23.3, 23.4_

  - [x] 26.3 Create IndicatorPickerPanel component
    - Add `IndicatorPickerPanel.razor` to StrategyBuilder
    - Category filter chips (Trend, Momentum, Volatility, Volume)
    - Text search input
    - Scrollable list of matching indicator cards
    - "Add to strategy" button appending selected indicator with default parameters
    - _Requirements: 23.5_

  - [x] 26.4 Write property test for Skender Bridge Output Equivalence (Property 17)
    - **Property 17: Skender Bridge Output Equivalence**
    - For any valid bar sequence ≥ warmup period, bridge output equals direct Skender extension method output
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 23.1**

  - [x] 26.5 Write benchmark test for Skender Bridge Performance (Property 18)
    - **Property 18: Skender Bridge Performance Bound**
    - 100,000 bars through MACD bridge completes within 2× wall-clock time of hand-written MacdIndicator
    - Relative benchmark — not fixed time cap
    - **Validates: Requirements 23.6**

- [x] 27. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (FsCheck.Xunit, minimum 100 iterations)
- Unit tests validate specific examples and edge cases (xUnit + Moq)
- Core namespace for strategy types: `TradingResearchEngine.Core.Strategy` (singular)
- CPCV field: use `OosSharpeDistribution` (existing canonical name), not `PathSharpeRatios`
- `JobWorkerService` already exists — wire it into the new status page flow, don't recreate
- Skender bridge benchmark: relative to hand-written adapter (within 2×), not fixed time cap
- All services registered in `ServiceCollectionExtensions.cs`
- No localStorage/sessionStorage — use draft persistence via existing repository
- No breaking changes to `IStrategy.OnMarketData`
