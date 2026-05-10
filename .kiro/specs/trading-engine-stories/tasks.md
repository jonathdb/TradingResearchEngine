# Implementation Plan: Trading Engine Stories

## Overview

This plan implements 22 stories (P0–P3) for the TradingResearchEngine in an order that minimizes merge conflicts. The implementation follows the established layer boundaries (Core ← Application ← Infrastructure ← Web), uses the `TradingResearchEngine.Core.Strategy` namespace (singular), and respects all architecture constraints. Property-based tests use FsCheck.Xunit with minimum 100 iterations; example-based tests use xUnit.

## Tasks

- [x] 1. Fix Sortino Ratio downside deviation formula (P0-4)
  - [x] 1.1 Fix `ComputeSortinoRatio` in `MetricsCalculator.cs` to use ALL returns with zeroed upside deviations
    - Replace the current filter-to-losing-periods logic with: `downsideDev = sqrt(mean(min(r - threshold, 0)^2 for all r))`
    - Accept `int barsPerYear` parameter for annualization
    - Return `null` only when `downsideDev == 0`, not when there are no losing bars
    - _Requirements: 4.1, 4.2, 4.3, 4.4_

  - [ ]* 1.2 Write property test for Sortino downside deviation (Property 5)
    - **Property 5: Sortino Downside Deviation Uses All Returns**
    - For any non-empty return series and any risk-free rate, verify the result matches the reference formula using all returns
    - Tag: `// Feature: trading-engine-stories, Property 5: Sortino Downside Deviation Uses All Returns`
    - `[Property(MaxTest = 100)]` in `MetricsCalculatorProperties.cs`
    - **Validates: Requirements 4.1, 4.4**

  - [ ]* 1.3 Write unit tests for Sortino fix
    - Test with known synthetic return series (mix of winning/losing) → expected hand-computed value
    - Test all-positive returns → non-null result (downsideDev > 0 when threshold > 0)
    - Test all-positive returns above threshold → null (downsideDev == 0)
    - _Requirements: 4.2, 4.3_

- [x] 2. Thread `BarsPerYear` into Calmar Ratio annualization (P0-5)
  - [x] 2.1 Fix `ComputeCalmarRatio` in `MetricsCalculator.cs` to accept `int barsPerYear`
    - Replace hardcoded 252 with `meanReturn * barsPerYear` annualization
    - Update `ComputeReturnOnMaxDrawdown` if it also hardcodes 252
    - Update all call sites to pass `config.BarsPerYear` or equivalent
    - _Requirements: 5.1, 5.2, 5.4_

  - [ ]* 2.2 Write property test for Calmar BarsPerYear annualization (Property 6)
    - **Property 6: Calmar Ratio BarsPerYear Annualization**
    - For any equity curve with 2+ points and any positive barsPerYear, verify proportional scaling
    - Tag: `// Feature: trading-engine-stories, Property 6: Calmar Ratio BarsPerYear Annualization`
    - `[Property(MaxTest = 100)]` in `MetricsCalculatorProperties.cs`
    - **Validates: Requirements 5.2, 5.3**

  - [ ]* 2.3 Write unit test for Calmar with different timeframes
    - M1 config (barsPerYear=131040) vs D1 config (barsPerYear=252) produce different correct values for same curve
    - _Requirements: 5.3_

