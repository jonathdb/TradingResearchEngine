# Requirements Document — Research Platform V9

## Introduction

Research Platform V9 is a comprehensive upgrade to the TradingResearchEngine application covering architecture quality, strategy builder UX, robustness workflows, dashboard redesign, results exploration, portfolio evolution, real-time execution experience, accessibility, testing, and phased delivery. The upgrade preserves Clean Architecture boundaries (Core ← Application ← Infrastructure ← Web) and extends existing components incrementally rather than rewriting them.

## Glossary

- **Engine**: The `BacktestEngine` in `TradingResearchEngine.Core` that runs event-driven backtests.
- **Dashboard**: The `Dashboard.razor` page serving as the application home screen.
- **Strategy_Builder**: The 5-step wizard in `StrategyBuilder.razor` and its sub-components (`Step1`–`Step5`, `BuilderViewModel`, `ResearchSummaryRail`).
- **Robustness_Hub**: A new dedicated page aggregating all robustness warnings, severity levels, explanations, and recommended next actions.
- **Research_Explorer**: The `ResearchExplorer.razor` page for browsing and filtering studies.
- **Condition_Builder**: The UI component that maps to the existing `ConditionNodes` AST (`LogicalNode`, `ComparisonNode`, `CrossNode`, `IndicatorRefNode`, `PriceRefNode`, `LiteralNode`).
- **Progress_Reporter**: The `IProgressReporter` / `BlazorProgressReporter` system for streaming execution status to the UI.
- **Research_Checklist**: The 9-item `ResearchChecklist` computed by `ResearchChecklistService`.
- **Robustness_Advisory**: The `RobustnessAdvisoryService` that evaluates `BacktestResult` metrics against configurable thresholds.
- **Portfolio_Runner**: The `PortfolioBacktestRunner` that orchestrates parallel multi-symbol backtests.
- **Export_Service**: The `IReportExporter` / `ResultExportService` providing Markdown, JSON, and CSV exports.
- **Job_Executor**: The `JobExecutor` / `JobWorkerService` managing background study and backtest execution.
- **Design_Tokens**: A CSS custom property system replacing inline styles with semantic variables for spacing, typography, color, and state.
- **CPCV**: Combinatorial Purged Cross-Validation, a study type already implemented in `CpcvStudyHandler`.
- **Condition_Parser**: The `ConditionParser` in `Application/Strategies/Composite/Conditions/` that parses condition expression strings into AST nodes.
- **Condition_Pretty_Printer**: The `ConditionPrettyPrinter` that serialises AST nodes back to expression strings.

## Phasing Strategy

- **Phase 1** (Foundation): Architecture cleanup, design tokens, Dashboard redesign, progress reporting improvements, accessibility foundations, core testing infrastructure.
- **Phase 2** (Builder & Robustness): Strategy Builder redesign, Robustness Hub, CPCV visualisation, parameter surface analysis, statistical significance, results exploration improvements.
- **Phase 3** (Portfolio & Polish): Portfolio evolution, advanced export, journaling, mobile layouts, performance optimisation, remaining testing coverage.

---

## Requirements

### Requirement 1: CSS Design Token System (Phase 1)

**User Story:** As a developer, I want all visual styling governed by CSS custom properties in a design token file, so that the UI is consistent and maintainable without inline styles.

#### Acceptance Criteria

1. THE Design_Tokens system SHALL define CSS custom properties for spacing (4px, 8px, 12px, 16px, 24px, 32px, 48px), typography (font-size scale, font-weight, line-height), color palette (primary, secondary, success, warning, error, surface, background), and status states (active, untested, failed, running, completed).
2. WHEN a Razor component is rendered, THE Web application SHALL use Design_Tokens classes instead of inline `style` attributes for layout, spacing, and color.
3. THE Design_Tokens file SHALL be located at `src/TradingResearchEngine.Web/wwwroot/css/design-tokens.css` and imported before `app.css`.
4. WHEN a component references a status color (e.g., Sharpe thresholds, study status, robustness severity), THE component SHALL use a Design_Tokens CSS class rather than computed inline color strings.
5. THE existing `app.css` classes (`.text-muted`, `.text-faint`, `.strategy-strip-card`, `.strip-active`, `.strip-untested`) SHALL be migrated to reference Design_Tokens custom properties.

---

### Requirement 2: Typed Strategy Identifiers (Phase 1)

**User Story:** As a developer, I want strategy types identified by a strongly-typed value object instead of raw strings, so that typos and mismatches are caught at compile time.

#### Acceptance Criteria

1. THE Application layer SHALL define a `StrategyTypeId` readonly record struct wrapping a `string` value with equality, `ToString()`, and implicit conversion from `string`.
2. WHEN `ScenarioConfig.StrategyType`, `StrategyIdentity.StrategyType`, or `BacktestResult.ScenarioConfig.StrategyType` is accessed, THE system SHALL use `StrategyTypeId` instead of raw `string`.
3. THE `StrategyRegistry.Resolve` method SHALL accept `StrategyTypeId` and throw `StrategyNotFoundException` with the typed identifier.
4. WHEN a `StrategyTypeId` is serialised to JSON, THE serialiser SHALL produce a plain string value (no wrapper object) to maintain backward compatibility with existing persisted data.

---

### Requirement 3: Explicit Timestamp Fields (Phase 1)

**User Story:** As a developer, I want explicit `CreatedAt`, `StartedAt`, and `CompletedAt` fields on run and study records, so that temporal queries do not depend on parsing ID prefixes.

#### Acceptance Criteria

1. THE `BacktestResult` record SHALL include `DateTimeOffset CreatedAt` and `DateTimeOffset? CompletedAt` fields.
2. THE `StudyRecord` record SHALL include `DateTimeOffset StartedAt` and `DateTimeOffset? CompletedAt` fields in addition to the existing `CreatedAt`.
3. WHEN a backtest run completes or fails, THE Engine SHALL populate `CompletedAt` with `DateTimeOffset.UtcNow`.
4. THE Dashboard SHALL sort and display run dates using `CreatedAt` instead of parsing the `RunId` prefix via `TryParseRunDate`.
5. WHEN deserialising legacy records that lack `CreatedAt`, THE repository SHALL fall back to parsing the ID prefix and populate the field for forward compatibility.

---

### Requirement 4: Paginated Repository Methods (Phase 1)

**User Story:** As a developer, I want paginated and filtered repository query methods, so that the Dashboard and Research Explorer do not load entire datasets into memory.

#### Acceptance Criteria

1. THE `IBacktestResultRepository` SHALL expose a `ListPagedAsync(int page, int pageSize, string? strategyTypeFilter, BacktestStatus? statusFilter, CancellationToken ct)` method returning a `PagedResult<BacktestResult>` record containing `Items`, `TotalCount`, `Page`, and `PageSize`.
2. THE `IStudyRepository` SHALL expose a `ListPagedAsync(int page, int pageSize, StudyType? typeFilter, StudyStatus? statusFilter, string? strategyVersionId, CancellationToken ct)` method.
3. WHEN the Dashboard loads recent runs, THE Dashboard SHALL call `ListPagedAsync(page: 1, pageSize: 10)` instead of `ListAsync()` followed by `.Take(10)`.
4. WHEN the Research Explorer loads studies, THE Research_Explorer SHALL call `ListPagedAsync` with the active filters instead of loading all studies and filtering in memory.
5. THE `PagedResult<T>` record SHALL be defined in the Application layer with `IReadOnlyList<T> Items`, `int TotalCount`, `int Page`, `int PageSize`, and a computed `int TotalPages` property.

