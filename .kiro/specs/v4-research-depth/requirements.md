# V4 Requirements — Research Depth, Analytics & UX Completeness

## Introduction

V4 builds on the fully-implemented V3 product surface (strategy identity, guided builder, research explorer, prop firm lab) to fill 21 identified gaps across three categories: architectural completeness (10 major gaps), implementation quality (5 minor gaps), and research/analytics depth (6 gaps). The goal is to transform TradingResearchEngine from a backtest runner with a nice UI into a genuine strategy research platform with structured validation lifecycles, anti-overfitting tooling, and production-grade UX for failure states, progress, export, and background execution.

V4 is still single-user, local/single-tenant. No authentication, no multi-user, no database persistence. The engine Core layer is not modified — all new concepts live in Application, Infrastructure, and Web.

---

## Requirements

### Requirement 1 — Product Scope Declaration (Gap A1)

**User Story:** As a user, I want the product scope and deployment model explicitly declared, so that I understand the system boundaries and there is no ambiguity about identity, persistence, or runtime requirements.

#### Acceptance Criteria

1. THE system SHALL be a single-user local desktop application with no login or authentication required.
2. `StrategyId` SHALL be unique within the local data directory; no `UserId` namespace is needed.
3. ALL repositories SHALL read/write from a configured local path (JSON files).
4. THE minimum supported runtime SHALL be .NET 8, Blazor Server on localhost, Chrome/Edge latest two versions.
5. Multi-user, cloud deployment, and authentication are explicitly out of scope for V4.

---

### Requirement 2 — Error Handling & Failed Run UX (Gap A2)

**User Story:** As a user, I want clear visibility into failed and cancelled runs with actionable recovery options, so that I can diagnose problems and retry without losing context.

#### Acceptance Criteria

1. WHEN a `ScenarioConfig` fails validation before execution starts, THEN the system SHALL show inline validation errors on the Run Detail screen and SHALL NOT create a `BacktestResult` record.
2. WHEN an unhandled exception occurs during execution, THEN the system SHALL create a `BacktestResult` with `Status = Failed` and store the exception message in a `FailureDetail` field (nullable string, added as a trailing parameter on `BacktestResult` in Core — same pattern as `StrategyVersionId`).
3. WHEN execution exceeds a configurable timeout (default: 60s for single runs, 5 minutes for studies), THEN the system SHALL treat it as a runtime error with `Status = Failed`.
4. THE `BacktestStatus` enum SHALL include: `Completed`, `Failed`, `Cancelled` (already present).
5. Failed runs SHALL appear in the Strategy run list with a ❌ badge.
6. THE Run Detail failure state SHALL show the error message collapsed by default, with [View Full Error] and [Copy to Clipboard] actions.
7. THE Run Detail failure state SHALL offer [Re-run with same config] and [Edit Config & Re-run] actions.

---

### Requirement 3 — Data Files Screen (Gap A3)

**User Story:** As a user, I want a dedicated Data Files screen to add, preview, validate, and manage my CSV data files, so that I can ensure my backtests use clean data and the Strategy Builder can reference validated files.

#### Acceptance Criteria

1. THE Data Files screen SHALL list all CSV files in the configured data directories with: filename, detected symbol, detected timeframe, bar count, file size, date range, and validation status.
2. WHEN a user adds a file (by path or drag-and-drop), THEN the system SHALL auto-detect: symbol hint, timeframe hint, date range, total bar count, and column format by reading the first and last rows.
3. THE system SHALL validate files against these rules: (a) at least 4 columns (Date, O, H, L, C), (b) dates monotonically increasing, (c) no more than 5% missing bars for the detected timeframe, (d) O/H/L/C values are positive decimals.
4. Validation failures SHALL show per-rule details with row numbers.
5. THE Strategy Builder Step 2 data source picker SHALL show only validated files.
6. WHEN deleting a file used by an existing run, THEN the system SHALL show a warning but SHALL NOT delete the run record.
7. A `DataFileRecord` record SHALL be defined in Application with fields: `FileId`, `FileName`, `FilePath`, `DetectedSymbol`, `DetectedTimeframe`, `FirstBar`, `LastBar`, `BarCount`, `ValidationStatus`, `ValidationError`, `AddedAt`.
8. A `ValidationStatus` enum SHALL be defined: `Pending`, `Valid`, `Invalid`.

