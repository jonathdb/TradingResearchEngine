# Implementation Plan: PR Gate Implementation Plan

## Overview

This implementation plan covers 35+ requirements across 10 sequential PR gates for the TradingResearchEngine. Each gate is a coherent, reviewable unit that must build cleanly, pass all tests, and align documentation before the next gate begins. The implementation follows the existing clean architecture (Core ← Application ← Infrastructure ← Web) using .NET 8 / C# 12. Gates 9 and 10 address findings from the v2 code review (bug fixes, code quality improvements, and new research capabilities).

## Tasks

- [x] 1. PR Gate 1 — Walk-Forward Correctness
  - [x] 1.1 Implement OptimizationObjective enum and ParameterGrid/ParameterRange records
    - Create `Application/Research/OptimizationObjective.cs` with `Sharpe`, `CAGR`, `MAR` values
    - Create `Application/Research/ParameterGrid.cs` with `ParameterGrid` and `ParameterRange` records
    - _Requirements: 1.1, 1.6, 1.7_
  - [x] 1.2 Implement GridOptimizer with objective-based ranking
    - Create `Application/Research/GridOptimizer.cs`
    - Implement `Optimize` method that selects the parameter combination producing the highest configured objective value
    - Handle undefined objectives by excluding candidates with structured `ExcludedCandidate` explanations
    - Never fall through to a different objective when the configured one is undefined
    - _Requirements: 1.1, 1.4, 1.8, 1.9_
  - [ ]* 1.3 Write property test for grid optimization selects maximum objective
    - **Property 1: Walk-Forward Grid Optimization Selects Maximum Objective**
    - **Validates: Requirements 1.1, 1.4**
  - [ ]* 1.4 Write property test for invalid parameter grid produces structured error
    - **Property 2: Invalid Parameter Grid Produces Structured Error**
    - **Validates: Requirements 1.3**
  - [ ]* 1.5 Write property test for undefined objective excludes candidate without fallthrough
    - **Property 3: Undefined Objective Excludes Candidate Without Fallthrough**
    - **Validates: Requirements 1.8, 1.9, 25.6**
  - [x] 1.6 Extend WalkForwardOptions with Grid and Objective properties
    - Add `ParameterGrid? Grid` and `OptimizationObjective Objective` (default Sharpe) to `WalkForwardOptions`
    - Extend `WalkForwardWindow` record with `SelectedParameters`, `OptimizationMetricValue`, `UsedObjective`
    - _Requirements: 1.5, 1.6, 1.7_
  - [x] 1.7 Implement walk-forward in-sample grid optimization in WalkForwardWorkflow
    - Modify `WalkForwardWorkflow.RunAsync` to evaluate all parameter combinations per IS window when grid is provided
    - Select best combination via `GridOptimizer` and apply to corresponding OOS window
    - Ensure independent optimization per window without cross-window information leakage
    - _Requirements: 1.1, 1.2, 1.4, 1.5_
  - [x] 1.8 Implement walk-forward pre-run validation in PreflightValidator
    - Validate data range accommodates at least one complete IS+OOS window pair
    - Reject with structured error stating minimum required data length when insufficient
    - Report expected window count before execution begins
    - Emit warning when fewer than 2 windows (limited statistical significance)
    - _Requirements: 2.1, 2.2, 2.3, 2.4_
  - [ ]* 1.9 Write property test for walk-forward window count formula
    - **Property 4: Walk-Forward Window Count Formula**
    - **Validates: Requirements 2.1, 2.2, 2.3**
  - [ ]* 1.10 Write unit tests for walk-forward grid optimization and validation
    - Test default objective is Sharpe; CAGR/MAR selectable
    - Test fewer than 2 windows emits warning
    - Test empty/invalid grid returns structured error
    - Test independent window optimization
    - _Requirements: 1.1–1.9, 2.1–2.4_

- [x] 2. Checkpoint — Gate 1 complete
  - Ensure all tests pass, ask the user if questions arise.