---

### Requirement 5: Blazor Render Performance (Phase 1)

**User Story:** As a developer, I want computed properties cached and UI collections keyed, so that Blazor does not re-render unchanged DOM elements on every state change.

#### Acceptance Criteria

1. WHEN the Dashboard renders the strategy strip, THE Dashboard SHALL apply `@key="s.StrategyId"` to each strategy card element.
2. WHEN the Dashboard renders the recent runs table, THE Dashboard SHALL apply `@key="context.Id"` to each table row.
3. THE `FilteredRecentRuns` computed property in `Dashboard.razor` SHALL be replaced with a cached field that is recomputed only when `_recentRuns`, `_showFailedRuns`, or `_selectedTypeFilter` change.
4. WHEN the Research Explorer renders the study table, THE Research_Explorer SHALL apply `@key="context.Study.StudyId"` to each row.
5. THE Strategy_Builder SHALL not recompute `_schemas` on every `StateHasChanged` call; schema loading SHALL occur only when `_vm.StrategyType` changes.

---

### Requirement 6: Error Handling and Logging (Phase 1)

**User Story:** As a developer, I want silent catch blocks replaced with structured logging, so that failures are diagnosable without hiding errors.

#### Acceptance Criteria

1. WHEN the Dashboard encounters an exception while computing a strategy's checklist, THE Dashboard SHALL log the exception at `Warning` level with the strategy ID context instead of swallowing it with an empty `catch {}`.
2. WHEN any Blazor component catches an exception during data loading, THE component SHALL log the exception via `ILogger<T>` and display a user-visible error alert.
3. THE application SHALL inject `ILogger<T>` into all page components that currently contain empty catch blocks (`Dashboard.razor`, `ResearchExplorer.razor`).
4. IF a repository call fails during Dashboard initialisation, THEN THE Dashboard SHALL display a "Failed to load" error state with a retry button instead of showing a blank page.

---

### Requirement 7: Dashboard Redesign — Skeleton Loading and Empty States (Phase 1)

**User Story:** As a researcher, I want the Dashboard to show skeleton placeholders while loading and informative empty states when no data exists, so that I always understand the application state.

#### Acceptance Criteria

1. WHILE the Dashboard is loading data (`_loading == true`), THE Dashboard SHALL display skeleton placeholders (MudSkeleton) matching the layout of KPI tiles, strategy strip, and recent runs table instead of a single `MudProgressCircular`.
2. WHEN no strategies exist, THE Dashboard SHALL display an empty state with a "Create your first strategy" call-to-action linking to `/strategies/builder`.
3. WHEN no backtest runs exist, THE Dashboard SHALL display an empty state in the recent runs section explaining how to run a first backtest.
4. WHEN no robustness warnings exist, THE Dashboard SHALL display a positive confirmation message ("All clear — no robustness flags") instead of hiding the section entirely.

---

### Requirement 8: Dashboard Redesign — KPI Cards with Sparklines (Phase 1)

**User Story:** As a researcher, I want KPI cards that show trend direction and sparkline context, so that I can assess momentum at a glance.

#### Acceptance Criteria

1. THE Dashboard KPI card for "Last Sharpe" SHALL include a sparkline showing the Sharpe ratio of the 5 most recent completed runs.
2. THE Dashboard KPI card for "Strategies" SHALL display a trend indicator (↑/↓/→) comparing current count to count 7 days ago.
3. THE Dashboard KPI card for "Active Studies" SHALL show a breakdown tooltip listing running study types and their progress percentages.
4. THE Dashboard KPI card for "Flags" SHALL use severity-coloured badges (red for critical, amber for moderate) instead of a plain count.
5. WHEN a KPI card is clicked, THE Dashboard SHALL navigate to the relevant detail page (strategies → `/strategies/library`, flags → `/robustness-hub`).

---

### Requirement 9: Dashboard Redesign — Strategy Strip Enhancement (Phase 1)

**User Story:** As a researcher, I want strategy strip cards to show checklist progress, last run date, and development stage clearly, so that I can prioritise my research workflow.

#### Acceptance Criteria

1. THE strategy strip card SHALL display a mini progress bar showing `PassedCount / TotalChecks` from the Research_Checklist.
2. THE strategy strip card SHALL display the last run date in relative format (e.g., "2d ago", "1h ago") using the `CreatedAt` field.
3. THE strategy strip card SHALL display the `DevelopmentStage` as a coloured badge (Exploration=blue, Validation=amber, FinalTest=green, Live=purple, Retired=grey).
4. WHEN a strategy has a `NextRecommendedAction`, THE strip card SHALL show a small action indicator icon with a tooltip describing the recommended next step.
5. THE strategy strip SHALL be horizontally scrollable with visible scroll affordances (fade edges or scroll arrows) on overflow.

---

### Requirement 10: Dashboard Redesign — Research Pipeline and Next Actions (Phase 1)

**User Story:** As a researcher, I want the Dashboard to show my research pipeline stages and suggest what I should do next, so that I maintain research momentum.

#### Acceptance Criteria

1. THE Dashboard research pipeline section SHALL display strategies grouped by `DevelopmentStage` as a horizontal flow diagram with counts per stage.
2. WHEN a strategy has a `NextRecommendedAction` from the Research_Checklist, THE Dashboard SHALL display a "Suggested Next Steps" section listing up to 3 strategies with their recommended actions.
3. WHEN the user clicks a suggested next step, THE Dashboard SHALL navigate to the appropriate study launch page pre-seeded with the strategy context.
4. THE research pipeline section SHALL use Design_Tokens status colours for each stage.

---


### Requirement 11: Progress Reporting Improvements (Phase 1)

**User Story:** As a researcher, I want real-time progress updates with stage labels, elapsed time, ETA, and warning counts, so that I can monitor long-running studies without uncertainty.

#### Acceptance Criteria

1. THE `BlazorProgressReporter` SHALL emit structured `ProgressSnapshot` updates to the UI including `Current`, `Total`, `Stage`, `ElapsedTime`, `EstimatedTimeRemaining`, and `WarningCount`.
2. WHEN a study is running, THE UI SHALL display a progress bar with percentage, stage label, elapsed time, and ETA.
3. THE `BlazorProgressReporter` SHALL throttle UI updates to a maximum of 4 per second to avoid excessive re-renders.
4. WHEN `ProgressSnapshot.WarningCount` is greater than zero, THE progress display SHALL show an amber warning badge with the count.
5. THE `IProgressReporter.Report(ProgressSnapshot)` method SHALL compute `EstimatedTimeRemaining` as `(ElapsedTime / Current) * (Total - Current)` when `Current > 0` and `Total > 0`.

---

### Requirement 12: Cancellation UX (Phase 1)

**User Story:** As a researcher, I want to cancel a running study or backtest with a single click and see immediate feedback, so that I do not waste time on unwanted computations.