---

### Requirement 4 — V2 Data Migration (Gap A4)

**User Story:** As an existing user upgrading from V2/V2.1, I want my existing backtest results to be visible in the V4 UI without data loss, so that I don't lose my research history.

#### Acceptance Criteria

1. ON first startup after V4 upgrade, the system SHALL create a synthetic `StrategyIdentity` called `"Imported (Pre-V4)"` with a single `StrategyVersion` called `"v0 (imported)"`.
2. ALL existing `BacktestResult` JSON files without a `StrategyVersionId` SHALL be linked to this imported version.
3. Original JSON files SHALL NOT be deleted or modified in place.
4. Migrated runs SHALL appear in the Strategy Library under an "Imported" group, visually distinct (greyed out, no version history).
5. A `MigrationService` in Infrastructure SHALL run once on startup, check for a `migration_v4.lock` file, and skip if already run.
6. Migration failure SHALL be logged and SHALL NOT crash the application.

---

### Requirement 5 — Real-Time Execution Progress (Gap A5)

**User Story:** As a user, I want to see real-time progress during backtest and study execution, with the ability to cancel, so that I know what's happening and can abort long-running work.

#### Acceptance Criteria

1. WHEN a single run is executing, THEN the UI SHALL show an indeterminate spinner with bar count progress and elapsed time.
2. WHEN a study is executing, THEN the UI SHALL show a determinate progress bar with: current/total count, percentage, and elapsed time (e.g. "Simulating path 347 of 1000 (34%)").
3. A [Cancel] button SHALL be available during any execution.
4. ON cancellation, the system SHALL set `Status = Cancelled` and persist partial results where meaningful.
5. An `IProgressReporter` interface SHALL be defined in Application with a `Report(int current, int total, string label)` method.
6. `RunScenarioUseCase` and all study workflows SHALL accept an optional `IProgressReporter`.

---

### Requirement 6 — Export & Report Formats (Gap A6)

**User Story:** As a user, I want to export any completed run to Markdown, CSV, and JSON formats, so that I can share results, analyse in external tools, and maintain records.

#### Acceptance Criteria

1. ANY completed run SHALL be exportable to: Markdown report, CSV trade log, CSV equity curve, and JSON (full result + config).
2. THE Markdown report SHALL include: header (strategy name, version, date, status, duration), configuration table, key metrics table, robustness notes (if studies exist), and full trade log.
3. THE CSV trade log SHALL include columns: `EntryDate`, `ExitDate`, `Direction`, `Symbol`, `Quantity`, `EntryPrice`, `ExitPrice`, `GrossPnL`, `NetPnL`, `MAE`, `MFE`, `HoldingPeriodBars`.
4. THE JSON export SHALL be the full `BacktestResult` serialized, round-tripping without data loss.
5. THE equity curve CSV SHALL include columns suitable for Excel/Python/Jupyter consumption.
6. Monte Carlo studies SHALL export a CSV of path summary statistics (P10/P50/P90 end equity, ruin flag per path).
7. Walk-forward studies SHALL export per-window OOS metrics as CSV.
8. AN `IReportExporter` interface SHALL be defined in Application with: `ExportMarkdownAsync`, `ExportTradeCsvAsync`, `ExportEquityCsvAsync`, `ExportJsonAsync`.
9. THE Run Detail screen SHALL show an Export dropdown menu with all four format options.

---

### Requirement 7 — Study Cancellation & Partial Results (Gap A7)

**User Story:** As a user, I want to cancel long-running studies and still see results from completed portions, so that I don't lose work when a study takes too long.

#### Acceptance Criteria

