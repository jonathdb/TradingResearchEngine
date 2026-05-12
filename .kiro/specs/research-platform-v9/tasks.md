# Implementation Plan: Research Platform V9

## Overview

Phased implementation of architecture quality, strategy builder UX, robustness workflows, dashboard redesign, results exploration, portfolio evolution, and testing. Phase 1 delivers foundation; Phase 2 adds builder and robustness; Phase 3 completes portfolio and polish.

## Tasks

- [x] 1. Phase 1 Foundation — Domain and Application Layer
  - [x] 1.1 Create StrategyTypeId value object and JSON converter
    - Create `src/TradingResearchEngine.Application/Strategies/StrategyTypeId.cs` as a readonly record struct
    - Create `src/TradingResearchEngine.Application/Strategies/StrategyTypeIdJsonConverter.cs`
    - Update `ScenarioConfig.StrategyType`, `StrategyIdentity.StrategyType`, and `StrategyRegistry.Resolve` to use `StrategyTypeId`
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x]* 1.2 Write property test for StrategyTypeId JSON round-trip
    - **Property 2: StrategyTypeId JSON Round-Trip**
    - Create `src/TradingResearchEngine.UnitTests/Strategies/StrategyTypeIdProperties.cs`
    - For any non-null non-empty string, creating a StrategyTypeId, serializing to JSON, and deserializing produces an equal StrategyTypeId as a plain string token
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 2.1, 2.4, 41.1**

  - [x] 1.3 Create PagedResult record
    - Create `src/TradingResearchEngine.Application/Research/PagedResult.cs` with Items, TotalCount, Page, PageSize, and computed TotalPages
    - _Requirements: 4.5_

  - [x]* 1.4 Write property test for PagedResult TotalPages computation
    - **Property 3: PagedResult TotalPages Computation**
    - Create `src/TradingResearchEngine.UnitTests/Research/PagedResultProperties.cs`
    - For any TotalCount >= 0 and PageSize > 0, TotalPages equals ceiling division; PageSize == 0 yields TotalPages == 0
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 4.5, 41.3**

  - [x] 1.5 Add explicit timestamp fields to BacktestResult and StudyRecord
    - Modify `src/TradingResearchEngine.Core/Results/BacktestResult.cs` to add `CreatedAt`, `CompletedAt`, `Tags`, `Notes`
    - Modify `src/TradingResearchEngine.Application/Research/StudyRecord.cs` to add `StartedAt`, `CompletedAt`, `Tags`, `Notes`, `PartialResultIds`
    - Implement legacy RunId date parsing fallback in repository
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 13.4_

  - [x]* 1.6 Write property test for legacy RunId date parsing fallback
    - **Property 5: Legacy RunId Date Parsing Fallback**
    - Create `src/TradingResearchEngine.UnitTests/Results/BacktestResultMigrationProperties.cs`
    - For any valid RunId in format `yyyyMMdd-HHmmss-{guid}`, when CreatedAt == default, fallback parsing produces matching DateTimeOffset
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 3.5, 3.4**

  - [x] 1.7 Add paginated repository methods
    - Modify `src/TradingResearchEngine.Application/Research/IBacktestResultRepository.cs` to add `ListPagedAsync`
    - Modify `src/TradingResearchEngine.Application/Research/IStudyRepository.cs` to add `ListPagedAsync`
    - Implement `ListPagedAsync` in `src/TradingResearchEngine.Infrastructure/Persistence/JsonFileRepository.cs` with in-memory filtering and atomic writes
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 56.3_

  - [x] 1.8 Enhance ProgressSnapshot with ETA
    - Modify `src/TradingResearchEngine.Application/Research/ProgressSnapshot.cs` to add `EstimatedTimeRemaining` computed property and `WarningCount`
    - _Requirements: 11.1, 11.5_

  - [x]* 1.9 Write property test for ProgressSnapshot ETA formula
    - **Property 4: ProgressSnapshot ETA Formula**
    - Create `src/TradingResearchEngine.UnitTests/Research/ProgressSnapshotProperties.cs`
    - For any snapshot where Current > 0 and Total > 0, ETA equals (ElapsedTime / Current) * (Total - Current); null when Current == 0 or Total == 0
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirement 11.5**

  - [x] 1.10 Upgrade BlazorProgressReporter to Channel-based streaming
    - Modify `src/TradingResearchEngine.Infrastructure/Progress/BlazorProgressReporter.cs` to use `Channel<ProgressSnapshot>` with bounded capacity 16, DropOldest, and 250ms throttle
    - Implement `IAsyncDisposable`
    - _Requirements: 11.2, 11.3, 37.1, 37.5_