- [x] 3. Fix `ComputeHistoricalVaR` small-sample boundary (P0-6)
  - [x] 3.1 Add minimum-sample guard to `ComputeHistoricalVaR` and `ComputeHistoricalCVaR`
    - Return `null` when `returns.Count < 30`
    - Keep correct index calculation for sufficient samples
    - _Requirements: 6.1, 6.2, 6.3_

  - [ ]* 3.2 Write property test for VaR/CVaR null guard (Property 7)
    - **Property 7: VaR/CVaR Small-Sample Null Guard**
    - For any equity curve with fewer than 30 period returns and any confidence level, both return null
    - Tag: `// Feature: trading-engine-stories, Property 7: VaR/CVaR Small-Sample Null Guard`
    - `[Property(MaxTest = 100)]` in `MetricsCalculatorProperties.cs`
    - **Validates: Requirements 6.1, 6.2**

  - [ ]* 3.3 Write property test for VaR correctness with sufficient samples (Property 8)
    - **Property 8: VaR Correctness for Sufficient Samples**
    - For any equity curve with 30+ returns and confidence in (0,1), verify non-null and correct sorted-index value
    - Tag: `// Feature: trading-engine-stories, Property 8: VaR Correctness for Sufficient Samples`
    - `[Property(MaxTest = 100)]` in `MetricsCalculatorProperties.cs`
    - **Validates: Requirements 6.3**

  - [ ]* 3.4 Write unit tests for VaR boundary cases
    - 15-bar curve at 95% confidence → null
    - 100-bar curve → non-null and correct value
    - _Requirements: 6.1, 6.3_

- [x] 4. Checkpoint — Metrics fixes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Fix short direction fill logic (P0-3)
  - [x] 5.1 Add `Direction.Short` branches to `TryFillLimit`, `TryFillStopMarket`, and `TryFillStopLimit`
    - Short limit: fill when `bar.High >= limitPrice` (sell at bid)
    - Short stop-market: fill when `bar.Low <= stopPrice`
    - Short stop-limit: trigger when `bar.Low <= stopPrice` AND fill when `bar.High >= limitPrice`
    - Apply bid-side pricing for short fills
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

  - [ ]* 5.2 Write property test for short limit fill (Property 2)
    - **Property 2: Short Limit Fill Correctness**
    - For any Direction.Short limit order with price L and any bar: fill iff `bar.High >= L`
    - Tag: `// Feature: trading-engine-stories, Property 2: Short Limit Fill Correctness`
    - `[Property(MaxTest = 100)]` in `FillEngineProperties.cs`
    - **Validates: Requirements 3.1, 3.4**

  - [ ]* 5.3 Write property test for short stop-market fill (Property 3)
    - **Property 3: Short Stop-Market Fill Correctness**
    - For any Direction.Short stop-market order with price S and any bar: fill iff `bar.Low <= S`
    - Tag: `// Feature: trading-engine-stories, Property 3: Short Stop-Market Fill Correctness`
    - `[Property(MaxTest = 100)]` in `FillEngineProperties.cs`
    - **Validates: Requirements 3.2, 3.4**

  - [ ]* 5.4 Write property test for short stop-limit fill (Property 4)
    - **Property 4: Short Stop-Limit Fill Correctness**
    - For any Direction.Short stop-limit order with stop S and limit L: fill iff `bar.Low <= S AND bar.High >= L`
    - Tag: `// Feature: trading-engine-stories, Property 4: Short Stop-Limit Fill Correctness`
    - `[Property(MaxTest = 100)]` in `FillEngineProperties.cs`
    - **Validates: Requirements 3.3, 3.4**

  - [ ]* 5.5 Write unit tests for short fill boundary cases
    - Each fill type × Direction.Short: conditions met, conditions not met, price exactly at trigger
    - Verify no regression on existing Direction.Long fill tests
    - _Requirements: 3.4, 3.5_