1. ALL study types SHALL support cancellation via a [Cancel Study] button.
2. WHEN a Monte Carlo study is cancelled, THEN completed paths SHALL be stored and results shown with a "⚠️ Partial: N of M paths completed" banner. Verdicts SHALL be suppressed until at least 200 paths complete.
3. WHEN a walk-forward study is cancelled, THEN completed windows SHALL be stored and the composite OOS equity curve drawn from completed windows only.
4. WHEN a parameter sweep is cancelled, THEN completed combinations SHALL be stored and the heatmap shown with completed cells filled and incomplete cells greyed out.
5. `StudyRecord` SHALL include `IsPartial` (bool), `CompletedCount` (int), and `TotalCount` (int) fields.
6. Partial studies SHALL be visually distinct in the Research Explorer (amber badge: "Partial — N%").

---

### Requirement 8 — Custom Firm Rule Pack Editor (Gap A8)

**User Story:** As a user, I want to create and edit custom prop firm rule packs, so that I can evaluate my strategies against firms not included in the pre-built packs.

#### Acceptance Criteria

1. THE custom rule pack editor SHALL allow setting: Firm Name, Challenge Name, Account Size, Payout Split, and one or more phases.
2. EACH phase SHALL allow setting: Phase Name, Profit Target %, Max Daily DD %, Max Total DD %, Min Trading Days, Max Trading Days (optional), Consistency Rule % (optional), and Trailing Drawdown (yes/no).
3. Validation: (a) Firm Name and Challenge Name required, (b) at least one phase required, (c) Profit Target and Max DD required per phase, (d) Consistency Rule % must be 1–100 if entered, (e) no duplicate phase names within a pack.
4. Custom packs SHALL be stored as JSON with `IsBuiltIn = false` and appear in the firm selector with a "(Custom)" suffix.
5. Built-in packs SHALL be read-only; the editor SHALL open in view-only mode with a "Duplicate & Edit" button.

---

### Requirement 9 — Background Execution & Notifications (Gap A9)

**User Story:** As a user, I want studies to run in the background so I can navigate freely, with a persistent status bar showing progress and completion notifications.

#### Acceptance Criteria

1. ALL study executions SHALL be non-blocking: the user can navigate away and the study continues.
2. A persistent Execution Status Bar (32px) SHALL be visible at the bottom of the app shell during any active execution, showing: study type, strategy name, progress %, and [View] / [Stop] actions.
3. ON completion, the status bar SHALL transition to a notification showing: success/failure status, summary, and [View Results] / [Dismiss] actions.
4. Single-run backtests (< 60s expected) SHALL execute inline on the current page.
5. Dismissed notifications SHALL still be accessible via the Strategy's study list.

---

### Requirement 10 — Dashboard, Settings & REQ Index (Gap A10)

**User Story:** As a user, I want a complete Dashboard with quick-launch actions, strategy health overview, and research pipeline status, plus a fully-specified Settings screen.

#### Acceptance Criteria

1. THE Dashboard SHALL include: Quick Launch actions (New Strategy, Run Last Config, Compare), Recent Runs list, Active Studies panel, Strategy Health Overview table (strategy, version, Sharpe, robustness verdict), and Research Pipeline summary (count of strategies by development stage).
2. THE Settings screen SHALL include: configurable directory paths (Results, Data Files, Rule Packs), execution defaults (Default Realism Profile, Study Timeout, Single-Run Timeout), read-only list of registered strategies, and About section (version, build date, changelog link, migration check).
3. ALL Settings values SHALL be stored in `appsettings.json` and exposed via a strongly-typed `AppSettings` record injected via `IOptions<AppSettings>`.
4. A Requirements Index table (REQ-V4-01 through REQ-V4-21) SHALL exist in this document.

---

### Requirement 11 — Responsive Behaviour (Gap B1)

**User Story:** As a user, I want the application to be fully functional on desktop and usable on tablet, so that I can work on different screen sizes.

#### Acceptance Criteria

1. ALL screens SHALL be fully functional at 1024px+ viewport width.
2. ALL screens SHALL be usable at 768px viewport width (tablet landscape): sidebar collapses to top nav, charts remain full-width, KPI cards reflow to 2×2 grid, tables gain horizontal scroll.
3. Mobile (< 768px) is explicitly out of scope for V4.