- [x] 3. PR Gate 2 — Indicator Fixes & Validation
  - [x] 3.1 Implement final validation confirmation gate in FinalValidationUseCase
    - Extend `FinalValidationUseCase.ExecuteAsync` with `bool userConfirmed` parameter
    - Return `Cancelled` when user declines; return `AlreadyConsumed` when test set already used
    - Display clear explanation of consequences before requesting confirmation
    - Disable/relabel final validation action after test set is consumed
    - _Requirements: 3.1, 3.2, 3.3, 3.4_
  - [x] 3.2 Implement research checklist as active workflow guide
    - Extend `ResearchChecklistService` to surface incomplete items with prominent visual indicators
    - Provide direct navigation paths to relevant workflows for incomplete items
    - Display low-confidence explanations (not just numeric scores)
    - Integrate checklist state into final validation flow with gating warnings
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5_
  - [x] 3.3 Audit and fix indicator catalog completeness
    - Audit `SkenderIndicatorCatalog.BuildCatalog()` — remove entries whose factory returns null
    - Add startup validation step that iterates all entries and logs warnings for failures
    - Ensure `Strategy_Builder` only displays functional indicators
    - _Requirements: 5.1, 5.2, 5.3, 5.4_
  - [ ]* 3.4 Write property test for indicator catalog completeness
    - **Property 18: Indicator Catalog Completeness**
    - **Validates: Requirements 5.1, 5.3**
  - [x] 3.5 Remove obsolete LongOnlyGuard documentation drift
    - Remove or rewrite XML docs/comments referencing LongOnlyGuard as active runtime behavior
    - Update test assertions to reflect V6+ bidirectional execution support
    - Mark `LongOnlyGuard` class with `[Obsolete]` attribute referencing replacement mechanism
    - _Requirements: 6.1, 6.2, 6.3_
  - [x] 3.6 Implement beginner-mode realism defaults
    - Add `DefaultRealismProfile` property to `StrategyTemplate` defaulting to `StandardBacktest`
    - Ensure strategy builder wizard uses this in beginner mode
    - Add explanatory text describing default realism settings
    - Keep advanced overrides accessible but not prominent in beginner flow
    - _Requirements: 7.1, 7.2, 7.3, 7.4_
  - [ ]* 3.7 Write unit tests for Gate 2 components
    - Test final validation: confirmation required; declined cancels; already consumed blocks
    - Test research checklist: incomplete items have navigation paths; low confidence has explanation
    - Test beginner mode: defaults to StandardBacktest; never FastResearch
    - _Requirements: 3.1–3.4, 4.1–4.5, 7.1–7.4_

- [x] 4. Checkpoint — Gate 2 complete
  - Ensure all tests pass, ask the user if questions arise.


- [x] 5. PR Gate 3 — Performance & Concurrency
  - [x] 5.1 Implement ConcurrencyBudget with SemaphoreSlim
    - Create `Application/Research/ConcurrencyBudget.cs` wrapping `SemaphoreSlim`
    - Implement `AcquireAsync` returning `IDisposable` releaser with CancellationToken support
    - Create `ConcurrencyOptions` class bound via `IOptions<T>` with default `Environment.ProcessorCount`
    - Register as singleton in DI
    - _Requirements: 9.1, 9.2, 9.3_
  - [x] 5.2 Implement Portfolio hot-path optimization with cached snapshots
    - Add cached `IReadOnlyList<Position>` snapshots and `_openPositionCount` field to `Portfolio`
    - Implement `InvalidateSnapshots()` called on state changes
    - Implement lazy `RebuildSnapshots()` on property access
    - Provide O(1) `OpenPositionCount` access
    - Maintain identical observable behavior verified by existing property tests
    - _Requirements: 8.1, 8.2, 8.3, 8.4_
  - [ ]* 5.3 Write property test for portfolio optimization preserves correctness
    - **Property 19: Portfolio Optimization Preserves Correctness**
    - Verify existing properties (cash conservation, equity curve length, risk layer traceability) still pass
    - **Validates: Requirements 8.4**
  - [x] 5.4 Parallelize Monte Carlo workflow with deterministic seeding
    - Pre-generate per-iteration seeds sequentially from master RNG
    - Dispatch simulations via `Parallel.ForEachAsync` with `ConcurrencyBudget`
    - Collect results into pre-allocated indexed array
    - Preserve block bootstrap behavior and progress reporting
    - _Requirements: 10.1, 10.2, 10.3, 10.4, 10.5_
  - [x] 5.5 Parallelize CPCV workflow with fold isolation
    - Dispatch fold evaluations via `Parallel.ForEachAsync` with `ConcurrencyBudget`
    - Each fold creates its own engine instance (no shared mutable state)
    - Collect into indexed array and aggregate after all folds complete
    - Preserve progress reporting correctness
    - _Requirements: 11.1, 11.2, 11.3, 11.4, 11.5_
  - [x] 5.6 Parallelize Parameter Perturbation workflow with deterministic jitter
    - Pre-generate jitter seeds sequentially from master RNG
    - Dispatch perturbation runs in parallel with `ConcurrencyBudget`
    - Collect into indexed array; report correct total run count
    - _Requirements: 12.1, 12.2, 12.3, 12.4_
  - [ ]* 5.7 Write property test for seeded workflow determinism under concurrency
    - **Property 5: Seeded Workflow Determinism Under Concurrency**
    - **Validates: Requirements 9.4, 9.5, 10.2, 10.3, 12.2, 12.3**
  - [ ]* 5.8 Write property test for CPCV fold aggregation order-independence
    - **Property 6: CPCV Fold Aggregation Order-Independence**
    - **Validates: Requirements 11.3, 11.4, 11.5**
  - [x] 5.9 Implement provider-aware progress estimation
    - Extend `IDataProvider` with `EstimateBarCountAsync` method
    - Update `DataHandler` to use provider estimate when available, fallback to date-range calculation
    - Refine estimate as actual bars are consumed during execution
    - _Requirements: 13.1, 13.2, 13.3, 13.4_
  - [ ]* 5.10 Write unit tests for concurrency and progress estimation
    - Test ConcurrencyBudget: acquire/release; cancellation; bounded permits
    - Test parallel Monte Carlo produces same results as sequential (seeded)
    - Test progress estimation uses provider estimate when available
    - _Requirements: 9.1–9.7, 10.1–10.5, 13.1–13.4_