- [x] 6. Checkpoint — Fill logic fixes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Add `IStrategyFactory` interface for parallel isolation (P0-2)
  - [x] 7.1 Create `IStrategyFactory` in `src/TradingResearchEngine.Core/Strategy/IStrategyFactory.cs`
    - Namespace: `TradingResearchEngine.Core.Strategy` (singular, matching existing folder)
    - Define `string StrategyType { get; }` and `IStrategy Create(StrategyConfig config)`
    - XML doc comments on all public members
    - _Requirements: 2.1_

  - [x] 7.2 Implement factory classes for all concrete `IStrategy` implementations
    - Each strategy gets a corresponding factory (nested or sibling class)
    - Register all factories in `ServiceCollectionExtensions.cs`
    - _Requirements: 2.2_

  - [x] 7.3 Update `WalkForwardWorkflow` and `ParameterSweepWorkflow` to use `factory.Create(config)` per iteration
    - Replace any pattern that reuses a single IStrategy instance across parallel iterations
    - Each `Parallel.ForEachAsync` iteration calls `factory.Create(config)`
    - _Requirements: 2.3, 2.4_

  - [ ]* 7.4 Write property test for factory isolation (Property 1)
    - **Property 1: Factory Isolation — Concurrent Instances Produce Independent Results**
    - Create 20 instances concurrently, execute in parallel on same data, verify results match sequential
    - Tag: `// Feature: trading-engine-stories, Property 1: Factory Isolation`
    - `[Property(MaxTest = 100)]` in `StrategyFactoryProperties.cs`
    - **Validates: Requirements 2.2, 2.5**

- [x] 8. Move backtest execution off Blazor SignalR thread (P0-1)
  - [x] 8.1 Create `JobStatusPage.razor` at route `/backtests/job/{JobId}`
    - Poll `JobExecutor.GetStatusAsync(JobId)` every 2 seconds via `PeriodicTimer`
    - Show `MudProgressLinear` with percentage (indeterminate if unavailable)
    - Show state chip: Queued/Running/Completed/Failed with appropriate colors
    - On completion: auto-navigate to `/backtests/{resultId}` after 1-second delay
    - On failure: show `MudAlert Severity="Severity.Error"` with "Edit & Retry" button
    - _Requirements: 1.2, 1.3, 1.4_

  - [x] 8.2 Update `StrategyBuilder.razor` to enqueue jobs via `JobExecutor`
    - Replace all inline `await RunUseCase.RunAsync(...)` calls with job enqueue + navigate
    - Wire into existing `JobWorkerService` (already at `src/TradingResearchEngine.Application/Research/JobWorkerService.cs`)
    - _Requirements: 1.1, 1.5_

  - [ ]* 8.3 Write unit tests for job status transitions
    - Enqueue → Queued → Running → Completed/Failed lifecycle
    - _Requirements: 1.1, 1.2, 1.3, 1.4_

- [x] 9. Checkpoint — Async dispatch and factory wiring
  - Ensure all tests pass, ask the user if questions arise.

- [x] 10. Add `Reset()` and `Initialize()` lifecycle to `IStrategy` (P1-1)
  - [x] 10.1 Extend `IStrategy` interface with `Initialize(StrategyConfig config)` and `Reset()` methods
    - XML doc comments describing lifecycle semantics
    - _Requirements: 7.1, 7.2_

  - [x] 10.2 Implement `Initialize` and `Reset` on all concrete strategy classes
    - `Reset()` clears all indicator state and internal tracking
    - `Initialize(config)` sets parameters from config
    - _Requirements: 7.2, 7.3_

  - [x] 10.3 Update `WalkForwardWorkflow` to call `strategy.Reset()` between windows
    - Replace instance reconstruction with Reset() calls where appropriate
    - _Requirements: 7.4_

  - [ ]* 10.4 Write property test for strategy reset equivalence (Property 9)
    - **Property 9: Strategy Reset Equivalence**
    - Process N bars, call Reset(), process same N bars → identical output to fresh instance
    - Tag: `// Feature: trading-engine-stories, Property 9: Strategy Reset Equivalence`
    - `[Property(MaxTest = 100)]` in `StrategyLifecycleProperties.cs`
    - **Validates: Requirements 7.3**

- [x] 11. Paginate Dashboard "Recent Runs" (P1-3)
  - [x] 11.1 Add `ListRecentAsync(int count, CancellationToken ct)` to `IRepository<T>`
    - _Requirements: 9.1_

  - [x] 11.2 Implement `ListRecentAsync` in `SqliteIndexRepository<T>` with `ORDER BY ... DESC LIMIT @count`
    - _Requirements: 9.1_

  - [x] 11.3 Update `Dashboard.razor` to use `ListRecentAsync(10)` instead of loading all results
    - At most 2 repository queries, neither loading all results
    - Robustness flags still display correctly
    - _Requirements: 9.2, 9.3_