---

### Requirement 12 — Keyboard Shortcuts & Accessibility (Gap B2)

**User Story:** As a user, I want keyboard shortcuts for common actions and baseline accessibility compliance, so that I can work efficiently and the application is usable with assistive technologies.

#### Acceptance Criteria

1. THE system SHALL support global keyboard shortcuts: `N` (New Strategy), `R` (Re-run last config from Dashboard), `?` (Show shortcut reference), `Esc` (Close modal/cancel overlay).
2. ALL interactive elements SHALL be keyboard-reachable via Tab/Shift+Tab with a visible focus ring.
3. ALL charts SHALL include a data table alternative accessible to screen readers (hidden by default, revealed via "Show data table" toggle).
4. ALL icon-only buttons SHALL carry an `aria-label`.
5. ALL status badges SHALL carry an `aria-label` in addition to the visual icon.
6. Colour SHALL NOT be the sole indicator of status.
7. ALL form fields SHALL have associated `<label>` elements.

---

### Requirement 13 — Research Explorer Screen (Gap B3)

**User Story:** As a user, I want a Research Explorer that lists all studies across all strategies, so that I can browse and manage my research from a single screen.

#### Acceptance Criteria

1. THE Research Explorer SHALL list all studies grouped by strategy and version.
2. A "New Study" action SHALL be available from both the Research Explorer and the Run Detail screen.
3. Study list rows SHALL show: type badge, parent strategy/version, status, created date, partial/complete indicator.
4. Clicking a study row SHALL navigate to the Study Detail screen.

---

### Requirement 14 — Study Detail Screens (Gap B3)

**User Story:** As a user, I want dedicated study detail screens for each study type with appropriate visualisations and metrics, so that I can interpret research results effectively.

#### Acceptance Criteria

1. THE Monte Carlo Study Detail SHALL show: P10/P50/P90 equity fan chart, ruin probability, Robustness Verdict, path count, path distribution histogram, PBO (if CPCV data available), and DSR.
2. THE Walk-Forward Study Detail SHALL show: composite OOS equity curve, per-window OOS Sharpe, IS/OOS Sharpe comparison, worst-window drawdown, parameter drift score, and rolling/anchored mode toggle.
3. THE Sensitivity Study Detail SHALL show: Sharpe heatmap (2 parameters), stability score, fragility index, parameter island flag, and area within 20% of peak Sharpe highlighted.
4. THE Parameter Sweep Study Detail SHALL show: sortable table of all parameter combinations with key metrics, and export-to-CSV action.

---

### Requirement 15 — Persistence Layer (Gap B3)

**User Story:** As a developer, I want all new repository interfaces defined with standard CRUD signatures and JSON file implementations, so that the persistence layer is consistent and testable.

#### Acceptance Criteria

1. `IStrategyRepository`, `IStudyRepository` (already exist), plus new `IDataFileRepository` SHALL be defined with standard CRUD signatures.
2. ALL implementations SHALL use JSON file store consistent with the existing `IRepository<T>` pattern.
3. ALL repository interfaces SHALL have corresponding unit tests.

---

### Requirement 16 — Strategy Detail Screen (Gap B4)

**User Story:** As a user, I want a comprehensive Strategy Detail screen showing versions, runs, studies, prop evaluations, and research progress, so that I have a complete view of my strategy's development.

#### Acceptance Criteria

1. THE Strategy Detail screen SHALL show: version list with parameters and dates, runs per version with status/Sharpe/DD, studies per version with type/status/count, prop evaluations per version with pass rate, and a Research Progress checklist with Confidence Level.
2. THE version list SHALL support creating new versions.
3. THE runs list SHALL support launching new backtests.
4. THE studies list SHALL support launching new studies via a dropdown.

---

### Requirement 17 — Study Detail Wireframes (Gap B5)

**User Story:** As a user, I want visually rich study detail screens with KPI tiles, charts, and per-window/per-path breakdowns, so that I can make informed research decisions.