- [x] 6. Checkpoint — Gate 3 complete
  - Ensure all tests pass, ask the user if questions arise.


- [x] 7. PR Gate 4 — Configuration & Construction
  - [x] 7.1 Implement ScenarioConfigNormalizer for canonicalization
    - Create `Application/Configuration/ScenarioConfigNormalizer.cs`
    - Transform legacy flat fields into canonical V5+ sub-object shape in memory
    - Do NOT modify legacy files on disk during load; persist canonical shape only on explicit save
    - Single validation path for canonical configurations
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5, 14.6_
  - [ ]* 7.2 Write property test for ScenarioConfig normalization preserves semantics
    - **Property 7: ScenarioConfig Normalization Preserves Semantics**
    - **Validates: Requirements 14.1, 16.4**
  - [x] 7.3 Unify strategy construction through StrategyRegistry
    - Remove any remaining `Activator.CreateInstance` calls that bypass the registry
    - Add `StrategyRegistry.VerifyAll()` startup verification attempting instantiation of all registered strategies
    - Ensure `ReflectionStrategyFactory` delegates to `StrategyRegistry.Resolve`
    - Consistent initialization regardless of entry point (UI builder, config file, API)
    - _Requirements: 15.1, 15.2, 15.3, 15.4_
  - [x] 7.4 Implement typed provider configuration with IOptions<T>
    - Create `CsvDataProviderOptions`, `HttpDataProviderOptions`, `DukascopyDataProviderOptions`
    - Bind via `IOptions<T>` in `ServiceCollectionExtensions`
    - Replace scattered string-key dictionary access in DataHandler, workflows, and providers
    - Maintain backward compatibility at JSON ingestion boundary via compatibility adapter
    - _Requirements: 16.1, 16.2, 16.3, 16.4_
  - [ ]* 7.5 Write unit tests for Gate 4 components
    - Test config normalization: legacy format transforms correctly; canonical passes through
    - Test strategy registry: VerifyAll catches broken strategies; no bypass paths
    - Test typed config: missing required value produces startup error; backward compat works
    - _Requirements: 14.1–14.6, 15.1–15.4, 16.1–16.4_

- [x] 8. Checkpoint — Gate 4 complete
  - Ensure all tests pass, ask the user if questions arise.