- [x] 2. Checkpoint — Phase 1 domain layer complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Phase 1 Foundation — Design Tokens and CSS
  - [x] 3.1 Create design-tokens.css
    - Create `src/TradingResearchEngine.Web/wwwroot/css/design-tokens.css` with spacing, typography, color palette, status states, segment colors, stage colors, severity colors, interactive states, breakpoints, and reduced-motion media query
    - _Requirements: 1.1, 1.3, 16.3, 17.1, 17.2, 17.3, 46.1, 50.1_

  - [x] 3.2 Migrate app.css to reference design tokens
    - Modify `src/TradingResearchEngine.Web/wwwroot/css/app.css` to replace inline hex values with token references for `.text-muted`, `.text-faint`, `.strategy-strip-card`, `.strip-active`, `.strip-untested`
    - Import design-tokens.css before app.css in the layout
    - _Requirements: 1.2, 1.4, 1.5_

- [x] 4. Phase 1 Foundation — Dashboard Redesign
  - [x] 4.1 Create SkeletonDashboard and KpiSparkline shared components
    - Create `src/TradingResearchEngine.Web/Components/Shared/SkeletonDashboard.razor` matching Dashboard layout with MudSkeleton placeholders
    - Create `src/TradingResearchEngine.Web/Components/Shared/KpiSparkline.razor` rendering mini SVG sparkline (5 data points)
    - _Requirements: 7.1, 8.1_

  - [x] 4.2 Redesign Dashboard.razor — loading, empty states, and error handling
    - Replace `MudProgressCircular` with `SkeletonDashboard` during loading
    - Add empty states for strategies, runs, and robustness sections
    - Replace empty `catch {}` blocks with `ILogger<Dashboard>` warning logs
    - Add error state with retry button on repository failure
    - Use `ListPagedAsync(page: 1, pageSize: 10)` instead of `ListAsync().Take(10)`
    - _Requirements: 6.1, 6.2, 6.3, 6.4, 7.1, 7.2, 7.3, 7.4, 47.4_

  - [x] 4.3 Redesign Dashboard.razor — KPI cards with sparklines and navigation
    - Add sparkline data to "Last Sharpe" KPI card (last 5 completed runs)
    - Add trend indicator to "Strategies" KPI card
    - Add breakdown tooltip to "Active Studies" KPI card
    - Add severity-coloured badges to "Flags" KPI card
    - Add click navigation to relevant detail pages
    - _Requirements: 8.1, 8.2, 8.3, 8.4, 8.5, 48.1, 48.2_

  - [x] 4.4 Redesign Dashboard.razor — strategy strip enhancement
    - Add mini progress bar (PassedCount/TotalChecks) to strip cards
    - Add relative date display using CreatedAt
    - Add DevelopmentStage coloured badge
    - Add NextRecommendedAction indicator icon with tooltip
    - Add horizontal scroll affordances (fade edges or scroll arrows)
    - Apply `@key` directives to strategy cards and table rows
    - Replace `FilteredRecentRuns` computed property with cached field
    - _Requirements: 5.1, 5.2, 5.3, 9.1, 9.2, 9.3, 9.4, 9.5_

  - [x] 4.5 Redesign Dashboard.razor — research pipeline and next actions
    - Add research pipeline section with strategies grouped by DevelopmentStage as horizontal flow diagram
    - Add "Suggested Next Steps" section (up to 3 strategies with recommended actions)
    - Add click navigation to study launch page pre-seeded with strategy context
    - Use design token status colours for each stage
    - _Requirements: 10.1, 10.2, 10.3, 10.4_

  - [x] 4.6 Dashboard responsive layout
    - Strategy strip stacks vertically below 768px
    - Two-column layout collapses to single column below 768px
    - KPI tiles display in 2x2 grid below 600px
    - Minimum 14px font size on viewports below 768px
    - _Requirements: 16.1, 16.2, 16.4, 16.5_