#### Acceptance Criteria

1. THE Monte Carlo Study Detail SHALL show KPI tiles for: Verdict, Pass Rate, Ruin Probability, PBO, and DSR.
2. THE Walk-Forward Study Detail SHALL show KPI tiles for: Avg OOS Sharpe, IS/OOS Ratio, and Parameter Drift.
3. THE Sensitivity Study Detail SHALL show: Stability Score, Fragility Index, and parameter island status.
4. ALL study detail screens SHALL include an [Export CSV] action.

---

### Requirement 18 — Research Lifecycle & Development Stage (Gap C5)

**User Story:** As a user, I want a structured research lifecycle with development stages, hypothesis tracking, and an automated research checklist, so that I follow a disciplined validation process and don't trade prematurely.

#### Acceptance Criteria

1. `StrategyIdentity` SHALL include a `DevelopmentStage` field (default: `Exploring`) with values: `Hypothesis`, `Exploring`, `Optimizing`, `Validating`, `FinalTest`, `Retired`. Existing JSON files missing this field SHALL deserialize to `Exploring` — no migration script needed.
2. `DevelopmentStage` SHALL be visible on all strategy cards and the Dashboard.
3. THE Dashboard SHALL group strategies by development stage in the Research Pipeline section.
4. `StrategyIdentity` SHALL include a nullable `Hypothesis` string field, prompted at strategy creation and displayed on Strategy Detail and Run Detail screens. For strategies where `Hypothesis` is null, the Strategy Detail screen SHALL show a soft prompt: "Add a hypothesis for this strategy →".
5. THE Strategy Detail screen SHALL display an automated Research Checklist: initial backtest, Monte Carlo robustness, walk-forward validation, regime sensitivity, execution realism impact, parameter surface mapping, final held-out test, prop firm evaluation.
6. A Confidence Level score (e.g. "LOW — 2 of 8 checks passed") SHALL be computed and updated on every new study completion.

---

### Requirement 19 — Overfitting Detection & DSR (Gap C2)

**User Story:** As a user, I want automatic overfitting detection including Deflated Sharpe Ratio, trial budget tracking, and minimum backtest length checks, so that I can identify curve-fitted strategies before risking capital.

#### Acceptance Criteria

1. `BacktestResult` SHALL include `DeflatedSharpeRatio` (nullable decimal) and `TrialCount` (nullable int) fields.
2. DSR SHALL be computed automatically on every completed run and displayed as a secondary metric next to raw Sharpe on the Run Detail screen.
3. WHEN DSR is below 0.95, THEN a "Possible overfitting" warning badge SHALL be displayed.
4. `StrategyVersion` SHALL include a `TotalTrialsRun` (int) field, incremented on every run and sweep (by the number of combinations for sweeps). `RunScenarioUseCase` increments this after every completed run.
5. THE `TrialCount` field on `BacktestResult` SHALL be a snapshot of `TotalTrialsRun` at the time the run completed, so historical DSR values remain stable even as the trial count grows.
6. THE Strategy Detail screen SHALL show trial count with a contextual warning when random-chance Sharpe exceeds observed Sharpe.
6. A `MinimumBarsRequired` function SHALL compute the minimum bar count for 95% confidence given observed Sharpe, trial count, skewness, and kurtosis.
7. WHEN actual bar count is below MinBTL, THEN a warning SHALL be surfaced: "This backtest is too short to be statistically significant."
8. THE Sensitivity Study Detail SHALL show a 2D parameter performance surface heatmap with fragility index (area where Sharpe remains within 20% of optimal).

---

### Requirement 20 — Advanced Validation Study Types (Gap C1)

**User Story:** As a user, I want anchored walk-forward, a sealed held-out test set, and a scaffolded CPCV study type, so that I can rigorously validate my strategies against overfitting using industry-standard methods.

#### Acceptance Criteria