#### Acceptance Criteria

1. WHEN a study or backtest is running, THE UI SHALL display a "Cancel" button adjacent to the progress indicator.
2. WHEN the user clicks "Cancel", THE system SHALL invoke `CancellationTokenSource.Cancel()` on the associated job's token.
3. WHEN cancellation is requested, THE progress display SHALL immediately show "Cancelling..." status before the job acknowledges cancellation.
4. WHEN a job is cancelled, THE `StudyRecord.Status` SHALL be set to `Cancelled` and the `CompletedAt` timestamp SHALL be populated.
5. IF a job has already completed before the cancellation signal arrives, THEN THE system SHALL display the completed result without error.

---

### Requirement 13: Failure Visibility and Partial Results (Phase 1)

**User Story:** As a researcher, I want to see why a study failed and access any partial results produced before failure, so that I can diagnose issues and salvage useful data.

#### Acceptance Criteria

1. WHEN a study fails, THE study detail page SHALL display the `FailureDetail` message in a prominent error banner.
2. WHEN a walk-forward or parameter sweep study fails partway through, THE system SHALL persist any completed fold/cell results as partial results on the `StudyRecord`.
3. WHEN partial results exist on a failed study, THE study detail page SHALL display them with a "Partial — N of M completed" indicator.
4. THE `StudyRecord` SHALL include an optional `IReadOnlyList<string> PartialResultIds` field for referencing completed sub-results.
5. WHEN a study fails, THE study detail page SHALL offer a "Retry" button that re-launches the study with the same configuration.

---

### Requirement 14: Queue-Aware UX (Phase 1)

**User Story:** As a researcher, I want to see my queued jobs, their position, and estimated wait time, so that I can plan my workflow around execution capacity.

#### Acceptance Criteria

1. THE `JobStatusPage.razor` SHALL display all queued, running, completed, and failed jobs in a unified view with status badges.
2. WHEN a job is queued, THE job status display SHALL show its position in the queue (e.g., "Position 3 of 5").
3. THE `JobWorkerService` SHALL expose a `GetQueueSnapshot()` method returning `IReadOnlyList<JobQueueEntry>` with `JobId`, `JobType`, `QueuedAt`, `Status`, and `Position`.
4. WHEN a queued job starts executing, THE UI SHALL transition its status badge from "Queued" to "Running" without requiring a page refresh.
5. THE job status page SHALL support filtering by status (Queued, Running, Completed, Failed, Cancelled).

---

### Requirement 15: Accessibility Foundations (Phase 1)

**User Story:** As a user relying on assistive technology, I want all interactive elements to have proper ARIA labels, keyboard focus management, and semantic structure, so that I can navigate the application without a mouse.

#### Acceptance Criteria

1. THE Dashboard strategy strip cards SHALL be rendered as `<button>` or have `role="button"` with `aria-label` describing the strategy name and status.
2. WHEN a user presses Enter or Space on a focused strategy strip card, THE card SHALL navigate to the strategy detail page.
3. THE navigation menu (`NavMenu.razor`) SHALL use `<nav aria-label="Main navigation">` as its root element.
4. ALL MudTable instances SHALL include `aria-label` attributes describing their content (e.g., "Recent backtest runs").
5. THE application SHALL include a skip-to-content link as the first focusable element on every page.
6. WHEN focus moves to a modal dialog (e.g., `AIAssistantDialog`, `RefineWithAIDialog`), THE focus SHALL be trapped within the dialog until it is dismissed.

---

### Requirement 16: Responsive Layout Foundations (Phase 1)

**User Story:** As a researcher using a tablet or smaller screen, I want the Dashboard and key pages to adapt their layout, so that I can review results on any device.

#### Acceptance Criteria

1. WHEN the viewport width is below 768px, THE Dashboard strategy strip SHALL stack vertically instead of scrolling horizontally.
2. WHEN the viewport width is below 768px, THE Dashboard two-column layout (recent runs + robustness flags) SHALL collapse to a single column with robustness flags below recent runs.
3. THE Design_Tokens system SHALL define breakpoint variables: `--breakpoint-sm: 600px`, `--breakpoint-md: 960px`, `--breakpoint-lg: 1280px`.
4. WHEN the viewport width is below 600px, THE KPI tiles SHALL display in a 2×2 grid instead of a 1×4 row.
5. ALL text content SHALL have a minimum computed font size of 14px on viewports below 768px.

---

### Requirement 17: Reduced Motion Support (Phase 1)

**User Story:** As a user with motion sensitivity, I want animations and transitions disabled when I have enabled reduced motion in my OS settings, so that the interface does not cause discomfort.

#### Acceptance Criteria

1. THE Design_Tokens CSS file SHALL include a `@media (prefers-reduced-motion: reduce)` block that sets all `transition-duration` and `animation-duration` to `0s`.
2. WHEN reduced motion is active, THE progress bars SHALL update their value without animated transitions.
3. WHEN reduced motion is active, THE skeleton loading placeholders SHALL display as static grey blocks without shimmer animation.

---

### Requirement 18: Strategy Builder — Live Preview and Sanity Run (Phase 2)

**User Story:** As a researcher, I want a fast sanity backtest during strategy configuration, so that I can validate my setup before committing to a full run.

#### Acceptance Criteria

1. WHEN the user is on Step 3 (Strategy Parameters) or later, THE Strategy_Builder SHALL offer a "Quick Preview" button that runs a reduced backtest (last 2 years, `FastResearch` realism profile).
2. WHEN the quick preview completes, THE Research Summary Rail SHALL display key metrics (Sharpe, MaxDD, TotalTrades, WinRate) from the preview result.
3. THE quick preview SHALL execute via `JobExecutor.SubmitAsync` with `JobType.QuickSanity` and complete within 10 seconds for typical daily-bar datasets.
4. WHILE the quick preview is running, THE "Quick Preview" button SHALL show a loading spinner and be disabled.
5. IF the quick preview fails, THEN THE Research Summary Rail SHALL display the error message with a "Retry" option.

---

### Requirement 19: Strategy Builder — Condition Builder UI (Phase 2)

**User Story:** As a researcher, I want a visual rule composition interface for entry/exit conditions, so that I can build complex composite strategies without writing expression strings manually.

#### Acceptance Criteria

1. THE Condition_Builder UI SHALL render the existing `ConditionNode` AST as a visual tree with nested groups for `LogicalNode` (AND/OR), comparison rows for `ComparisonNode`, and cross-detection rows for `CrossNode`.
2. WHEN the user adds a condition, THE Condition_Builder SHALL offer a dropdown of available indicators (from `CompositeStrategyConfig.Indicators`) and price fields (`Open`, `High`, `Low`, `Close`, `Volume`).
3. THE Condition_Builder SHALL support drag-and-drop reordering of condition groups and nesting up to 3 levels deep.
4. WHEN the user modifies the visual tree, THE Condition_Builder SHALL invoke `ConditionPrettyPrinter` to generate the expression string and update `BuilderViewModel.EntryCondition` or `ExitCondition`.
5. WHEN the Condition_Builder loads an existing strategy, THE system SHALL invoke `ConditionParser` to parse the expression string into the AST and render it visually.
6. IF the `ConditionParser` fails to parse an expression, THEN THE Condition_Builder SHALL fall back to a raw text editor with a validation error message.
7. FOR ALL valid condition ASTs, parsing the pretty-printed expression SHALL produce an equivalent AST (round-trip property).