- [x] 5. Phase 1 Foundation — Accessibility and Navigation
  - [x] 5.1 Enhance NavMenu with accessibility and Robustness Hub link
    - Modify `src/TradingResearchEngine.Web/Components/Layout/NavMenu.razor`
    - Add `<nav aria-label="Main navigation">` wrapper
    - Add skip-to-content link as first focusable element
    - Add Robustness Hub link (`/robustness-hub`)
    - _Requirements: 15.3, 15.5, 48.4, 48.5_

  - [x] 5.2 Add accessibility attributes to Dashboard interactive elements
    - Strategy strip cards: `role="button"` with `aria-label`, keyboard Enter/Space navigation
    - MudTable instances: `aria-label` attributes
    - Clickable cards: `cursor: pointer`, hover elevation, focus ring
    - _Requirements: 15.1, 15.2, 15.4, 46.2, 46.3, 46.4, 46.5, 48.1, 48.3_

  - [x] 5.3 Add cancellation UX to progress display
    - Add "Cancel" button adjacent to progress indicator when study/backtest is running
    - Show "Cancelling..." status immediately on click
    - Set StudyRecord.Status to Cancelled and populate CompletedAt on cancellation
    - Handle already-completed jobs gracefully
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5_

  - [x] 5.4 Add queue-aware job status display
    - Display queue position on JobStatusPage.razor
    - Expose `GetQueueSnapshot()` on JobWorkerService
    - Support filtering by status (Queued, Running, Completed, Failed, Cancelled)
    - Transition status badges without page refresh
    - _Requirements: 14.1, 14.2, 14.3, 14.4, 14.5_

- [x] 6. Checkpoint — Phase 1 complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Phase 2 — Robustness and Advisory
  - [x] 7.1 Create RobustnessWarning record and extend advisory service
    - Create `src/TradingResearchEngine.Application/Research/RobustnessWarning.cs` with Severity enum, Code, Explanation, RecommendedAction, Cause, Remediation, CauseCategory
    - Modify `src/TradingResearchEngine.Application/Research/RobustnessAdvisoryService.cs` to add `GetStructuredWarnings` method
    - Modify `src/TradingResearchEngine.Application/Research/IRobustnessAdvisoryService.cs` to add interface method
    - Preserve existing `GetWarnings()` for backward compatibility
    - _Requirements: 25.3, 51.1, 51.2, 51.3_

  - [ ]* 7.2 Write property test for RobustnessAdvisoryService severity classification
    - **Property 6: RobustnessAdvisoryService Severity Classification**
    - Create `src/TradingResearchEngine.UnitTests/Research/RobustnessAdvisoryProperties.cs`
    - For any BacktestResult with metrics exceeding thresholds, verify correct severity mapping; no warnings for metrics within thresholds
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 25.3, 41.2**

  - [x] 7.3 Create Robustness Hub page
    - Create `src/TradingResearchEngine.Web/Components/Pages/Research/RobustnessHub.razor` at route `/robustness-hub`
    - Display strategies with active warnings grouped by strategy
    - Show severity badges, explanations, recommended actions (clickable to study launch)
    - Add filter by severity and strategy
    - Add summary bar with total warnings by severity
    - Group warnings by cause category
    - _Requirements: 25.1, 25.2, 25.4, 25.5, 25.6, 51.3, 51.4_