1. THE `StudyType` enum SHALL include `AnchoredWalkForward` and `CombinatorialPurgedCV` in addition to existing types.
2. A `WalkForwardMode` enum SHALL be defined with `Rolling` and `Anchored` values.
3. THE walk-forward study configuration SHALL include a `WalkForwardMode` field; Rolling is the default.
4. Anchored walk-forward SHALL keep the training start fixed and expand the window on each step.
5. CPCV is deferred to V4.1. The `StudyType.CombinatorialPurgedCV` enum entry SHALL exist, the Study Detail wireframe SHALL have a PBO tile, and a placeholder handler SHALL return a clear "CPCV is scheduled for V4.1" message. PBO below 5% = robust; PBO above 25% = strong overfitting signal (for when implemented).
6. THE Strategy Builder SHALL prompt the user to designate the last N% of the date range as a sealed test set (recommended: 20–30%, minimum 1 year).
7. THE sealed test set SHALL be locked — unavailable for walk-forward, sensitivity, or parameter sweeps. Enforcement SHALL be at the Application layer: before dispatching any study, the orchestrator checks for sealed test set overlap and throws `SealedTestSetViolationException` if violated.
8. Core SHALL include a `DateRangeConstraint` value object (start, end, IsSealed) that the engine respects by treating bars outside the allowed range as non-existent.
9. A one-time "Final Validation" run SHALL be available as a distinct action with appropriate warnings. The `FinalValidationUseCase` bypasses the sealed-set guard with an explicit `allowSealedSet: true` flag.
10. AFTER a Final Validation run, the strategy SHALL be marked as `DevelopmentStage.FinalTest`. Further parameter changes SHALL trigger a warning: "You have already run a Final Validation. Changing parameters invalidates it."

---

### Requirement 21 — Analytics Metrics Expansion (Gap C6)

**User Story:** As a user, I want expanded analytics including MAE/MFE analysis, run comparison, and additional metrics, so that I can make better trade management and strategy iteration decisions.

#### Acceptance Criteria

1. THE Run Detail screen SHALL show these additional metrics: Deflated Sharpe Ratio (primary KPI tile), Profit Factor, Expectancy ($/trade), Recovery Factor, Calmar Ratio, Average Holding Period, and Consecutive Loss Max.
2. THE Trades tab SHALL show an MAE vs MFE scatter chart colour-coded by win/loss.
3. THE Trades tab SHALL show MAE and MFE distribution histograms.
4. WHEN comparing two versions of the same strategy, THE system SHALL show a delta view highlighting: parameter changes, performance changes (with direction arrows), and contextual warnings (e.g. "Fewer trades reduces statistical confidence").

---

## Requirements Index

| ID | Title | Requirement # |
|----|-------|---------------|
| REQ-V4-01 | Product Scope Declaration | 1 |
| REQ-V4-02 | Error Handling & Failed Run UX | 2 |
| REQ-V4-03 | Data Files Screen | 3 |
| REQ-V4-04 | V2 Data Migration | 4 |
| REQ-V4-05 | Real-Time Execution Progress | 5 |
| REQ-V4-06 | Export & Report Formats | 6 |
| REQ-V4-07 | Study Cancellation & Partial Results | 7 |
| REQ-V4-08 | Custom Firm Rule Pack Editor | 8 |
| REQ-V4-09 | Background Execution & Notifications | 9 |
| REQ-V4-10 | Dashboard, Settings & REQ Index | 10 |
| REQ-V4-11 | Responsive Behaviour | 11 |
| REQ-V4-12 | Keyboard Shortcuts & Accessibility | 12 |
| REQ-V4-13 | Research Explorer Screen | 13 |
| REQ-V4-14 | Study Detail Screens | 14 |
| REQ-V4-15 | Persistence Layer | 15 |
| REQ-V4-16 | Strategy Detail Screen | 16 |
| REQ-V4-17 | Study Detail Wireframes | 17 |
| REQ-V4-18 | Research Lifecycle & Development Stage | 18 |
| REQ-V4-19 | Overfitting Detection & DSR | 19 |
| REQ-V4-20 | Advanced Validation Study Types | 20 |
| REQ-V4-21 | Analytics Metrics Expansion | 21 |