---

### Requirement 20: Strategy Builder — Parameter Editing UX (Phase 2)

**User Story:** As a researcher, I want parameters grouped by category with defaults, constraints, and contextual validation, so that I can configure strategies efficiently without errors.

#### Acceptance Criteria

1. THE `ParameterGroupEditor.razor` SHALL group parameters by their `Group` property from `StrategyParameterSchema` with collapsible sections.
2. WHEN a parameter value violates its schema constraints (min, max, step), THE editor SHALL display an inline validation error immediately without waiting for form submission.
3. THE parameter editor SHALL display the default value as a placeholder and highlight non-default values with a visual indicator (bold label or accent border).
4. WHEN the user hovers over a parameter label, THE editor SHALL display a tooltip with the parameter's `Description` from the schema.
5. THE parameter editor SHALL support resetting individual parameters to their default value via a reset icon button.
6. WHEN any parameter is changed, THE Research Summary Rail SHALL update to reflect the new configuration (indicator of "unsaved changes" and preview invalidation).

---

### Requirement 21: Strategy Builder — Active Overrides Surface (Phase 2)

**User Story:** As a researcher, I want to see all non-default settings and active overrides at a glance, so that I understand exactly how my configuration differs from the baseline.

#### Acceptance Criteria

1. THE `AdvancedOverridesPanel.razor` SHALL display a summary list of all parameters and settings that differ from their default values.
2. WHEN the user navigates to Step 5 (Review & Launch), THE review panel SHALL highlight all non-default values with a distinct visual treatment.
3. THE overrides summary SHALL be visible in the Research Summary Rail on all steps, showing a count badge (e.g., "3 overrides").
4. WHEN the user clicks an override in the summary, THE Strategy_Builder SHALL navigate to the relevant step and scroll to the parameter.

---

### Requirement 22: Strategy Builder — Timeline Split Editing (Phase 2)

**User Story:** As a researcher, I want to visually edit the IS/OOS/held-out data split on a timeline, so that I can understand and adjust my data allocation intuitively.

#### Acceptance Criteria

1. THE `TimelineSplitVisualizer.razor` SHALL render a horizontal bar showing In-Sample, Out-of-Sample, and Held-Out segments with proportional widths and date labels.
2. WHEN the user drags a segment boundary, THE visualiser SHALL update the split percentages in `BuilderViewModel` and recompute date ranges based on the loaded data file's date range.
3. THE timeline visualiser SHALL display the bar count for each segment.
4. IF the user sets any segment to less than 30 bars, THEN THE visualiser SHALL display a warning that the segment may be too small for meaningful analysis.
5. THE timeline split SHALL default to 70% IS / 15% OOS / 15% Held-Out when no prior configuration exists.

---

### Requirement 23: Strategy Builder — Keyboard Shortcuts (Phase 2)

**User Story:** As a power user, I want keyboard shortcuts for common builder actions, so that I can navigate and configure strategies without reaching for the mouse.

#### Acceptance Criteria

1. WHEN the user presses `Ctrl+Enter` in the Strategy_Builder, THE builder SHALL advance to the next step (equivalent to clicking "Next →").
2. WHEN the user presses `Ctrl+Shift+Enter` in the Strategy_Builder, THE builder SHALL go back to the previous step.
3. WHEN the user presses `Ctrl+S` in the Strategy_Builder, THE builder SHALL save the current draft.
4. WHEN the user presses `?` (with no input focused), THE `ShortcutHelpOverlay.razor` SHALL display a modal listing all available shortcuts.
5. THE keyboard shortcuts SHALL not interfere with text input fields (shortcuts are only active when no text input has focus).

---

### Requirement 24: Strategy Builder — Preflight Validation Enhancement (Phase 2)

**User Story:** As a researcher, I want preflight validation to catch more configuration issues and explain them clearly, so that I do not waste time on runs that will fail or produce meaningless results.

#### Acceptance Criteria

1. THE `PreflightValidator` SHALL check that the selected data file contains at least 100 bars for the configured timeframe and return a warning if fewer.
2. THE `PreflightValidator` SHALL check that all referenced indicators in the condition expression have matching entries in the indicator configuration list.
3. THE `PreflightFindingsPanel.razor` SHALL categorise findings as Errors (blocking), Warnings (non-blocking), and Info (suggestions) with distinct icons and colours.
4. WHEN preflight finds blocking errors, THE "Launch" button on Step 5 SHALL be disabled with a tooltip explaining the blocking issues.
5. THE `PreflightValidator` SHALL check for potential look-ahead bias by verifying that the OOS period does not overlap with the IS period in the data split configuration.

---


### Requirement 25: Robustness Hub Page (Phase 2)

**User Story:** As a researcher, I want a dedicated Robustness Hub page that aggregates all warnings with severity, explanations, and next actions, so that I can systematically address strategy weaknesses.

#### Acceptance Criteria

1. THE Robustness_Hub page SHALL be accessible at `/robustness-hub` and linked from the Dashboard "Flags" KPI card and the navigation menu.
2. THE Robustness_Hub SHALL display all strategies with active robustness warnings, grouped by strategy, with each warning showing severity (Critical, High, Medium, Low), a human-readable explanation, and a recommended action.
3. THE `RobustnessAdvisoryService` SHALL be extended to return `RobustnessWarning` records containing `Severity` (enum), `Code` (string), `Explanation` (string), and `RecommendedAction` (string) instead of plain warning strings.
4. WHEN the user clicks a recommended action on the Robustness_Hub, THE system SHALL navigate to the appropriate study launch page pre-configured for the strategy.
5. THE Robustness_Hub SHALL support filtering by severity level and by strategy.
6. THE Robustness_Hub SHALL display a summary bar showing total warnings by severity across all strategies.

---

### Requirement 26: CPCV Visualisation and Diagnostics (Phase 2)

**User Story:** As a researcher, I want CPCV results displayed with distribution charts and diagnostic metrics, so that I can understand the statistical validity of my strategy beyond a simple pass/fail.

#### Acceptance Criteria

1. THE `CpcvDistributionChart.razor` SHALL render a histogram of CPCV path Sharpe ratios with the in-sample Sharpe marked as a vertical reference line.
2. THE CPCV study detail page SHALL display the probability of backtest overfitting (PBO) as a percentage with a colour-coded badge (green ≤ 10%, amber 10–30%, red > 30%).
3. THE CPCV study detail page SHALL display the distribution of out-of-sample returns across all combinatorial paths.
4. WHEN the PBO exceeds 30%, THE CPCV study detail page SHALL display a prominent warning explaining the overfitting risk and recommending parameter simplification.
5. THE CPCV study detail page SHALL show a table of individual path results sortable by OOS Sharpe, OOS return, and path index.

---

### Requirement 27: 2D Parameter Sweep Heatmaps (Phase 2)

**User Story:** As a researcher, I want 2D parameter sweep results displayed as interactive heatmaps, so that I can identify stable parameter regions and avoid fragile optima.

#### Acceptance Criteria