- [x] 8. Phase 2 — Statistical Workflows
  - [x] 8.1 Implement PermutationTestWorkflow and result record
    - Create `src/TradingResearchEngine.Application/Research/Results/PermutationTestResult.cs`
    - Create `src/TradingResearchEngine.Application/Research/Workflows/PermutationTestWorkflow.cs`
    - Accept explicit seed for deterministic reproducibility
    - Compute p-value as proportion of permuted results >= original metric
    - _Requirements: 29.1, 29.2, 29.5_

  - [ ]* 8.2 Write property test for permutation test determinism and p-value bounds
    - **Property 8: Permutation Test Determinism and P-Value Bounds**
    - Create `src/TradingResearchEngine.UnitTests/Research/PermutationTestProperties.cs`
    - Running twice with same seed produces identical PValue and PermutedMetrics; PValue always in [0.0, 1.0]
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 29.2, 29.5, 41.4**

  - [x] 8.3 Implement ParameterStabilityWorkflow and result record
    - Create `src/TradingResearchEngine.Application/Research/Results/ParameterStabilityResult.cs`
    - Create `src/TradingResearchEngine.Application/Research/Workflows/ParameterStabilityWorkflow.cs`
    - Compute stability score based on variance of target metric across neighbouring cells
    - Identify stability zones and ridges
    - _Requirements: 28.1, 28.2, 28.3_

  - [ ]* 8.4 Write property test for parameter stability score invariants
    - **Property 7: Parameter Stability Score Invariants**
    - Create `src/TradingResearchEngine.UnitTests/Research/ParameterStabilityProperties.cs`
    - Flat surface (all cells equal) yields stability score 0.0 for every cell; all scores are non-negative
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 28.1, 28.2, 44.2, 44.3**

- [x] 9. Phase 2 — Condition Builder UI
  - [x] 9.1 Create ConditionBuilder, ConditionGroupNode, and ConditionRow components
    - Create `src/TradingResearchEngine.Web/Components/Builder/ConditionBuilder.razor` — visual tree editor for ConditionNode AST
    - Create `src/TradingResearchEngine.Web/Components/Builder/ConditionGroupNode.razor` — recursive component for logical groups
    - Create `src/TradingResearchEngine.Web/Components/Builder/ConditionRow.razor` — comparison/cross row with dropdowns
    - Support drag-and-drop reordering up to 3 levels deep
    - On change: call ConditionPrettyPrinter.Print() and update BuilderViewModel
    - On load: call ConditionParser.Parse() and render visual tree
    - Fallback to raw text editor on parse failure
    - _Requirements: 19.1, 19.2, 19.3, 19.4, 19.5, 19.6_

  - [x] 9.2 Update BuilderViewModel for condition state and modes
    - Modify `src/TradingResearchEngine.Web/Components/Builder/BuilderViewModel.cs`
    - Add `ParsedEntryCondition`, `ParsedExitCondition`, `IsBeginnerMode` properties
    - _Requirements: 19.4, 38.1_

  - [ ]* 9.3 Write property test for condition parser round-trip
    - **Property 1: Condition Parser Round-Trip**
    - Create `src/TradingResearchEngine.UnitTests/Strategies/Composite/Conditions/ConditionParserProperties.cs`
    - For any valid ConditionNode AST (depth up to 4), pretty-printing then parsing produces structurally equivalent AST
    - Include ConditionNodeArbitrary generator covering LogicalNode, ComparisonNode, CrossNode, IndicatorRefNode, PriceRefNode, LiteralNode
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 19.7, 42.1, 42.2, 42.3**