- [x] 9. PR Gate 5 — Persistence & Resilience
  - [x] 9.1 Implement AI call timeout and cancellation in GeminiClient
    - Extend `GeminiOptions` with `TimeSpan CallTimeout` (default 60s)
    - Use linked `CancellationTokenSource` combining caller token and timeout token
    - Catch `OperationCanceledException` when timeout fires and throw descriptive `TimeoutException`
    - Preserve existing retry behavior for transient failures while respecting per-call timeout
    - _Requirements: 17.1, 17.2, 17.3, 17.4_
  - [x] 9.2 Implement job retry policy and failure handling
    - Create `Application/Research/RetryPolicy.cs` with configurable max retries, backoff, and multiplier
    - Extend `JobStatus` enum with `Retrying` state
    - Extend `BacktestJob` with `RetryCount` and `JobFailureType?` fields
    - Implement retry loop in `JobWorkerService.ProcessJobAsync`: transient → retry with backoff; terminal → immediate `Failed`
    - Log structured diagnostics for each retry attempt and final failure
    - Sanitized user-visible error message on final failure
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5_
  - [ ]* 9.3 Write property test for job retry bounded termination
    - **Property 8: Job Retry Bounded Termination**
    - **Validates: Requirements 18.4**
  - [x] 9.4 Implement ConsistencyReconciler for SQLite/JSON reconciliation
    - Create `Infrastructure/Persistence/ConsistencyReconciler.cs`
    - On startup, verify consistency between SQLite index and JSON store for all indexed entity types
    - JSON store is source of truth: add missing entries to index, remove orphaned index entries
    - Log structured diagnostics identifying mismatched entities and corrective actions
    - Invoke via `IHostedService` before application accepts requests
    - _Requirements: 19.1, 19.2, 19.3, 19.4_
  - [ ]* 9.5 Write property test for JSON store data preservation during reconciliation
    - **Property 9: JSON Store Data Preservation During Reconciliation**
    - **Validates: Requirements 19.4**
  - [x] 9.6 Implement configurable paper-trading polling
    - Create `Application/Configuration/PaperTradingOptions.cs` with interval, min, and max bounds
    - Use `IOptionsMonitor<PaperTradingOptions>` for hot-reload support
    - Validate interval bounds (not zero, not excessively large)
    - Apply new interval without requiring restart where feasible
    - _Requirements: 20.1, 20.2, 20.3, 20.4_
  - [ ]* 9.7 Write unit tests for Gate 5 components
    - Test AI timeout: short timeout triggers TimeoutException; cancellation propagates
    - Test job retry: transient retries with backoff; terminal fails immediately; structured logs
    - Test paper trading: default interval; validation rejects zero/excessive; hot-reload applies
    - Test reconciliation: JSON wins over SQLite; orphans removed; no data loss
    - _Requirements: 17.1–17.4, 18.1–18.5, 19.1–19.4, 20.1–20.4_

- [x] 10. Checkpoint — Gate 5 complete
  - Ensure all tests pass, ask the user if questions arise.


- [x] 11. PR Gate 6 — Repository Cleanup
  - [x] 11.1 Audit and clean Prompts directory
    - Identify prompt files actively referenced by `PromptLoader` in production code
    - Remove or relocate archival prompt-engineering artifacts not used at runtime
    - Update all file path references in code and configuration
    - _Requirements: 21.1, 21.2, 21.3_
  - [x] 11.2 Remove obsolete CLI/API/Web transition leftovers
    - Remove or relocate obsolete assets referencing removed entry points
    - Document reason for any intentionally retained samples
    - _Requirements: 22.1, 22.2_
  - [x] 11.3 Align documentation and specs with implemented reality
    - Update README, CHANGELOG, and docs to reflect current architecture and entry points
    - Remove stale XML doc comments that no longer reflect the code
    - Mark completed tasks in `.kiro/specs/*/tasks.md` files
    - Remove comments describing planned-but-not-implemented behavior without TODO/PLANNED prefix
    - _Requirements: 23.1, 23.2, 23.3, 23.4, 22.3_

- [x] 12. Checkpoint — Gate 6 complete
  - Ensure all tests pass, ask the user if questions arise.