1. THE `ParameterSweepHeatmap.razor` SHALL render a 2D heatmap with the two swept parameters on X and Y axes and a selected metric (Sharpe, MaxDD, ProfitFactor, WinRate) as the colour intensity.
2. WHEN the user hovers over a heatmap cell, THE chart SHALL display a tooltip with the exact parameter values and all computed metrics for that cell.
3. THE heatmap SHALL highlight the optimal cell with a distinct border and mark the current strategy's parameter values with a crosshair indicator.
4. THE user SHALL be able to switch the displayed metric via a dropdown without re-running the sweep.
5. WHEN a parameter region shows consistent performance across adjacent cells (stability zone), THE heatmap SHALL outline that region with a dashed border.

---

### Requirement 28: Parameter Surface Analysis (Phase 2)

**User Story:** As a researcher, I want parameter stability analysis that identifies flat regions and ridges in the parameter surface, so that I can select robust parameter values.

#### Acceptance Criteria

1. THE `ParameterStabilityWorkflow` SHALL compute a stability score for each parameter combination based on the variance of the target metric across neighbouring cells (±1 step in each dimension).
2. THE parameter surface analysis SHALL identify "stability zones" where the stability score is below a configurable threshold (`ParameterStabilityOptions.StabilityThreshold`, default 0.15).
3. THE parameter surface analysis SHALL identify "ridges" where performance is sensitive to one parameter but stable along another.
4. THE study detail page for parameter stability SHALL display a colour-coded surface plot with stability zones highlighted in green and fragile regions in red.
5. THE study detail page SHALL recommend parameter values from the centre of the largest stability zone.

---

### Requirement 29: Statistical Significance Testing (Phase 2)

**User Story:** As a researcher, I want p-values from permutation testing on my strategy's performance, so that I can distinguish genuine edge from random luck.

#### Acceptance Criteria

1. THE Application layer SHALL implement a `PermutationTestWorkflow` that shuffles trade entry/exit timing N times (configurable, default 1000) and computes the target metric for each permutation.
2. THE `PermutationTestWorkflow` SHALL compute a p-value as the proportion of permuted results that equal or exceed the original strategy's metric.
3. WHEN the p-value is below 0.05, THE study detail page SHALL display "Statistically significant at 95% confidence" with a green badge.
4. WHEN the p-value is above 0.10, THE study detail page SHALL display a warning that the strategy's performance may not be distinguishable from random.
5. THE `PermutationTestWorkflow` SHALL accept an explicit seed for deterministic reproducibility.
6. THE study detail page SHALL display a histogram of permuted metric values with the original strategy's value marked as a vertical line.

---

### Requirement 30: Regime Sensitivity Analysis (Phase 2)

**User Story:** As a researcher, I want to see how my strategy performs across different market regimes (trending, mean-reverting, high-volatility, low-volatility), so that I understand its regime dependencies.

#### Acceptance Criteria

1. THE `RegimeSegmentationWorkflow` SHALL segment the backtest period into regimes based on configurable methodology: volatility percentile (high/medium/low) and trend strength (trending/ranging).
2. THE regime analysis study detail page SHALL display per-regime metrics (Sharpe, MaxDD, WinRate, ProfitFactor) in a comparison table.
3. THE regime analysis study detail page SHALL display the equity curve with regime periods colour-coded as background bands.
4. WHEN a strategy's Sharpe ratio in any regime is below 0.0, THE study detail page SHALL flag that regime as a weakness with a warning badge.
5. THE regime segmentation methodology SHALL be documented in the study configuration and displayed on the study detail page.

---

### Requirement 31: Results Exploration — Run Annotation (Phase 2)

**User Story:** As a researcher, I want to add notes and tags to backtest runs and studies, so that I can record my observations and filter results by context.

#### Acceptance Criteria

1. THE `BacktestResult` record SHALL include optional `IReadOnlyList<string> Tags` and `string? Notes` fields.
2. THE `StudyRecord` SHALL include optional `IReadOnlyList<string> Tags` and `string? Notes` fields.
3. WHEN viewing a run detail page, THE user SHALL be able to add, edit, and remove tags via an inline tag editor.
4. WHEN viewing a run detail page, THE user SHALL be able to add and edit a free-text note via an expandable text area.
5. THE Research_Explorer and Dashboard recent runs table SHALL support filtering by tag.
6. WHEN a tag is clicked in any list view, THE system SHALL filter the current view to show only items with that tag.

---

### Requirement 32: Results Exploration — Compare Runs Workflow (Phase 2)

**User Story:** As a researcher, I want to compare multiple backtest runs side-by-side with delta metrics and overlaid equity curves, so that I can evaluate the impact of parameter or configuration changes.

#### Acceptance Criteria

1. THE `CompareRuns.razor` page SHALL display a comparison table with one column per selected run and rows for all key metrics (Sharpe, Sortino, Calmar, MaxDD, WinRate, ProfitFactor, TotalTrades, RecoveryFactor).
2. THE comparison page SHALL display overlaid equity curves using `OverlaidEquityCurves.razor` with distinct colours per run.
3. THE `RunComparisonDelta.razor` component SHALL compute and display the delta (difference and percentage change) between each run and a user-selected baseline run.
4. THE comparison page SHALL support selecting 2–5 runs from the Dashboard or Research Explorer via checkbox selection.
5. WHEN runs have different data periods, THE comparison page SHALL align equity curves by normalising to percentage returns from the common start date.

---

### Requirement 33: Results Exploration — Journaling and Audit Trail (Phase 2)

**User Story:** As a researcher, I want a journal recording strategy promotion, rejection, and revision decisions, so that I have an audit trail of my research process.

#### Acceptance Criteria

1. THE Application layer SHALL define a `ResearchJournalEntry` record with `EntryId`, `StrategyId`, `StrategyVersionId`, `Timestamp`, `Action` (Promoted, Rejected, Revised, Noted), `Reason` (string), and `FromStage`/`ToStage` (optional `DevelopmentStage`).
2. WHEN a strategy's `DevelopmentStage` changes, THE system SHALL automatically create a journal entry recording the transition with the user's reason.
3. THE strategy detail page SHALL display a chronological journal timeline showing all entries for that strategy.
4. THE user SHALL be able to manually add journal entries with free-text notes from the strategy detail page.
5. THE `IResearchJournalRepository` SHALL support querying entries by strategy ID and by date range.

---

### Requirement 34: Results Exploration — Extended Export (Phase 2)

**User Story:** As a researcher, I want to export study results, comparison reports, and portfolio results in addition to single-run exports, so that I can share findings externally.

#### Acceptance Criteria

1. THE `ExportMenu.razor` SHALL be extended to support exporting study results (Monte Carlo, Walk-Forward, Sweep, CPCV) as Markdown reports.
2. THE Export_Service SHALL support exporting a run comparison as a Markdown table with delta metrics.
3. THE Export_Service SHALL support exporting portfolio backtest results including per-asset attribution and correlation matrix as Markdown.
4. WHEN exporting a study result, THE export SHALL include the study configuration, key findings, and any warnings or recommendations.
5. THE Export_Service SHALL support a new `ExportFormat.Pdf` option that generates a PDF report from the Markdown content (Phase 3 delivery acceptable).

---