- [x] 12. Refactor `StudyDetail.razor` to pluggable renderer pattern (P1-2)
  - [x] 12.1 Create `IStudyResultRenderer` contract and `StudyRendererRegistry` in `Web/Components/Studies/`
    - Map each `StudyType` to a dedicated Blazor renderer component
    - _Requirements: 8.2_

  - [x] 12.2 Create individual renderer components for each study type
    - `MonteCarloResultRenderer.razor`, `WalkForwardResultRenderer.razor`, `SweepResultRenderer.razor`
    - `RealismResultRenderer.razor`, `BenchmarkResultRenderer.razor`, `CpcvResultRenderer.razor`, `VarianceResultRenderer.razor`
    - _Requirements: 8.2, 8.4_

  - [x] 12.3 Replace the `switch` block in `StudyDetail.razor` with `DynamicComponent` + registry lookup
    - No `switch` on `StudyType` for result rendering remains
    - _Requirements: 8.1, 8.3_

- [x] 13. Wire live study progress to UI (P1-4)
  - [x] 13.1 Subscribe `StudyDetail.razor` to `BackgroundStudyService.OnStudyProgress` and `OnStudyCompleted`
    - Show `MudProgressLinear` with percentage during Running state
    - Auto-hide progress and render results on completion
    - Implement `IDisposable` and unsubscribe in `Dispose()`
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [x] 13.2 Apply same progress pattern to `JobStatusPage.razor` for backtest job progress
    - _Requirements: 10.1_

- [x] 14. Checkpoint — Architecture and feedback loops
  - Ensure all tests pass, ask the user if questions arise.

- [x] 15. CPCV result visualization (P2-1)
  - [x] 15.1 Create `CpcvDistributionChart.razor` with Plotly histogram
    - Use `CpcvResult.OosSharpeDistribution` (the canonical field name) for histogram data
    - X-axis: OOS Sharpe ratio values; Y-axis: frequency count
    - Vertical dashed lines at median and zero
    - Color bars: red (Sharpe < 0), yellow (0 ≤ Sharpe < 1), green (Sharpe ≥ 1)
    - _Requirements: 11.1, 11.2_

  - [x] 15.2 Add percentile table (P10, P25, P50, P75, P90) below the histogram
    - Compute from `OosSharpeDistribution`
    - _Requirements: 11.3_

  - [x] 15.3 Verify `CpcvResult.OosSharpeDistribution` is populated by the CPCV workflow
    - If not already populated, wire it in the CPCV workflow
    - _Requirements: 11.4_

- [x] 16. Parameter sweep heatmap metric selector (P2-2)
  - [x] 16.1 Extend `SweepCell` record with `MaxDrawdown`, `WinRate`, `ProfitFactor`, `TotalTrades` fields
    - Populate all 5 metrics in the `ParameterSweepWorkflow`
    - _Requirements: 12.4_

  - [x] 16.2 Add `MudSelect` metric dropdown to `ParameterSweepHeatmap.razor`
    - Options: Sharpe Ratio, Max Drawdown, Win Rate, Profit Factor, Trade Count
    - Invert color scale for Max Drawdown (lower = better = green)
    - Reactive re-render without page reload
    - _Requirements: 12.1, 12.2, 12.3_