- [ ] 13. PR Gate 7 — Research Analytics Expansion
  - [ ] 13.1 Implement expanded Monte Carlo simulation modes
    - Create `MonteCarloSimulationMode` enum with `TradeResample`, `BlockBootstrap`, `ReturnSeries`
    - Add `SimulationMode` property to `MonteCarloOptions`
    - Implement `ReturnSeries` mode: resample equity curve period returns directly
    - Dispatch to mode-specific logic based on selection; never mix approaches
    - _Requirements: 24.1, 24.2, 24.3, 24.5_
  - [ ]* 13.2 Write property test for Monte Carlo mode isolation
    - **Property 20: Monte Carlo Mode Isolation**
    - **Validates: Requirements 24.5**
  - [ ] 13.3 Implement enriched walk-forward analytics
    - Create `WalkForwardAnalytics` record with OOS profitability rate, concatenated equity curve, parameter drift score
    - Compute OOS profitability rate as fraction of profitable OOS windows
    - Stitch individual OOS equity curves chronologically into concatenated curve
    - Compute parameter drift score quantifying parameter changes across windows
    - Add `WalkForwardAnalytics` property to `WalkForwardResult`
    - _Requirements: 25.1, 25.2, 25.3, 25.4, 25.5, 25.6_
  - [ ]* 13.4 Write property test for OOS profitability rate computation
    - **Property 10: OOS Profitability Rate Computation**
    - **Validates: Requirements 25.1**
  - [ ]* 13.5 Write property test for concatenated OOS equity curve chronological continuity
    - **Property 11: Concatenated OOS Equity Curve Chronological Continuity**
    - **Validates: Requirements 25.2**
  - [ ] 13.6 Implement trade anatomy analytics (MAE/MFE/Duration)
    - Create `Core/Portfolio/TradeAnatomy.cs` record with MAE, MFE, Duration
    - Extend `ClosedTrade` with optional `TradeAnatomy? Anatomy` field
    - Compute MAE/MFE from intra-trade price data when `TraceOptions.EnableEventTrace` is active
    - Set `Anatomy` to null when trace data is unavailable (never produce incorrect values)
    - Compute duration distribution analytics
    - _Requirements: 26.1, 26.2, 26.3, 26.4, 26.5_
  - [ ]* 13.7 Write property test for trade excursion computation (MAE/MFE)
    - **Property 12: Trade Excursion Computation (MAE/MFE)**
    - **Validates: Requirements 26.1, 26.2, 26.3**
  - [ ] 13.8 Implement correlation-aware portfolio constraints
    - Extend `PortfolioRiskConfig` with `MaxPairwiseCorrelation` and `CorrelationLookbackBars`
    - Create `Application/Risk/CorrelationConstraintEnforcer.cs`
    - Integrate into `DefaultRiskLayer` — evaluate correlation before approving orders
    - Reject/defer positions violating constraints; log structured diagnostics
    - _Requirements: 27.1, 27.2, 27.3, 27.4_
  - [ ]* 13.9 Write property test for correlation constraint enforcement
    - **Property 13: Correlation Constraint Enforcement**
    - **Validates: Requirements 27.1, 27.2**
  - [ ] 13.10 Implement persistent comparison report generation
    - Create `Application/Export/ComparisonReportGenerator.cs`
    - Generate Markdown comparison reports with key metrics, equity curves, summary statistics
    - Persist Markdown artifact to configured output location
    - Optional HTML export when enabled via `ComparisonReportOptions`
    - Integrate into comparison UI as accessible action
    - _Requirements: 28.1, 28.2, 28.3, 28.4, 28.5, 28.6_
  - [ ]* 13.11 Write property test for comparison report completeness
    - **Property 14: Comparison Report Completeness**
    - **Validates: Requirements 28.1, 28.2**
  - [ ]* 13.12 Write unit tests for Gate 7 analytics
    - Test Monte Carlo modes: each mode produces expected statistical characteristics
    - Test walk-forward analytics: OOS rate, concatenated curve, parameter drift
    - Test trade anatomy: MAE/MFE computed correctly; null when no trace data
    - Test correlation enforcement: rejection logged; allowed when within bounds
    - Test comparison report: all metrics present; Markdown well-formed
    - _Requirements: 24.1–24.5, 25.1–25.6, 26.1–26.5, 27.1–27.4, 28.1–28.6_

- [ ] 14. Checkpoint — Gate 7 complete
  - Ensure all tests pass, ask the user if questions arise.