### Requirement 35: Portfolio Multi-Asset Evolution (Phase 3)

**User Story:** As a researcher, I want to backtest portfolios of multiple assets with allocation strategies and per-asset attribution, so that I can evaluate diversified strategy performance.

#### Acceptance Criteria

1. THE `PortfolioBacktestRunner` SHALL support configurable allocation modes: Equal Weight, Volatility Parity, Risk Parity, and Custom Weights.
2. THE portfolio result page SHALL display per-asset contribution to total return as a stacked area chart.
3. THE portfolio result page SHALL display the correlation matrix as an interactive heatmap using `Blazor-ApexCharts`.
4. THE `PortfolioBacktestResult` SHALL include per-asset metrics (Sharpe, MaxDD, Return) alongside the aggregate portfolio metrics.
5. THE portfolio configuration UI (`PortfolioRunSetup.razor`) SHALL allow adding/removing symbols, selecting per-symbol strategies, and configuring allocation weights.
6. WHEN a portfolio backtest completes, THE result page SHALL display a risk decomposition showing each asset's contribution to portfolio volatility.

---

### Requirement 36: Portfolio-Level Metrics (Phase 3)

**User Story:** As a researcher, I want portfolio-specific metrics (diversification ratio, maximum correlation, turnover, tracking error), so that I can assess portfolio construction quality.

#### Acceptance Criteria

1. THE `PortfolioBacktestResult` SHALL include `DiversificationRatio` computed as the ratio of weighted-average individual volatilities to portfolio volatility.
2. THE `PortfolioBacktestResult` SHALL include `MaxPairwiseCorrelation` as the highest off-diagonal value in the correlation matrix.
3. THE `PortfolioBacktestResult` SHALL include `AnnualisedTurnover` (already computed) and `TrackingError` relative to an equal-weight benchmark.
4. THE portfolio result page SHALL display these metrics in a dedicated "Portfolio Health" card with colour-coded thresholds.
5. WHEN `MaxPairwiseCorrelation` exceeds 0.8, THE result page SHALL display a diversification warning.

---

### Requirement 37: Real-Time Status Streaming (Phase 2)

**User Story:** As a researcher, I want study progress updates pushed to the UI in real-time without manual refresh, so that I can monitor execution from any page.

#### Acceptance Criteria

1. THE `BlazorProgressReporter` SHALL use a `Channel<ProgressSnapshot>` to decouple progress production from UI consumption.
2. WHEN a study is running, THE `JobStatusPage.razor` SHALL subscribe to the progress channel and update the display without polling or page refresh.
3. THE progress streaming SHALL support multiple concurrent subscribers (e.g., Dashboard badge + Job Status page).
4. WHEN the user navigates away from the job status page and returns, THE page SHALL display the latest progress state immediately.
5. THE progress channel SHALL have a bounded capacity of 16 items with `BoundedChannelFullMode.DropOldest` to prevent memory growth.

---

### Requirement 38: Strategy Builder — Beginner and Expert Modes (Phase 2)

**User Story:** As a new user, I want a simplified builder flow, and as an expert, I want full control over all parameters, so that the builder serves both audiences.

#### Acceptance Criteria

1. THE Strategy_Builder SHALL offer a mode toggle (Beginner / Expert) persisted in user settings.
2. WHILE in Beginner mode, THE Strategy_Builder SHALL hide advanced parameters (slippage model selection, commission model selection, fill delay, realism profile) and use sensible defaults.
3. WHILE in Beginner mode, THE Strategy_Builder SHALL display contextual help tooltips on each section explaining what the settings mean.
4. WHILE in Expert mode, THE Strategy_Builder SHALL display all parameters including `AdvancedOverridesPanel` and `TimelineSplitVisualizer`.
5. WHEN the user switches from Beginner to Expert mode, THE Strategy_Builder SHALL preserve all current settings and reveal the additional controls.

---

### Requirement 39: Research Explorer Discoverability (Phase 2)

**User Story:** As a researcher, I want the Research Explorer to surface study relationships, suggest next studies, and provide quick-launch actions, so that I can navigate my research efficiently.

#### Acceptance Criteria

1. THE Research_Explorer SHALL display a "Related Studies" section on each study row showing other studies for the same strategy version.
2. THE Research_Explorer SHALL display a "Suggested Studies" banner when a strategy has fewer than 5 completed studies, recommending the next study type from the Research_Checklist.
3. WHEN the user selects a strategy filter, THE Research_Explorer SHALL display the strategy's checklist completion status as a progress indicator above the study table.
4. THE Research_Explorer study table SHALL support multi-select with a "Compare Selected" action for studies of the same type.
5. THE Research_Explorer SHALL display study duration and cost estimate (from `StudyCostEstimatorService`) in the table.

---

### Requirement 40: Mobile and Tablet Layouts (Phase 3)

**User Story:** As a researcher using a tablet, I want all major pages to be usable with touch interactions and adapted layouts, so that I can review results away from my desk.

#### Acceptance Criteria

1. WHEN the viewport width is below 960px, THE Strategy_Builder SHALL collapse the Research Summary Rail below the main content instead of beside it.
2. WHEN the viewport width is below 960px, THE comparison page SHALL display runs in a vertical card layout instead of a horizontal table.
3. WHEN the viewport width is below 600px, THE navigation menu SHALL collapse to a hamburger menu with a slide-out drawer.
4. ALL touch targets (buttons, links, interactive elements) SHALL have a minimum size of 44×44px on viewports below 960px.
5. THE charts (equity curve, heatmaps, histograms) SHALL be responsive and resize to fit their container width without horizontal scrolling.

---

### Requirement 41: Unit Testing — Domain and Service Changes (Phase 1)

**User Story:** As a developer, I want comprehensive unit tests for all new domain types and service changes, so that regressions are caught early.

#### Acceptance Criteria

1. THE `StrategyTypeId` value object SHALL have unit tests verifying equality, `ToString()`, implicit conversion, and JSON serialisation round-trip.
2. THE extended `RobustnessAdvisoryService` (returning `RobustnessWarning` records) SHALL have unit tests verifying all threshold conditions produce correct severity levels.
3. THE `PagedResult<T>` record SHALL have unit tests verifying `TotalPages` computation for edge cases (0 items, exact page boundary, partial last page).
4. THE `PermutationTestWorkflow` SHALL have a unit test verifying deterministic output given the same seed and inputs.
5. THE `ResearchJournalEntry` record SHALL have a JSON round-trip unit test.

---

### Requirement 42: Property-Based Testing — Condition Parser Round-Trip (Phase 2)

**User Story:** As a developer, I want a property-based test proving that parsing a pretty-printed condition expression always produces an equivalent AST, so that the Condition_Builder UI never corrupts user rules.

#### Acceptance Criteria

1. FOR ALL valid `ConditionNode` ASTs generated by an FsCheck arbitrary, parsing the output of `ConditionPrettyPrinter.Print(ast)` via `ConditionParser.Parse` SHALL produce a structurally equivalent AST.
2. THE property test SHALL generate ASTs with depth up to 4, covering `LogicalNode`, `ComparisonNode`, `CrossNode`, `IndicatorRefNode`, `PriceRefNode`, and `LiteralNode`.
3. THE property test SHALL run a minimum of 100 iterations (`[Property(MaxTest = 100)]`).
4. THE property test SHALL be tagged with `// Feature: research-platform-v9, Property 1: Condition parser round-trip`.