- [x] 10. Phase 2 — Strategy Builder Enhancements
  - [x] 10.1 Create ParameterGroupEditor component
    - Create `src/TradingResearchEngine.Web/Components/Builder/ParameterGroupEditor.razor`
    - Group parameters by Group property with collapsible sections
    - Inline validation on constraint violation
    - Default value placeholder, non-default highlight, tooltips, reset button
    - _Requirements: 20.1, 20.2, 20.3, 20.4, 20.5, 20.6_

  - [x] 10.2 Create TimelineSplitVisualizer component
    - Create `src/TradingResearchEngine.Web/Components/Builder/TimelineSplitVisualizer.razor`
    - Horizontal bar showing IS/OOS/Held-Out segments with proportional widths and date labels
    - Draggable segment boundaries updating BuilderViewModel
    - Warning for segments < 30 bars
    - Default 70% IS / 15% OOS / 15% Held-Out
    - _Requirements: 22.1, 22.2, 22.3, 22.4, 22.5_

  - [x] 10.3 Create AdvancedOverridesPanel and ShortcutHelpOverlay
    - Create `src/TradingResearchEngine.Web/Components/Builder/AdvancedOverridesPanel.razor` — summary of non-default parameters with navigation
    - Create `src/TradingResearchEngine.Web/Components/Builder/ShortcutHelpOverlay.razor` — modal listing keyboard shortcuts
    - _Requirements: 21.1, 21.2, 21.3, 21.4, 23.4_

  - [x] 10.4 Integrate enhancements into StrategyBuilder.razor
    - Modify `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
    - Add beginner/expert mode toggle
    - Add "Quick Preview" button on Step 3+ (JobType.QuickSanity)
    - Add keyboard shortcut handling (Ctrl+Enter, Ctrl+Shift+Enter, Ctrl+S, ?)
    - Integrate ConditionBuilder, TimelineSplitVisualizer, AdvancedOverridesPanel
    - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5, 23.1, 23.2, 23.3, 23.5, 38.1, 38.2, 38.3, 38.4, 38.5_

  - [x] 10.5 Enhance PreflightValidator
    - Modify `src/TradingResearchEngine.Application/Engine/PreflightValidator.cs`
    - Check data file has >= 100 bars
    - Check all referenced indicators have matching config entries
    - Check for look-ahead bias (OOS/IS overlap)
    - Categorise findings as Errors/Warnings/Info
    - Create `PreflightFindingsPanel.razor` with distinct icons and colours
    - Disable Launch button on blocking errors
    - _Requirements: 24.1, 24.2, 24.3, 24.4, 24.5_

- [x] 11. Phase 2 — Research Explorer and Results
  - [x] 11.1 Enhance ResearchExplorer with pagination and discoverability
    - Modify `src/TradingResearchEngine.Web/Components/Pages/Research/ResearchExplorer.razor`
    - Use `ListPagedAsync` with active filters
    - Add "Related Studies" section, "Suggested Studies" banner
    - Add checklist completion progress indicator
    - Add multi-select with "Compare Selected" action
    - Add study duration and cost estimate display
    - _Requirements: 4.4, 39.1, 39.2, 39.3, 39.4, 39.5_

  - [x] 11.2 Create ResearchJournalEntry and repository interface
    - Create `src/TradingResearchEngine.Application/Research/ResearchJournalEntry.cs` with EntryId, StrategyId, StrategyVersionId, Timestamp, Action, Reason, FromStage, ToStage
    - Create `src/TradingResearchEngine.Application/Research/IResearchJournalRepository.cs`
    - _Requirements: 33.1, 33.5_

  - [ ]* 11.3 Write property test for ResearchJournalEntry JSON round-trip
    - **Property 10: ResearchJournalEntry JSON Round-Trip**
    - Create `src/TradingResearchEngine.UnitTests/Research/ResearchJournalProperties.cs`
    - For any valid ResearchJournalEntry, serializing to JSON and deserializing produces an equal record
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 33.1, 41.5**

  - [x] 11.4 Add run annotation support (tags and notes)
    - Implement tag editor and notes text area on run detail page
    - Add tag filtering to Research Explorer and Dashboard recent runs
    - _Requirements: 31.1, 31.2, 31.3, 31.4, 31.5, 31.6_

  - [x] 11.5 Extend export service for studies, comparisons, and portfolios
    - Modify `src/TradingResearchEngine.Application/Export/IReportExporter.cs` to add study, comparison, and portfolio export methods
    - Modify `src/TradingResearchEngine.Web/Components/Shared/ExportMenu.razor` to add new menu items
    - _Requirements: 34.1, 34.2, 34.3, 34.4_

- [x] 12. Phase 2 — Charts and Visualisation
  - [x] 12.1 Create CPCV distribution chart and parameter sweep heatmap
    - Create `src/TradingResearchEngine.Web/Components/Charts/CpcvDistributionChart.razor` — histogram of path Sharpe ratios with reference line
    - Create `src/TradingResearchEngine.Web/Components/Charts/ParameterSweepHeatmap.razor` — 2D heatmap with metric switching, tooltips, optimal cell highlight, stability zone outlines
    - _Requirements: 26.1, 26.2, 26.3, 26.4, 27.1, 27.2, 27.3, 27.4, 27.5_

  - [x] 12.2 Add data segment colour coding across UI
    - Apply IS/OOS/WF/CPCV/Held-Out colours from design tokens to equity curves, study detail pages, and Research Summary Rail
    - Add legends explaining colour coding
    - Label OOS metrics with "(OOS)" suffix
    - _Requirements: 50.2, 50.3, 50.4, 50.5_

- [x] 13. Checkpoint — Phase 2 complete
  - Ensure all tests pass, ask the user if questions arise.

- [x] 14. Phase 3 — Portfolio Evolution
  - [x] 14.1 Create PortfolioMetrics record and extend portfolio runner
    - Create `src/TradingResearchEngine.Application/Portfolio/PortfolioMetrics.cs` with DiversificationRatio, MaxPairwiseCorrelation, AnnualisedTurnover, TrackingError
    - Modify `src/TradingResearchEngine.Core/Configuration/PortfolioRebalanceMode.cs` to add RiskParity, CustomWeights
    - Modify `src/TradingResearchEngine.Application/Portfolio/PortfolioBacktestRunner.cs` to add RiskParity and CustomWeights allocation logic
    - Add PortfolioMetrics field to PortfolioBacktestResult
    - _Requirements: 35.1, 35.4, 36.1, 36.2, 36.3_

  - [ ]* 14.2 Write property test for portfolio diversification ratio lower bound
    - **Property 9: Portfolio Diversification Ratio Lower Bound**
    - Create `src/TradingResearchEngine.UnitTests/Portfolio/PortfolioMetricsProperties.cs`
    - For any portfolio with 2+ assets where all individual volatilities are positive, DiversificationRatio >= 1.0
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 36.1, 36.5**

  - [x] 14.3 Create PortfolioRunSetup page and CorrelationHeatmap chart
    - Create `src/TradingResearchEngine.Web/Components/Pages/Portfolio/PortfolioRunSetup.razor` — add/remove symbols, select strategies, configure weights
    - Create `src/TradingResearchEngine.Web/Components/Charts/CorrelationHeatmap.razor` — interactive correlation matrix using Blazor-ApexCharts
    - _Requirements: 35.2, 35.3, 35.5, 35.6, 36.4, 36.5_

- [x] 15. Phase 3 — Mobile Layouts and Polish
  - [x] 15.1 Implement mobile and tablet responsive layouts
    - Strategy Builder: collapse Research Summary Rail below main content at < 960px
    - Comparison page: vertical card layout at < 960px
    - Navigation menu: hamburger with slide-out drawer at < 600px
    - Touch targets: minimum 44x44px at < 960px
    - Charts: responsive resize without horizontal scrolling
    - _Requirements: 40.1, 40.2, 40.3, 40.4, 40.5_

  - [x] 15.2 Implement research pipeline stage transitions
    - Enforce stage transition rules (Exploration -> Validation requires 3 studies, etc.)
    - Add "Ready to Promote" banner with one-click promotion
    - Add unmet requirements modal on invalid promotion
    - Add rejection reason prompt with journal entry creation
    - Add visual pipeline indicator on strategy detail page
    - _Requirements: 53.1, 53.2, 53.3, 53.4, 53.5_

- [x] 16. Phase 3 — Integration Tests
  - [ ]* 16.1 Write integration tests for paginated repository
    - Create `src/TradingResearchEngine.IntegrationTests/PaginatedRepositoryTests.cs`
    - Seed 25 BacktestResult records, verify page sizes, filters, total counts
    - Verify filtering by strategyTypeFilter and BacktestStatus
    - _Requirements: 43.1, 43.2, 43.3, 43.4_

  - [ ]* 16.2 Write integration test for atomic JSON writes
    - Verify temp-file-then-rename pattern in JsonFileRepository
    - _Requirements: 56.3_

- [x] 17. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document
- Unit tests validate specific examples and edge cases
- Phase 1 tasks (1-6) deliver independently deployable foundation
- Phase 2 tasks (7-13) deliver builder and robustness features
- Phase 3 tasks (14-17) deliver portfolio evolution and polish