- [ ] 15. PR Gate 8 — Engine Capability Expansion
  - [ ] 15.1 Implement AI refinement loop with backtest context
    - Extend `GeminiStrategyAssistant.RefineStrategyAsync` with optional `BacktestResult` parameter
    - Extract key metrics (Sharpe, max drawdown, win rate, trade count, K-Ratio) and append concise summary to refinement prompt
    - Keep within token budget constraints
    - Make backtest context inclusion visible in refinement UI flow
    - _Requirements: 29.1, 29.2, 29.3, 29.4_
  - [ ] 15.2 Implement large sweep result usability with virtualization
    - Use `PagedResult<T>` for sweep results in Application layer
    - Implement virtualized rendering via Blazor `Virtualize` component in Web layer
    - Preserve chart and summary views without loading all results into DOM
    - Support filtering and sorting without full client-side data loading
    - _Requirements: 30.1, 30.2, 30.3, 30.4_
  - [ ] 15.3 Consolidate comparison page
    - Provide one canonical comparison route and page
    - Remove obsolete/dead comparison routes
    - Differentiate remaining components clearly in navigation and titles
    - Update documentation and navigation to reflect consolidated flow
    - _Requirements: 31.1, 31.2, 31.3, 31.4_
  - [ ] 15.4 Implement multi-timeframe strategy support
    - Extend `ScenarioConfig` with `IReadOnlyList<SecondaryTimeframeConfig>? SecondaryTimeframes`
    - Create `IMultiTimeframeStrategy` interface extending `IStrategy` with `OnSecondaryBar`
    - Implement `MultiTimeframeDataHandler` merging bars from all timeframes chronologically
    - Extend engine heartbeat loop to interleave secondary timeframe bars in chronological order
    - Validate all specified timeframe data sources are available before execution
    - _Requirements: 32.1, 32.2, 32.3, 32.4, 32.5_
  - [ ]* 15.5 Write property test for multi-timeframe event chronological ordering
    - **Property 16: Multi-Timeframe Event Chronological Ordering**
    - **Validates: Requirements 32.2**
  - [ ] 15.6 Implement export validation for Pine Script and MQL
    - Create `Application/Export/ExportValidator.cs`
    - Validate structural correctness: matching braces, required sections (Pine: `//@version`, `strategy()`; MQL: `OnInit`, `OnTick`)
    - Report specific validation errors with line, section, and message
    - Include regression test fixtures for known-good and known-bad patterns
    - _Requirements: 33.1, 33.2, 33.3, 33.4_
  - [ ]* 15.7 Write property test for export validation correctness
    - **Property 17: Export Validation Correctness**
    - **Validates: Requirements 33.1, 33.2**
  - [ ] 15.8 Implement expression compiler negative testing
    - Add comprehensive negative test coverage for `ExpressionCompiler`
    - Cover: missing operators, unbalanced parentheses, invalid identifiers, empty expressions, deeply nested expressions
    - All malformed inputs must produce descriptive `ExpressionCompileError` (no unhandled exceptions)
    - _Requirements: 34.1, 34.2, 34.3_
  - [ ]* 15.9 Write property test for expression compiler rejects all malformed inputs
    - **Property 15: Expression Compiler Rejects All Malformed Inputs**
    - **Validates: Requirements 34.1, 34.2, 34.3**
  - [ ] 15.10 Implement reference multi-timeframe strategy
    - Create at least one concrete strategy implementing `IMultiTimeframeStrategy`
    - Demonstrate multi-timeframe execution end-to-end (higher-timeframe context for lower-timeframe decisions)
    - _Requirements: 32.3_
  - [ ]* 15.11 Write unit and integration tests for Gate 8 components
    - Test AI refinement: backtest context included; token budget respected
    - Test export validation: known-good passes; known-bad fails with specific errors
    - Test multi-timeframe: bars delivered chronologically; missing source returns structured error
    - Test expression compiler: all malformed inputs produce descriptive errors
    - _Requirements: 29.1–29.4, 32.1–32.5, 33.1–33.4, 34.1–34.3_

- [ ] 16. Checkpoint — Gate 8 complete
  - Ensure all tests pass, ask the user if questions arise.