---

### Requirement 43: Integration Testing — Paginated Repository (Phase 1)

**User Story:** As a developer, I want integration tests verifying that paginated repository methods return correct pages with proper counts, so that pagination logic is validated against real storage.

#### Acceptance Criteria

1. THE integration test SHALL seed 25 `BacktestResult` records and verify that `ListPagedAsync(page: 1, pageSize: 10)` returns exactly 10 items with `TotalCount == 25`.
2. THE integration test SHALL verify that `ListPagedAsync(page: 3, pageSize: 10)` returns exactly 5 items (the last page).
3. THE integration test SHALL verify that filtering by `strategyTypeFilter` returns only matching records with correct `TotalCount`.
4. THE integration test SHALL verify that filtering by `BacktestStatus.Failed` excludes completed runs.

---

### Requirement 44: Quant Validation Testing (Phase 2)

**User Story:** As a developer, I want validation tests for statistical computations (permutation p-values, CPCV PBO, parameter stability scores), so that quant logic is verified against known analytical results.

#### Acceptance Criteria

1. THE permutation test workflow SHALL have a unit test with a known-outcome dataset where the strategy is demonstrably random (expected p-value ≈ 0.5 ± 0.1 over 1000 permutations with fixed seed).
2. THE parameter stability score computation SHALL have a unit test with a flat surface (all cells equal) producing a stability score of 0.0.
3. THE parameter stability score computation SHALL have a unit test with a steep gradient producing a stability score above the threshold.
4. THE CPCV PBO computation SHALL have a unit test with a dataset where all OOS paths underperform IS (expected PBO ≈ 1.0).
5. ALL quant validation tests SHALL use deterministic seeds and produce identical results across runs.

---

### Requirement 45: Performance Testing (Phase 3)

**User Story:** As a developer, I want performance benchmarks for large datasets and paginated queries, so that I can verify the application remains responsive at scale.

#### Acceptance Criteria

1. THE integration test suite SHALL include a benchmark verifying that `ListPagedAsync` on a repository with 1000 records completes within 200ms.
2. THE integration test suite SHALL include a benchmark verifying that the Dashboard `OnInitializedAsync` with 50 strategies and 500 runs completes within 500ms.
3. THE integration test suite SHALL include a benchmark verifying that `PortfolioBacktestRunner.MergeEquityCurves` with 10 symbols × 5000 bars completes within 100ms.
4. THE benchmark results SHALL be tracked in a `BenchmarkDotNet` project and compared against baseline on CI.

---


### Requirement 46: Hover, Focus, and Active State Consistency (Phase 1)

**User Story:** As a user, I want consistent visual feedback on hover, focus, and active states across all interactive elements, so that I always know what is clickable and what is selected.

#### Acceptance Criteria

1. THE Design_Tokens system SHALL define `--state-hover-opacity`, `--state-focus-ring-color`, `--state-focus-ring-width`, and `--state-active-scale` variables.
2. ALL clickable `MudPaper` elements (strategy strip cards, KPI tiles, nav cards) SHALL display a hover elevation change and focus ring on keyboard focus.
3. WHEN a table row is hovered, THE row SHALL display a subtle background colour change using `--state-hover-opacity`.
4. WHEN a button receives keyboard focus, THE button SHALL display a visible focus ring with minimum 2px width and sufficient contrast (3:1 against adjacent colours).
5. THE active (pressed) state for buttons SHALL use a slight scale reduction (`transform: scale(0.98)`) for tactile feedback.

---

### Requirement 47: Empty, Error, and Loading States for All Major Views (Phase 1)

**User Story:** As a user, I want every major view to handle empty, error, and loading states gracefully, so that I am never confused by blank screens or unresponsive pages.

#### Acceptance Criteria

1. THE Strategy Library page SHALL display a skeleton loading state while fetching strategies and an empty state with "Create your first strategy" when none exist.
2. THE Research Explorer SHALL display a skeleton loading state while fetching studies and an empty state explaining how to launch a first study.
3. THE Backtest History page SHALL display a skeleton loading state and an empty state with a link to the Strategy Builder.
4. IF any page encounters a data loading error, THEN THE page SHALL display an error alert with the error message and a "Retry" button that re-invokes the data loading method.
5. THE Strategy Detail page SHALL display a "Strategy not found" message with a link back to the library when the requested strategy ID does not exist.

---

### Requirement 48: Navigation Semantics and Affordances (Phase 1)

**User Story:** As a user, I want clickable cards to look clickable and navigation to be predictable, so that I can find features without guessing.

#### Acceptance Criteria

1. ALL clickable cards (Dashboard nav cards, strategy strip cards, KPI tiles) SHALL display `cursor: pointer` and a hover elevation change.
2. THE Dashboard nav cards SHALL include a right-arrow icon or "→" indicator to signal navigation affordance.
3. WHEN a user navigates to a page via a card click, THE browser URL SHALL update to reflect the new route (no hidden state transitions).
4. THE navigation menu SHALL highlight the currently active page with a distinct background colour and left border indicator.
5. ALL navigation links SHALL use `<a>` elements or MudBlazor `Href` properties (not `@onclick` with `Nav.NavigateTo`) for proper browser history and right-click "Open in new tab" support.

---

### Requirement 49: Study Comparison Tools (Phase 2)

**User Story:** As a researcher, I want to compare results across studies of the same type (e.g., two Monte Carlo runs with different parameters), so that I can evaluate the impact of methodology changes.

#### Acceptance Criteria

1. THE Research_Explorer SHALL support selecting 2–3 studies of the same type and navigating to a study comparison view.
2. THE study comparison view SHALL display key metrics side-by-side (e.g., Monte Carlo: median Sharpe, 5th percentile Sharpe, ruin probability).
3. THE study comparison view SHALL overlay distribution charts (e.g., Monte Carlo fan charts, CPCV histograms) from multiple studies.
4. THE study comparison view SHALL highlight which study configuration produced better robustness outcomes.
5. WHEN studies have different configurations, THE comparison view SHALL display a configuration diff table showing parameter differences.

---

### Requirement 50: Clear IS/OOS/WF/CPCV/Held-Out Distinction (Phase 2)

**User Story:** As a researcher, I want the UI to clearly label and colour-code which data segment (In-Sample, Out-of-Sample, Walk-Forward fold, CPCV path, Held-Out) each result comes from, so that I never confuse in-sample with out-of-sample performance.

#### Acceptance Criteria

1. THE Design_Tokens system SHALL define distinct colours for each data segment: IS (blue), OOS (green), Walk-Forward (purple), CPCV (teal), Held-Out (orange).
2. WHEN displaying equity curves from walk-forward studies, THE `WalkForwardCompositeChart.razor` SHALL colour each fold's OOS segment distinctly from the IS training segment.
3. THE study detail pages SHALL display a legend explaining the data segment colour coding.
4. WHEN a metric is computed from OOS data, THE metric label SHALL include an "(OOS)" suffix.
5. THE Research Summary Rail in the Strategy_Builder SHALL clearly label whether displayed metrics come from IS preview or OOS validation.