- [x] 17. Add 1-bar entry delay perturbation to sensitivity analysis (P2-3)
  - [x] 17.1 Add `FillDelayBars` property to `ExecutionConfig` (default 0)
    - _Requirements: 13.3_

  - [x] 17.2 Implement fill delay queue in the execution engine's bar processing
    - When `FillDelayBars > 0`, defer order submission by N bars
    - _Requirements: 13.2_

  - [x] 17.3 Add fill-delay dimension (0, 1, 2 bars) to `SensitivityWorkflow`
    - Label variants as "Delay 0 bars", "Delay 1 bar", "Delay 2 bars"
    - _Requirements: 13.1_

  - [x] 17.4 Expose `FillDelayBars` in `AdvancedOverridesPanel.razor`
    - _Requirements: 13.3_

  - [ ]* 17.5 Write property test for fill delay deferral (Property 10)
    - **Property 10: Fill Delay Deferral**
    - For any order and FillDelayBars = D > 0, order not eligible for fill until bar B + D
    - Tag: `// Feature: trading-engine-stories, Property 10: Fill Delay Deferral`
    - `[Property(MaxTest = 100)]` in `FillDelayProperties.cs`
    - **Validates: Requirements 13.2**

  - [ ]* 17.6 Write unit test for fill delay behavior
    - Delay of 1 bar → orders execute 1 bar later than without delay
    - _Requirements: 13.2, 13.4_

- [x] 18. Surface checklist score on Dashboard strategy cards (P2-4)
  - [x] 18.1 Inject `ResearchChecklistService` into `Dashboard.razor`
    - Call `EvaluateAsync` for each strategy's latest run
    - Display "X/9 checks" badge with color coding (≥7 green, 5-6 yellow, <5 red)
    - Add `MudTooltip` showing failed check names
    - Show "—" for strategies with no runs
    - _Requirements: 14.1, 14.2, 14.3, 14.4_

- [x] 19. Checkpoint — Robustness and research depth
  - Ensure all tests pass, ask the user if questions arise.

- [x] 20. AI Strategy Builder streaming response (P2-5)
  - [x] 20.1 Add `StreamGenerateAsync` and `StreamRefineAsync` to `IAIStrategyAssistant`
    - Return `IAsyncEnumerable<string>` for token-by-token streaming
    - _Requirements: 15.1_

  - [x] 20.2 Implement streaming in `GeminiStrategyAssistant` using Mscc.GenerativeAI streaming API
    - _Requirements: 15.1_

  - [x] 20.3 Update AI builder UI to consume `IAsyncEnumerable<string>` with live display
    - Maintain `_streamBuffer`, append tokens, call `StateHasChanged`
    - Show "Stop generation" button that cancels via `CancellationToken`
    - On completion, parse buffer as `AIStrategyDraft` JSON and populate form
    - _Requirements: 15.1, 15.2, 15.3, 15.4_

- [x] 21. AI Strategy Builder iterative refinement (P2-6)
  - [x] 21.1 Add "Refine with AI feedback" section to the builder UI
    - Text input + submit button, visible after draft generation
    - Call `StreamRefineAsync` with current draft as context
    - _Requirements: 16.1, 16.2_

  - [x] 21.2 Implement refinement history panel with revert capability
    - Store history in `AIStrategyDraft.RefinementHistory`
    - Allow user to click any previous version to revert
    - _Requirements: 16.3, 16.4_

  - [ ]* 21.3 Write property test for AIStrategyDraft JSON round-trip (Property 11)
    - **Property 11: AIStrategyDraft JSON Round-Trip**
    - For any valid AIStrategyDraft (including RefinementHistory), serialize → deserialize == original
    - Tag: `// Feature: trading-engine-stories, Property 11: AIStrategyDraft JSON Round-Trip`
    - `[Property(MaxTest = 100)]` in `AIStrategyDraftProperties.cs`
    - **Validates: Requirements 15.3, 16.4**