- [ ] 19. PR Gate 9 — Code Quality & Async Correctness
  - [ ] 19.1 Rename OptimizationObjective.CAGR to TotalReturn or compute true annualised CAGR
    - `GridOptimizer.ComputeCagr` currently computes total return, not annualised CAGR
    - Either rename to `TotalReturn` (and update exclusion messages) or compute true CAGR using `BarsPerYear` and window length
    - Ensure cross-window comparison is coherent regardless of IS window length
    - _Review: Follow-up 2_
  - [ ] 19.2 Replace synchronous File.WriteAllText with File.WriteAllTextAsync in ReportExporter
    - `ExportMarkdownAsync`, `ExportTradeCsvAsync`, `ExportEquityCsvAsync`, `ExportJsonAsync` all use synchronous I/O
    - Replace with `File.WriteAllTextAsync` to avoid blocking thread-pool threads under load
    - _Review: Opp 8_
  - [ ] 19.3 Add MaxPromptLength guard to GeminiStrategyAssistant
    - Add `MaxPromptLength` property to `GeminiOptions` (default 30000 chars)
    - Validate combined system prompt + user message length before API call
    - Return descriptive error when exceeded rather than opaque API failure
    - _Review: Opp 9_
  - [ ] 19.4 Resolve commented-out metrics in BacktestResult (VaR95, CVaR95, OmegaRatio, UlcerIndex)
    - Determine if these are intentionally deferred or unintentionally omitted
    - If deferred: document as known gap in CHANGELOG with tracking reference
    - If ready: restore computation in MetricsCalculator and uncomment fields
    - _Review: Opp 10_
  - [ ] 19.5 Add ExportComparisonMarkdownAsync to IReportExporter
    - `MarkdownReporter.RenderToMarkdown(ComparisonReport)` produces a string but never persists it
    - Add `Task<string> ExportComparisonMarkdownAsync(ComparisonReport, CancellationToken)` to `IReportExporter`
    - Implement in `ReportExporter` and call from `ScenarioComparisonUseCase`
    - _Review: Bug 3_
  - [ ] 19.6 Migrate StrategyRegistry default parameter inference from reflection to attribute-based schema
    - Replace `switch` on `typeof(int)` / `typeof(decimal)` with `[StrategyParameter(default: 14)]` attribute
    - Or implement `IStrategyParameterSchema` static interface method for explicit defaults
    - Make defaults schema-driven rather than inferred at runtime
    - _Review: Opp 6_
  - [ ]* 19.7 Write unit tests for Gate 9 components
    - Test CAGR/TotalReturn objective coherence across different window lengths
    - Test async file I/O does not block
    - Test prompt length guard returns descriptive error
    - Test comparison report persistence

- [ ] 20. Checkpoint — Gate 9 complete
  - Ensure all tests pass, ask the user if questions arise.


- [ ] 21. PR Gate 10 — Research Depth & Developer Experience
  - [ ] 21.1 Add MAE/MFE fields to ClosedTrade and wire engine tracking
    - Add `decimal MaxAdverseExcursion` and `decimal MaxFavorableExcursion` to `ClosedTrade`
    - Track running high-water mark and low-water mark of unrealised P&L between entry and exit
    - Enable edge ratio, R-multiple distribution, entry/exit quality scoring downstream
    - _Review: Bug 4, Opp 1_
  - [ ] 21.2 Add concatenated OOS equity curve to WalkForwardResult
    - Add `IReadOnlyList<EquityCurvePoint> ConcatenatedOosEquityCurve` computed property
    - Stitch OOS curves by appending each window's OOS equity curve in window index order
    - _Review: Opp 2_
  - [ ] 21.3 Add OOS profitability rate to WalkForwardSummary
    - Compute `decimal OosProfitabilityRate` as profitable OOS windows / total windows
    - High IS Sharpe + low OOS profitability rate = strong overfitting signal
    - _Review: Opp 3_
  - [ ] 21.4 Add multi-criteria ranking to ScenarioComparisonUseCase
    - Create `ComparisonFilter` record with `MinWinRate`, `MinTrades`, `MaxDrawdown`
    - Add optional sort key (Calmar, Sharpe, etc.) for filtered survivors
    - Preserve existing single-metric best-of logic as default
    - _Review: Opp 4_
  - [ ] 21.5 Implement strategy version side-by-side comparison
    - Enable comparing two `StrategyVersion` IDs with metric deltas
    - Pin results to specific strategy versions (distinct from arbitrary BacktestResult comparison)
    - Display deltas in all metrics across both versions
    - _Review: Opp 5_
  - [ ] 21.6 Migrate DataProviderOptions to discriminated union type
    - Replace `Dictionary<string, object>` in `ScenarioConfig.DataProviderOptions` with sealed discriminated union
    - `CsvDataProviderConfig | HttpDataProviderConfig | DukascopyDataProviderConfig`
    - Eliminate all remaining string key usage; make malformed configs a compile-time error
    - Maintain JSON backward compatibility at deserialization boundary
    - _Review: Opp 7_
  - [ ] 21.7 Add end-to-end integration test for walk-forward → OOS → persist cycle
    - Run `WalkForwardWorkflow` against sample CSV data
    - Verify OOS windows are populated and result is persisted and retrievable
    - _Review: Opp 11_
  - [ ] 21.8 Add observable job queue depth metrics
    - Create `IJobQueueMetrics` interface with `PendingCount`, `RunningCount`, `FailedCount`
    - Source from `JobExecutor` progress cache and repository queries
    - Expose via health check endpoint or structured log
    - _Review: Opp 12_
  - [ ] 21.9 Add architecture dependency enforcement test
    - Create `ArchitectureDependencyTests.cs` using `NetArchTest.Rules` or equivalent
    - Enforce Core ← Application ← Infrastructure ← Web dependency rule in CI
    - Complement the IDE-only `.kiro/hooks/architecture-check.md` hook
    - _Review: Opp 13_
  - [ ] 21.10 Update CHANGELOG.md to reflect PR gate implementation
    - Document all eight gates under a new version entry
    - Include `BacktestResult.Notes` and `Tags` additions
    - Document V9 additions visible in the record
    - _Review: Opp 14_
  - [ ]* 21.11 Write unit tests for Gate 10 components
    - Test MAE/MFE tracking correctness
    - Test concatenated OOS curve chronological ordering
    - Test OOS profitability rate computation
    - Test multi-criteria comparison filtering
    - Test architecture dependency rules