---

### Requirement 51: Robustness Failure Explanation (Phase 2)

**User Story:** As a researcher, I want clear explanations of why a strategy fails robustness checks, so that I can take targeted corrective action.

#### Acceptance Criteria

1. WHEN the `RobustnessAdvisoryService` generates a warning, THE warning SHALL include a `Cause` field explaining the likely reason (e.g., "Sharpe > 3.0 often indicates curve-fitting to noise in the training period").
2. WHEN the `RobustnessAdvisoryService` generates a warning, THE warning SHALL include a `Remediation` field with a specific actionable suggestion (e.g., "Run a walk-forward study to validate out-of-sample performance").
3. THE Robustness_Hub SHALL group warnings by cause category (Overfitting, Insufficient Data, Execution Unrealism, Parameter Fragility).
4. WHEN a strategy has multiple related warnings (e.g., high Sharpe + no walk-forward), THE Robustness_Hub SHALL display them as a connected cluster with a combined explanation.

---

### Requirement 52: Realism Modelling and Impact Reporting (Phase 2)

**User Story:** As a researcher, I want to see how different realism profiles affect my strategy's performance, so that I can assess the gap between idealised and realistic execution.

#### Acceptance Criteria

1. THE `RealismSensitivityWorkflow` result page SHALL display a comparison table showing key metrics (Sharpe, MaxDD, ProfitFactor) across all three realism profiles (FastResearch, StandardBacktest, BrokerConservative).
2. THE realism impact page SHALL display overlaid equity curves for each profile.
3. WHEN the performance degradation from FastResearch to BrokerConservative exceeds 50% of Sharpe, THE result page SHALL display a "High Realism Sensitivity" warning.
4. THE realism impact page SHALL display a breakdown of execution cost impact: slippage contribution, commission contribution, and fill delay contribution.
5. THE `RealismSensitivityWorkflow` SHALL be launchable from the Strategy_Builder Step 4 (Realism & Risk Profile) as a "Test Realism Impact" button.

---

### Requirement 53: Research Pipeline Stage Transitions (Phase 2)

**User Story:** As a researcher, I want clear stage transition rules and UI guidance for promoting or rejecting strategies through the research pipeline, so that my workflow is systematic.

#### Acceptance Criteria

1. THE system SHALL enforce stage transition rules: Exploration → Validation requires at least 3 completed studies; Validation → FinalTest requires walk-forward pass and CPCV PBO < 30%; FinalTest → Live requires held-out test pass.
2. WHEN a strategy meets the criteria for promotion, THE strategy detail page SHALL display a "Ready to Promote" banner with a one-click promotion button.
3. WHEN the user attempts to promote a strategy that does not meet criteria, THE system SHALL display a modal listing the unmet requirements.
4. WHEN a strategy is rejected, THE system SHALL prompt for a rejection reason and create a journal entry.
5. THE strategy detail page SHALL display the current stage with a visual pipeline indicator showing completed and remaining stages.

---

### Requirement 54: Non-Functional — Performance Requirements

**User Story:** As a researcher, I want the application to remain responsive under typical workloads, so that my research flow is not interrupted by slow UI.

#### Acceptance Criteria

1. THE Dashboard SHALL complete initial data loading and render within 2 seconds when the repository contains up to 100 strategies and 1000 runs.
2. THE Research Explorer SHALL render the study table within 1 second for up to 500 studies.
3. THE Strategy Builder SHALL transition between steps within 200ms (excluding network calls for draft persistence).
4. THE parameter sweep heatmap SHALL render within 500ms for a 20×20 grid (400 cells).
5. THE portfolio backtest with 5 symbols × 5000 bars SHALL complete within 30 seconds on a 4-core machine.
6. THE `ListPagedAsync` repository methods SHALL complete within 100ms for repositories with up to 5000 records.

---

### Requirement 55: Non-Functional — Accessibility Compliance

**User Story:** As a product owner, I want the application to meet WCAG 2.1 Level AA for all new and modified components, so that the application is usable by researchers with disabilities.

#### Acceptance Criteria

1. ALL text content SHALL maintain a minimum contrast ratio of 4.5:1 against its background (3:1 for large text ≥ 18px).
2. ALL form inputs SHALL have associated `<label>` elements or `aria-label` attributes.
3. ALL interactive elements SHALL be reachable and operable via keyboard Tab navigation.
4. THE application SHALL not use colour as the sole means of conveying information (e.g., status badges SHALL include text labels or icons in addition to colour).
5. ALL images and icons that convey meaning SHALL have `alt` text or `aria-label` attributes.
6. THE application SHALL support browser zoom up to 200% without loss of content or functionality.

---

### Requirement 56: Non-Functional — Data Integrity

**User Story:** As a researcher, I want my research data (runs, studies, journal entries, annotations) to be persisted reliably, so that I never lose work.

#### Acceptance Criteria

1. WHEN a backtest run completes, THE repository SHALL persist the result before returning success to the caller.
2. WHEN a study is cancelled or fails, THE repository SHALL persist the final status and any partial results before releasing resources.
3. THE JSON file repository SHALL use atomic write operations (write to temp file, then rename) to prevent corruption from interrupted writes.
4. WHEN the application starts, THE repository SHALL validate the integrity of stored JSON files and log warnings for any corrupted records without crashing.

---

### Requirement 57: Success Metrics

**User Story:** As a product owner, I want measurable success criteria for the V9 upgrade, so that I can evaluate whether the investment achieved its goals.

#### Acceptance Criteria

1. THE Dashboard initial load time SHALL be reduced by at least 40% compared to the current implementation (measured by eliminating full-dataset loading via pagination).
2. THE number of inline style attributes across all Razor files SHALL be reduced to zero (all styling via Design_Tokens or MudBlazor classes).
3. THE Research_Checklist completion rate across active strategies SHALL increase by at least 20% within 30 days of deployment (measured by average `PassedCount` across strategies).
4. THE application SHALL pass automated accessibility audit (axe-core) with zero critical or serious violations on Dashboard, Strategy Builder, and Research Explorer pages.
5. THE unit test coverage for Application layer services SHALL reach at least 80% line coverage.
6. THE property-based test for Condition Parser round-trip SHALL pass 100 iterations without failure.

---

### Requirement 58: Phased Delivery Plan

**User Story:** As a product owner, I want a clear phased delivery plan, so that I can track progress and prioritise work.

#### Acceptance Criteria

1. THE Phase 1 delivery SHALL include Requirements 1–17, 41, 43, 46–48 (architecture, design tokens, Dashboard redesign, progress reporting, accessibility foundations, core testing).
2. THE Phase 2 delivery SHALL include Requirements 18–34, 37–39, 42, 44, 49–53 (Strategy Builder redesign, Robustness Hub, quant improvements, results exploration, real-time streaming, statistical testing).
3. THE Phase 3 delivery SHALL include Requirements 35–36, 40, 45 (portfolio evolution, mobile layouts, performance benchmarks, PDF export).
4. EACH phase SHALL be independently deployable without breaking existing functionality from prior phases.
5. THE Phase 1 delivery SHALL not introduce breaking changes to existing JSON persistence formats (backward-compatible field additions only).