- [x] 22. Result-aware dynamic study interpretations (P2-7)
  - [x] 22.1 Create `StudyInterpretationService` in Application layer
    - Methods: `InterpretMonteCarlo`, `InterpretWalkForward`, `InterpretCpcv`, `InterpretParameterSweep`, `InterpretRealism`, `InterpretBenchmark`
    - Include specific numeric values from results
    - Apply warning thresholds: ruin > 5%, overfit > 50%, OOS Sharpe < 50% IS Sharpe, < 20% positive cells
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5_

  - [x] 22.2 Wire `StudyInterpretationService` into renderer components (replace static `GetInterpretation()`)
    - _Requirements: 17.5_

  - [ ]* 22.3 Write property test for interpretation threshold warnings (Property 12)
    - **Property 12: Interpretation Service Threshold Warnings**
    - For MonteCarloResult with RuinProbability > 0.05m → ruin warning present
    - For CpcvResult with ProbabilityOfOverfitting > 0.50m → overfitting warning present
    - For WalkForwardResult with OOS Sharpe < 50% IS Sharpe → degradation warning present
    - Tag: `// Feature: trading-engine-stories, Property 12: Interpretation Service Threshold Warnings`
    - `[Property(MaxTest = 100)]` in `StudyInterpretationProperties.cs`
    - **Validates: Requirements 17.2, 17.3, 17.4**

  - [ ]* 22.4 Write unit tests for interpretation service
    - Each study type with known results → expected text fragments
    - _Requirements: 17.1_

- [x] 23. Checkpoint — AI and interpretations
  - Ensure all tests pass, ask the user if questions arise.

- [x] 24. Universal Skender indicator bridge (P3-5)
  - [x] 24.1 Create `SkenderIndicatorCatalog` with 40+ indicator descriptors
    - Parameters, output fields, category metadata for each indicator
    - Pre-compiled delegate invoker factories (zero reflection in hot path)
    - _Requirements: 22.2, 22.3, 22.4_

  - [x] 24.2 Create `SkenderBridgeIndicator` implementing `IIndicatorSeries<decimal>`
    - Constructor: `(string indicatorKey, Dictionary<string, object> parameters, string? outputField)`
    - Methods: `Add(BarRecord bar)`, `Reset()`, properties: `Results`, `IsWarm`
    - Use pre-compiled delegates from catalog — zero reflection during bar processing
    - _Requirements: 22.1, 22.2_

  - [x] 24.3 Register catalog entries in `IndicatorRegistry.All`
    - _Requirements: 22.4_

  - [x] 24.4 Create `IndicatorPickerPanel.razor` with category filtering, text search, and "Add to strategy" action
    - _Requirements: 22.5_

  - [ ]* 24.5 Write property test for Skender bridge output equivalence (Property 17)
    - **Property 17: Skender Bridge Output Equivalence**
    - For any valid bar sequence (length ≥ warmup) and supported indicator config, bridge output == direct Skender output
    - Tag: `// Feature: trading-engine-stories, Property 17: Skender Bridge Output Equivalence`
    - `[Property(MaxTest = 100)]` in `SkenderBridgeProperties.cs`
    - **Validates: Requirements 22.1**

  - [ ]* 24.6 Write performance benchmark for Skender bridge (Property 18)
    - **Property 18: Skender Bridge Performance Bound**
    - 100,000 bars with MACD config: bridge completes within 2× of hand-written MacdIndicator
    - Invariant: no reflection in the hot path (relative benchmark, not fixed time cap)
    - Tag: `// Feature: trading-engine-stories, Property 18: Skender Bridge Performance Bound`
    - BenchmarkDotNet test in integration tests
    - **Validates: Requirements 22.6**

- [x] 25. Builder step persistence (P3-1)
  - [x] 25.1 Implement auto-save of `CurrentStep` and `MaxVisitedStep` on `ConfigDraft`
    - Debounce at 500ms
    - Restore wizard to saved step on load
    - Prevent skipping forward to unvisited steps
    - _Requirements: 18.1, 18.2, 18.3_

  - [ ]* 25.2 Write property test for builder step persistence (Property 13)
    - **Property 13: Builder Step Persistence and Navigation Guard**
    - For any ConfigDraft with CurrentStep=S and MaxVisitedStep=M, loading restores to S, navigation > M prevented
    - Tag: `// Feature: trading-engine-stories, Property 13: Builder Step Persistence and Navigation Guard`
    - `[Property(MaxTest = 100)]` in `ConfigDraftProperties.cs`
    - **Validates: Requirements 18.2, 18.3**