- [ ] 22. Checkpoint — Gate 10 complete
  - Ensure all tests pass, ask the user if questions arise.


- [ ] 17. Final validation — PR Gate process compliance
  - [ ] 17.1 Verify all gates build cleanly with zero errors and zero warnings-as-errors
    - Run full solution build; confirm clean output
    - _Requirements: 35.1_
  - [ ] 17.2 Verify all tests pass including new property-based and unit tests
    - Run full test suite; confirm all 20 new property tests and all unit tests pass
    - Confirm existing 8 property tests from testing-standards still pass
    - _Requirements: 35.2_
  - [ ] 17.3 Verify documentation alignment across all gates
    - Confirm README, CHANGELOG, docs, specs, and comments reflect implemented reality
    - Confirm backward compatibility for persisted data formats
    - _Requirements: 35.3, 35.4, 35.5_

- [ ] 18. Final checkpoint — All gates complete
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation at each PR gate boundary
- Property tests validate universal correctness properties defined in the design document (20 total)
- Unit tests validate specific examples and edge cases
- All property tests use `[Property(MaxTest = 100)]` and are tagged with `// Feature: pr-gate-implementation-plan, Property N: <description>`
- The implementation uses .NET 8 / C# 12 with FsCheck.Xunit for property-based testing
- Existing 8 property tests from testing-standards.md remain unchanged and continue to validate core engine correctness
- PR gates are sequential: each gate must build, pass tests, and align documentation before the next begins

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1", "1.6"] },
    { "id": 1, "tasks": ["1.2", "1.8"] },
    { "id": 2, "tasks": ["1.3", "1.4", "1.5", "1.7", "1.9"] },
    { "id": 3, "tasks": ["1.10"] },
    { "id": 4, "tasks": ["3.1", "3.3", "3.5", "3.6"] },
    { "id": 5, "tasks": ["3.2", "3.4"] },
    { "id": 6, "tasks": ["3.7"] },
    { "id": 7, "tasks": ["5.1", "5.2"] },
    { "id": 8, "tasks": ["5.3", "5.4", "5.5", "5.6", "5.9"] },
    { "id": 9, "tasks": ["5.7", "5.8", "5.10"] },
    { "id": 10, "tasks": ["7.1", "7.3", "7.4"] },
    { "id": 11, "tasks": ["7.2", "7.5"] },
    { "id": 12, "tasks": ["9.1", "9.2", "9.4", "9.6"] },
    { "id": 13, "tasks": ["9.3", "9.5", "9.7"] },
    { "id": 14, "tasks": ["11.1", "11.2"] },
    { "id": 15, "tasks": ["11.3"] },
    { "id": 16, "tasks": ["13.1", "13.3", "13.6", "13.8", "13.10"] },
    { "id": 17, "tasks": ["13.2", "13.4", "13.5", "13.7", "13.9", "13.11"] },
    { "id": 18, "tasks": ["13.12"] },
    { "id": 19, "tasks": ["15.1", "15.2", "15.3", "15.4", "15.6", "15.8"] },
    { "id": 20, "tasks": ["15.5", "15.7", "15.9", "15.10"] },
    { "id": 21, "tasks": ["15.11"] },
    { "id": 22, "tasks": ["19.1", "19.2", "19.3", "19.4", "19.5", "19.6"] },
    { "id": 23, "tasks": ["19.7"] },
    { "id": 24, "tasks": ["21.1", "21.2", "21.3", "21.4", "21.5", "21.6", "21.7", "21.8", "21.9", "21.10"] },
    { "id": 25, "tasks": ["21.11"] },
    { "id": 26, "tasks": ["17.1", "17.2", "17.3"] }
  ]
}
```