- [x] 26. Robustness flag tooltips (P3-2)
  - [x] 26.1 Create `RobustnessWarningCatalog` with plain-English explanations for all warning types
    - `GetExplanation(label)` returns catalog entry or raw label as fallback (never throws)
    - _Requirements: 19.2, 19.3_

  - [x] 26.2 Add `MudTooltip` to robustness warning chips on Dashboard
    - Display explanation from catalog on hover
    - _Requirements: 19.1_

  - [ ]* 26.3 Write property test for warning catalog fallback (Property 14)
    - **Property 14: Warning Catalog Fallback**
    - For any string label, `GetExplanation` returns non-null without throwing
    - Tag: `// Feature: trading-engine-stories, Property 14: Warning Catalog Fallback`
    - `[Property(MaxTest = 100)]` in `RobustnessWarningCatalogProperties.cs`
    - **Validates: Requirements 19.2, 19.3**

- [x] 27. Recent runs table sorting and filtering (P3-3)
  - [x] 27.1 Add sorting (ascending/descending) on Sharpe, MaxDrawdown, TradeCount columns
    - Client-side sorting, no additional repository queries
    - _Requirements: 20.1, 20.4_

  - [x] 27.2 Add strategy type filter chips and "Show failed runs" toggle
    - Reactive filtering without page reload
    - _Requirements: 20.2, 20.3, 20.4_

  - [ ]* 27.3 Write property test for dashboard sorting correctness (Property 15)
    - **Property 15: Dashboard Sorting Correctness**
    - For any list of BacktestResult and any sortable column, ascending order ≤ invariant holds
    - Tag: `// Feature: trading-engine-stories, Property 15: Dashboard Sorting Correctness`
    - `[Property(MaxTest = 100)]` in `DashboardSortingProperties.cs`
    - **Validates: Requirements 20.1**

  - [ ]* 27.4 Write property test for dashboard filtering correctness (Property 16)
    - **Property 16: Dashboard Filtering Correctness**
    - Strategy type filter returns only matching items; "Show failed" toggle excludes failed items
    - Tag: `// Feature: trading-engine-stories, Property 16: Dashboard Filtering Correctness`
    - `[Property(MaxTest = 100)]` in `DashboardFilteringProperties.cs`
    - **Validates: Requirements 20.2, 20.3**

- [x] 28. Strategy library empty state (P3-4)
  - [x] 28.1 Add structured empty state to `StrategyLibrary.razor`
    - Research lifecycle explanation
    - "Start from Template" and "Use AI Builder" buttons
    - Hidden when strategies exist
    - _Requirements: 21.1, 21.2, 21.3_

- [x] 29. Checkpoint — UX polish
  - Ensure all tests pass, ask the user if questions arise.

- [x] 30. Final checkpoint — Full integration verification
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (18 total)
- Unit tests validate specific examples and edge cases
- Implementation order follows the spec: P0-4→P0-5→P0-6 (metrics), P0-3 (fills), P0-2 (factory), P0-1 (async), P1-1→P1-3→P1-2→P1-4 (architecture), P2-1→P2-2→P2-3→P2-4 (robustness), P2-5→P2-6→P2-7 (AI/interpretations), P3-5 (Skender bridge), P3-1→P3-2→P3-3→P3-4 (UX polish)
- Namespace: `TradingResearchEngine.Core.Strategy` (singular) — do NOT use "Strategies" (plural)
- CPCV field: use `OosSharpeDistribution` everywhere — do NOT create a separate `PathSharpeRatios` field
- Skender bridge benchmark: "within 2× of hand-written MacdIndicator" (relative, not fixed time cap)
- `JobWorkerService` already exists — wire it into the new flow, do not recreate
