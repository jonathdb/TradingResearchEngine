# Kiro Spec Improvement Prompt — TradingResearchEngine V3
## Complete Amendment Set (v2 — All Gaps & Review Findings)

You are working on the V3 specification for **TradingResearchEngine**, a Blazor Server backtesting
and research platform. The existing `requirements.md` defines a solid domain model, UX architecture,
wireframes, and implementation roadmap covering five phases.

A two-pass review identified **15 gaps** across two categories:
- **Major gaps (10):** Missing specifications that would force Kiro to make unguided architectural decisions.
- **Minor gaps (5):** Missing details that affect implementation quality but are lower risk.

Your task is to produce additions and amendments to `requirements.md` that fill every gap listed
below. **Do not rewrite sections that are already correct — append or amend only what is missing.**

---

# PART A — Major Gaps (Architectural / Missing Specs)

---

## Gap A1 — Authentication & Multi-User Scope

**Problem:** The spec never states whether this is a single-user local tool or a multi-user
application. This changes persistence, routing, identity namespacing (`StrategyId` uniqueness),
and whether a `UserId` is needed throughout the model.

**Task:** Add a new section **§0. Product Scope & Deployment Model** before §1, specifying:

1. Explicitly declare the deployment target: single-user local desktop app (no login required)
   OR multi-user hosted app (login required).
2. If **single-user**: state that `StrategyId` is unique within the local data directory, no
   authentication layer is needed, and all repositories read/write from a configured local path.
3. If **multi-user**: amend `StrategyIdentity`, `StrategyVersion`, `StudyRecord`, and all
   repository interfaces to include a `UserId` scope parameter. Add
   **REQ-V3-06: Authentication & User Isolation** with acceptance criteria covering login,
   session, and data isolation.
4. Regardless of choice: document the minimum supported runtime and browser
   (e.g. .NET 9, Blazor Server on localhost, Chrome/Edge latest).

---

## Gap A2 — Error Handling & Failed Run UX

**Problem:** The spec describes successful backtest flows in detail but has no specification for
failure states — what happens when a backtest crashes mid-execution, a data file is malformed,
or a study times out.

**Task:** Add a new subsection **§8.x — Error & Failure States** inside §8:

1. Define the three failure categories:
   - **Configuration error** — invalid `ScenarioConfig` caught before execution starts
     (e.g. missing data file, invalid date range). Show an inline validation error on the
     Run Detail screen. Do not create a `BacktestResult` record.
   - **Runtime error** — unhandled exception thrown during execution (e.g. data parsing
     failure, strategy exception). Create a `BacktestResult` with `Status = Failed` and
     store the exception message and stack trace in a `FailureDetail` field.
   - **Timeout** — execution exceeds a configurable time limit (default: 5 minutes for
     studies, 60 seconds for single runs). Treat as runtime error.

2. Specify the Run Detail failure state wireframe:
   ```
   ┌─────────────────────────────────────────────────────┐
   │ Run: EURUSD Mean Reversion v3 — 2024-01-15 14:32   │
   │ Status: ❌ Failed · 0 bars · 0 trades · 1.4s        │
   ├─────────────────────────────────────────────────────┤
   │ ⚠️ Run failed: NullReferenceException in            │
   │    MeanReversionStrategy.OnBar() at bar 142         │
   │    [View Full Error] [Copy to Clipboard]            │
   ├─────────────────────────────────────────────────────┤
   │ [Re-run with same config]  [Edit Config & Re-run]  │
   └─────────────────────────────────────────────────────┘
   ```

3. Specify a `RunStatus` enum in the domain model:
   `Pending`, `Running`, `Completed`, `Failed`, `Cancelled`.

4. Add **REQ-V3-07: Run Failure Visibility** with acceptance criteria:
   (a) failed runs appear in the Strategy run list with a ❌ badge,
   (b) full error message is accessible but collapsed by default,
   (c) users can re-launch from the failure screen.

---

## Gap A3 — Data Files Screen

**Problem:** "Data Files" appears in the primary navigation wireframe but has no wireframe,
no acceptance criteria, and no functional spec. It is a critical dependency for the Strategy
Builder Step 2, which references it.

**Task:** Add a new subsection **§5.x — Data Files Screen** (insert after the Strategy Detail
screen description in §5):

1. **Wireframe:**
   ```
   ┌─────────────────────────────────────────────────────┐
   │ Data Files                          [+ Add File]    │
   ├─────────────────────────────────────────────────────┤
   │ Search: [____________]  Format: [All ▼]             │
   ├─────────────────────────────────────────────────────┤
   │ ┌─────────────────────────────────────────────────┐ │
   │ │ 📄 EURUSD_Daily_2015_2024.csv                   │ │
   │ │    1 MB · Daily · 2,515 bars · EURUSD           │ │
   │ │    Validated ✅ · Last used: 2h ago             │ │
   │ │    [Preview] [Validate] [Delete]                │ │
   │ └─────────────────────────────────────────────────┘ │
   │ ┌─────────────────────────────────────────────────┐ │
   │ │ 📄 BTCUSD_H4_2020_2024.csv                     │ │
   │ │    400 KB · H4 · 8,760 bars · BTCUSD           │ │
   │ │    ⚠️ Not yet validated                         │ │
   │ │    [Preview] [Validate] [Delete]                │ │
   │ └─────────────────────────────────────────────────┘ │
   └─────────────────────────────────────────────────────┘
   ```

2. **Add File flow:** User provides a file path (local) or uploads via drag-and-drop. On add,
   the system reads the first and last row to auto-detect: symbol hint, timeframe hint, date
   range, total bar count, and column format (OHLCV order). Show a preview of the first 10 rows.

3. **Validation rules:** (a) File must have at least 4 columns (Date, O, H, L, C),
   (b) dates must be monotonically increasing, (c) no more than 5% missing bars for the
   detected timeframe, (d) O/H/L/C values must be positive decimals. Validation failures
   show per-rule details.

4. **Domain model:** Add `DataFileRecord` to Application:
   ```csharp
   public sealed record DataFileRecord(
       string FileId,
       string FileName,
       string FilePath,
       string? DetectedSymbol,
       string? DetectedTimeframe,
       DateTimeOffset? FirstBar,
       DateTimeOffset? LastBar,
       int BarCount,
       ValidationStatus ValidationStatus,
       string? ValidationError,
       DateTimeOffset AddedAt);

   public enum ValidationStatus { Pending, Valid, Invalid }
   ```

5. Add **REQ-V3-08: Data File Management** with acceptance criteria:
   (a) users can add a CSV file by path or drag-and-drop,
   (b) the system auto-detects metadata on add,
   (c) the Strategy Builder Step 2 data source picker shows only validated files,
   (d) deleting a file used by an existing run shows a warning but does not delete the run record.

---

## Gap A4 — Persistence Migration for Existing V2/V2.1 Data

**Problem:** Phase 1 adds new repositories and JSON files but there is no migration plan for
existing `BacktestResult` records persisted under V2/V2.1. These records have no
`StrategyVersionId`, no `StudyId`, and no `RunStatus`.

**Task:** Add a new subsection **§11.x — Migration Strategy** inside §11:

1. Migration scope: all existing `BacktestResult` JSON files in the configured results directory.

2. **Non-destructive migration:** On first startup after V3 upgrade, the system creates a
   synthetic `StrategyIdentity` called `"Imported (Pre-V3)"` with a single `StrategyVersion`
   called `"v0 (imported)"`. All existing `BacktestResult` records are linked to this version
   by writing their `StrategyVersionId` field. Original JSON files are not deleted.

3. Migrated runs appear in the Strategy Library under an `"Imported"` group, visually distinct
   (greyed out, no version history available).

4. A `MigrationService` in Infrastructure runs once on startup, checks for a
   `migration_v3.lock` file, and skips if already run.

5. Add **REQ-V3-09: V2 Data Migration** with acceptance criteria:
   (a) existing results are visible after upgrade with no data loss,
   (b) migrated results are labelled as pre-V3,
   (c) migration runs at most once,
   (d) migration failure is logged and does not crash the app.

---

## Gap A5 — Real-Time Backtest Progress

**Problem:** The spec describes launching a backtest but never addresses what the UI shows
during execution. For Monte Carlo studies with 1000 paths, this is a significant UX gap.

**Task:** Add a new subsection **§8.x — Real-Time Execution Progress** inside §8:

1. **Single-run progress:** Show an indeterminate spinner with "Running… (142 bars processed)"
   updated via Blazor SignalR or polling. Elapsed time displayed. A **[Cancel]** button is
   available during execution.

2. **Study progress:** Show a determinate progress bar:
   - Monte Carlo: "Simulating path 347 of 1000 (34%)"
   - Walk-Forward: "Window 3 of 8 (37%)"
   - Parameter Sweep: "Combination 28 of 120 (23%)"

3. **Progress wireframe:**
   ```
   ┌─────────────────────────────────────────────────────┐
   │ Monte Carlo Study — EURUSD Mean Reversion v3        │
   │ Status: 🔄 Running                                  │
   ├─────────────────────────────────────────────────────┤
   │ ████████████░░░░░░░░░░░░░░░  34%                   │
   │ Simulating path 347 of 1000 · Elapsed: 00:42       │
   │                                                     │
   │ [Cancel Study]                                      │
   └─────────────────────────────────────────────────────┘
   ```

4. Specify `IProgressReporter` interface in Application:
   ```csharp
   public interface IProgressReporter
   {
       void Report(int current, int total, string label);
   }
   ```
   `RunScenarioUseCase` and all study workflows accept an optional `IProgressReporter`.
   Blazor implementation uses `InvokeAsync(StateHasChanged)` to push updates on each call.

5. On cancellation: set `RunStatus = Cancelled` and persist partial results where meaningful
   (e.g. completed walk-forward windows).

6. Add **REQ-V3-10: Execution Progress & Cancellation** with acceptance criteria:
   (a) single runs show bar-count progress,
   (b) studies show percent-complete,
   (c) any running execution can be cancelled,
   (d) cancelled runs are stored with `Status = Cancelled`.

---

## Gap A6 — Export & Reporting Spec

**Problem:** The Run Detail wireframe shows `[Export Markdown]` but the spec never defines
what is exported, to what formats, or what the Markdown report structure contains.

**Task:** Add a new section **§8.x — Export & Report Formats**:

1. **Markdown Run Report** — exported as a single `.md` file. Structure:
   ```markdown
   # Run Report: {StrategyName} v{VersionNumber}
   **Date:** {RunDate}  **Status:** Completed  **Duration:** 1.2s

   ## Configuration
   | Field       | Value              |
   |-------------|-------------------|
   | Symbol      | EURUSD            |
   | Timeframe   | Daily             |
   | Date Range  | 2020-01-01–2024-12-31 |
   | Initial Cash| $100,000          |

   ## Key Metrics
   | Metric      | Value  |
   |-------------|--------|
   | Sharpe Ratio| 1.42   |
   | Max Drawdown| 8.3%   |
   | Win Rate    | 61%    |
   | Total Trades| 23     |

   ## Robustness Notes
   (Auto-populated from warning badges and robustness verdict if studies exist)

   ## Trade Log
   (Full trade table: entry date, exit date, direction, size, P&L, MFE, MAE)
   ```

2. **CSV Trade Log** — `{StrategyName}_v{N}_{RunDate}_trades.csv`. Columns:
   `EntryDate, ExitDate, Direction, Symbol, Quantity, EntryPrice, ExitPrice,
   GrossPnL, NetPnL, MAE, MFE, HoldingPeriodBars`.

3. **JSON Raw Result** — the full `BacktestResult` serialized as `{RunId}.json`.
   Intended for programmatic use and re-import.

4. **Study Export** — Monte Carlo exports a CSV of path summary statistics
   (P10/P50/P90 end equity, ruin flag per path). Walk-forward exports per-window
   OOS metrics as CSV.

5. Specify `IReportExporter` in Application:
   ```csharp
   public interface IReportExporter
   {
       Task<string> ExportMarkdownAsync(BacktestResult result);
       Task<string> ExportTradeCsvAsync(BacktestResult result);
       Task<string> ExportJsonAsync(BacktestResult result);
   }
   ```

6. Add **REQ-V3-11: Export & Reports** with acceptance criteria:
   (a) any completed run can be exported to Markdown and CSV from the Run Detail screen,
   (b) exported Markdown renders correctly in GitHub and Obsidian,
   (c) trade CSV opens cleanly in Excel,
   (d) JSON export round-trips through `IResultRepository` without data loss.

---

## Gap A7 — Study Cancellation & Partial Results

**Problem:** Walk-forward and Monte Carlo studies are long-running. The spec does not address
whether they can be cancelled mid-run or whether partial results are stored.

**Task:** Amend §8 (Backtest and Analytics UX) as follows:

1. Cancellation is supported for all study types via the `[Cancel Study]` button (see Gap A5).

2. Partial result persistence rules:
   - **Monte Carlo:** Store all completed paths. On cancellation, show results for completed
     paths with a "⚠️ Partial: 347 of 1000 paths completed" banner. Verdicts
     (Robust/Marginal/Fragile) are suppressed until at least 200 paths complete.
   - **Walk-Forward:** Store completed windows. Composite OOS equity curve is drawn from
     completed windows only.
   - **Parameter Sweep:** Store completed combinations. Show heatmap with completed cells
     filled and incomplete cells greyed out.

3. Add `IsPartial`, `CompletedCount`, and `TotalCount` fields to `StudyRecord`.

4. Partial studies are visually distinct in the Research Explorer list
   (amber badge: "Partial — 34%").

---

## Gap A8 — Custom Firm Rule Pack UI

**Problem:** §9 states users can create custom rule packs but there is no wireframe or
acceptance criteria for the editor. `ChallengePhase` has enough fields to require a
non-trivial form.

**Task:** Add a new subsection **§9.x — Custom Rule Pack Editor** inside §9:

1. **Wireframe:**
   ```
   ┌─────────────────────────────────────────────────────┐
   │ New Rule Pack                                        │
   ├─────────────────────────────────────────────────────┤
   │ Firm Name:      [________________]                  │
   │ Challenge Name: [________________]                  │
   │ Account Size:   [$_____________]                    │
   │ Payout Split:   [___] %                             │
   ├─────────────────────────────────────────────────────┤
   │ Phases                              [+ Add Phase]   │
   │ ┌─────────────────────────────────────────────────┐ │
   │ │ Phase 1                             [✕ Remove]  │ │
   │ │ Phase Name:          [Phase 1      ]            │ │
   │ │ Profit Target:       [____] %                   │ │
   │ │ Max Daily DD:        [____] %                   │ │
   │ │ Max Total DD:        [____] %                   │ │
   │ │ Min Trading Days:    [____]                     │ │
   │ │ Max Trading Days:    [____] (blank = no limit)  │ │
   │ │ Consistency Rule:    [____] % (blank = no rule) │ │
   │ │ Trailing Drawdown:   [ ] Yes  [●] No            │ │
   │ └─────────────────────────────────────────────────┘ │
   ├─────────────────────────────────────────────────────┤
   │ Notes: [_________________________________________]  │
   │                       [Cancel]  [Save Rule Pack]   │
   └─────────────────────────────────────────────────────┘
   ```

2. Validation: (a) Firm Name and Challenge Name are required, (b) at least one phase required,
   (c) Profit Target and Max DD are required per phase, (d) Consistency Rule % must be 1–100
   if entered, (e) duplicate phase names within a pack are not allowed.

3. Custom packs are stored as JSON with `"IsBuiltIn": false`. They appear in the firm selector
   with a "(Custom)" suffix.

4. Built-in packs are read-only — the editor opens in view-only mode with a "Duplicate & Edit"
   button.

5. Add **REQ-V3-12: Custom Rule Pack Editor** with acceptance criteria matching the validation
   rules above.

---

## Gap A9 — Notifications & Background Execution

**Problem:** Studies may take several minutes. There is no spec for how users are notified
when background work completes, and no guidance on whether studies block the UI or run
in the background.

**Task:** Add a new subsection **§5.x — Background Execution & Notifications**:

1. All study executions are **non-blocking**: the user can navigate away and the study
   continues running.

2. A persistent **Execution Status Bar** sits at the bottom of the app shell (32px, always
   visible during execution):
   ```
   ┌─────────────────────────────────────────────────────┐
   │ 🔄 Monte Carlo (EURUSD MR v3): 34% · [View] [Stop] │
   └─────────────────────────────────────────────────────┘
   ```
   Hidden when idle. If multiple studies run concurrently (future), shows: "2 studies running".

3. On completion, the status bar transitions to a notification:
   ```
   ✅ Monte Carlo (EURUSD MR v3): Complete — 1000 paths · [View Results] [Dismiss]
   ❌ Walk-Forward (BTC H4 v1): Failed — timeout · [View Error] [Dismiss]
   ```
   Persists until dismissed or until a new execution starts.

4. Single-run backtests (< 60s expected) are **inline** — the user stays on the Run Detail
   screen and sees the spinner. Studies are always background.

5. Add **REQ-V3-13: Background Execution & Notification** with acceptance criteria:
   (a) users can navigate while a study runs,
   (b) the status bar is visible on all screens during execution,
   (c) a completion notification is shown on finish,
   (d) dismissed notifications are still accessible via the Strategy's study list.

---

## Gap A10 — Missing Dashboard Wireframe, Settings Screen & REQ Index

**Problem:** (a) The Dashboard is the primary screen but has no layout wireframe.
(b) The Settings screen is in the nav but completely unspecified.
(c) REQ numbering stops at REQ-V3-05 while the task groups imply more.

### Task A — Dashboard Wireframe (add to §6):

```
┌─────────────────────────────────────────────────────┐
│ Dashboard                        Thu 9 Apr 2026     │
├─────────────────────────────────────────────────────┤
│ Quick Launch                                        │
│ [+ New Strategy]  [▶ Run Last Config]  [📊 Compare] │
├──────────────────────────┬──────────────────────────┤
│ Recent Runs              │ Active Studies           │
│ ─────────────────────    │ ─────────────────────    │
│ ✅ EURUSD MR v3   2h ago │ 🔄 MC (BTC H4): 34%     │
│    Sharpe 1.42 DD 8.3%  │    [View] [Stop]         │
│ ✅ BTC H4 v1     1d ago  │                          │
│    Sharpe 0.87 DD 15.1% │ No other studies running │
│ ❌ GBPUSD SC v2  3d ago  │                          │
│    Failed: data error   │                          │
│ [View All Runs →]        │                          │
├──────────────────────────┴──────────────────────────┤
│ Strategy Health Overview                            │
│ ┌──────────────┬─────────┬──────────┬────────────┐ │
│ │ Strategy     │ Version │ Sharpe   │ Robustness │ │
│ ├──────────────┼─────────┼──────────┼────────────┤ │
│ │ EURUSD MR    │ v3      │ 1.42     │ 🟢 Robust  │ │
│ │ BTC Breakout │ v1      │ 0.87     │ 🟡 Marginal│ │
│ │ GBPUSD SC    │ v2      │ —        │ ⬜ No runs │ │
│ └──────────────┴─────────┴──────────┴────────────┘ │
└─────────────────────────────────────────────────────┘
```

### Task B — Settings Screen (add new §5.x):

```
┌─────────────────────────────────────────────────────┐
│ Settings                                            │
├─────────────────────────────────────────────────────┤
│ Data                                                │
│   Results Directory:  [/data/results      ] [📁]    │
│   Data Files Dir:     [/data/csv          ] [📁]    │
│   Rule Packs Dir:     [/data/rulepacks    ] [📁]    │
├─────────────────────────────────────────────────────┤
│ Execution Defaults                                  │
│   Default Realism Profile: [Standard      ▼]        │
│   Study Timeout (minutes): [5             ]         │
│   Single-Run Timeout (s):  [60            ]         │
├─────────────────────────────────────────────────────┤
│ Registered Strategies                               │
│   (Read-only list of discovered IStrategy impls)   │
│   mean-reversion · sma-crossover · donchian · ...  │
├─────────────────────────────────────────────────────┤
│ About                                               │
│   Version: 3.0.0  Build: 2026-04-09                │
│   [View Changelog]  [Run Migration Check]           │
└─────────────────────────────────────────────────────┘
```

All Settings values are stored in `appsettings.json` and exposed via a strongly-typed
`AppSettings` record injected via `IOptions<AppSettings>`. Changes write back at runtime
using `IWritableOptions<AppSettings>`.

### Task C — Requirements Index (add to §10):

| ID | Title | Section |
|----|-------|---------|
| REQ-V3-01 | Strategy Identity Model | §10 |
| REQ-V3-02 | Study as First-Class Concept | §10 |
| REQ-V3-03 | Guided Strategy Builder | §10 |
| REQ-V3-04 | Prop Firm Rule Packs with Challenge Phases | §10 |
| REQ-V3-05 | Robustness Verdict | §10 |
| REQ-V3-06 | Authentication & User Isolation (or: Single-User Scope Declaration) | §0 |
| REQ-V3-07 | Run Failure Visibility | §8 |
| REQ-V3-08 | Data File Management | §5 |
| REQ-V3-09 | V2 Data Migration | §11 |
| REQ-V3-10 | Execution Progress & Cancellation | §8 |
| REQ-V3-11 | Export & Reports | §8 |
| REQ-V3-12 | Custom Rule Pack Editor | §9 |
| REQ-V3-13 | Background Execution & Notification | §5 |
| REQ-V3-14 | Mobile & Responsive Behaviour | §5 (new) |
| REQ-V3-15 | Keyboard Shortcuts & Accessibility | §8 (new) |

---

# PART B — Minor Gaps (Implementation Quality)

---

## Gap B1 — Mobile / Responsive Behaviour

**Problem:** The spec is entirely desktop-focused with no statement on minimum viewport,
responsive behaviour, or whether mobile is in or out of scope. This is a Blazor Server app
likely used on desktop, but the spec should be explicit.

**Task:** Add a new subsection **§5.x — Responsive Behaviour & Minimum Viewport**:

1. Explicitly declare the **primary supported viewport**: desktop (1024px+), with degraded
   but functional support down to 768px (tablet landscape).

2. Mobile (< 768px) is **out of scope for V3** — state this explicitly so Kiro does not
   generate mobile-specific CSS or layouts unless the requirement is later added.

3. At 768–1023px (tablet): the sidebar collapses to a top navigation bar. Charts remain
   full-width. KPI cards reflow to 2×2 grid. Tables gain horizontal scroll.

4. At 1024px+ (desktop): full sidebar visible. All wireframes assume this viewport.

5. Add **REQ-V3-14: Responsive Behaviour** with acceptance criteria:
   (a) all screens are fully functional at 1024px+,
   (b) all screens are usable (no overflow, no broken layout) at 768px,
   (c) no mobile-specific work is required for V3 sign-off.

---

## Gap B2 — Keyboard Shortcuts & Accessibility

**Problem:** Keyboard shortcuts and accessibility are never mentioned. This matters for
power users running many backtests daily, and for basic WCAG compliance.

**Task:** Add a new subsection **§8.x — Keyboard Shortcuts & Accessibility**:

1. **Global shortcuts** (document these in a `?` help overlay):
   | Shortcut | Action |
   |----------|--------|
   | `N` | New Strategy |
   | `R` | Re-run last config (from Dashboard) |
   | `?` | Show keyboard shortcut reference |
   | `Esc` | Close modal / cancel overlay |
   | `Ctrl+K` | Focus global search (if implemented in V3+) |

2. **Accessibility baseline (WCAG 2.1 AA):**
   - All interactive elements are keyboard-reachable via Tab/Shift+Tab.
   - Focus ring is always visible (never `outline: none` without a replacement style).
   - All charts include a data table alternative accessible to screen readers
     (hidden by default, revealed via a "Show data table" toggle beneath each chart).
   - All icon-only buttons carry an `aria-label`.
   - All status badges (✅ ⚠️ ❌) carry an `aria-label` in addition to the visual icon
     (e.g. `aria-label="Passed"`).
   - Colour is never the sole indicator of status — every status indicator has a text
     or icon component alongside the colour.
   - All form fields have associated `<label>` elements.

3. Add **REQ-V3-15: Keyboard Shortcuts & Accessibility** with acceptance criteria:
   (a) global shortcuts listed in the table above are functional,
   (b) all interactive elements are keyboard-navigable,
   (c) all charts have a screen-reader accessible data table alternative,
   (d) all icon-only buttons have `aria-label`,
   (e) colour is never the sole status indicator.

---

## Gap B3 — Incomplete REQ Coverage for Task Groups

**Problem:** The task groups in §10 (Robustness UX, Persistence Layer, Research Explorer)
have no formal REQ entries. This means Kiro has implementation tasks with no acceptance
criteria to validate against.

**Task:** Add the following formal requirements to §10, mapped to the task groups:

### REQ-V3-16: Research Explorer Screen
**User Story:** As a user, I want a dedicated screen to launch and review research studies
(Monte Carlo, Walk-Forward, Sensitivity, Parameter Sweep) from a strategy version context,
so that I can manage all study work without navigating through the run detail screen.

**Acceptance Criteria:**
1. The Research Explorer screen lists all studies across all strategies, grouped by
   strategy and version.
2. A "New Study" action is available from both the Research Explorer and the Run Detail screen.
3. Study list rows show: type badge, parent strategy/version, status, created date,
   partial/complete indicator.
4. Clicking a study row navigates to the Study Detail screen.

### REQ-V3-17: Study Detail Screens
**User Story:** As a user, I want study-type-specific result screens that present Monte Carlo,
Walk-Forward, and Sensitivity results in dedicated layouts.

**Acceptance Criteria:**
1. **Monte Carlo Study Detail** shows: P10/P50/P90 equity curves, ruin probability, Robustness
   Verdict card (Robust/Marginal/Fragile), path count, and a path distribution histogram.
2. **Walk-Forward Study Detail** shows: composite OOS equity curve, per-window OOS Sharpe,
   average OOS vs in-sample Sharpe comparison, worst-window drawdown, parameter drift score.
3. **Sensitivity Study Detail** shows: a heatmap of Sharpe vs two parameters, a stability
   score, and a flag if the strategy sits on a parameter island.
4. **Parameter Sweep Study Detail** shows: a sortable table of all parameter combinations
   with their key metrics, and an export-to-CSV action.

### REQ-V3-18: Persistence Layer
**User Story:** As a developer, I want clearly defined repository interfaces and JSON file
implementations for all new V3 entities, so that the persistence layer is consistent and
testable.

**Acceptance Criteria:**
1. `IStrategyRepository` defines: `SaveAsync`, `GetAsync`, `ListAsync`, `DeleteAsync`
   for `StrategyIdentity`.
2. `IStrategyVersionRepository` defines: `SaveAsync`, `GetAsync`, `ListForStrategyAsync`,
   `DeleteAsync` for `StrategyVersion`.
3. `IStudyRepository` defines: `SaveAsync`, `GetAsync`, `ListForVersionAsync`, `DeleteAsync`
   for `StudyRecord`.
4. `IDataFileRepository` defines: `SaveAsync`, `GetAsync`, `ListAsync`, `DeleteAsync`
   for `DataFileRecord`.
5. All repository implementations use the JSON file store in Infrastructure, consistent with
   the existing `IResultRepository` pattern.
6. All repository interfaces have corresponding unit tests for the JSON implementations.

---

## Gap B4 — Strategy Detail Screen Missing Wireframe

**Problem:** `Strategy Detail` is listed in the screen responsibilities table but has no
wireframe in §6, even though it is one of the most complex screens (versions, runs, studies,
evaluations, all in one view).

**Task:** Add a wireframe to §6 for the Strategy Detail screen:

```
┌─────────────────────────────────────────────────────┐
│ ← Strategy Library / EURUSD Mean Reversion          │
│                              [Rename] [⋮ More ▼]    │
├─────────────────────────────────────────────────────┤
│ Versions                                            │
│  ● v3 (current) — Fast: 12, Slow: 26 · 2024-01-10  │
│  ○ v2           — Fast: 10, Slow: 24 · 2023-11-05  │
│  ○ v1           — Fast: 8,  Slow: 20 · 2023-08-12  │
│                                   [+ New Version]   │
├─────────────────────────────────────────────────────┤
│ v3 — Runs (12)                                      │
│ ┌──────────────────────────────────────────────────┐│
│ │ ✅ 2024-01-15  Sharpe 1.42  DD 8.3%   [View]    ││
│ │ ✅ 2024-01-10  Sharpe 1.38  DD 9.1%   [View]    ││
│ │ ❌ 2024-01-08  Failed: timeout         [View]    ││
│ └──────────────────────────────────────────────────┘│
│ [Run New Backtest]                                  │
├─────────────────────────────────────────────────────┤
│ v3 — Studies (3)                                    │
│ ┌──────────────────────────────────────────────────┐│
│ │ 🟢 Monte Carlo  2024-01-16  1000 paths  [View]  ││
│ │ 🟡 Walk-Forward 2024-01-14  8 windows   [View]  ││
│ │ 🟠 MC Partial   2024-01-13  347/1000    [View]  ││
│ └──────────────────────────────────────────────────┘│
│ [Run New Study ▼]                                   │
├─────────────────────────────────────────────────────┤
│ v3 — Prop Evaluations (2)                           │
│ ┌──────────────────────────────────────────────────┐│
│ │ ⚠️ FTMO 100k Phase 1  62% pass rate   [View]    ││
│ │ ✅ MFF 200k Eval       71% pass rate   [View]    ││
│ └──────────────────────────────────────────────────┘│
│ [Evaluate Against Firm ▼]                           │
└─────────────────────────────────────────────────────┘
```

---

## Gap B5 — Study Detail Wireframes Missing

**Problem:** §6 has no wireframes for any of the four Study Detail screens (Monte Carlo,
Walk-Forward, Sensitivity, Parameter Sweep). These are referenced in §8 but not visualised.

**Task:** Add wireframes to §6 for each study type.

### Monte Carlo Study Detail
```
┌─────────────────────────────────────────────────────┐
│ ← EURUSD MR v3 / Monte Carlo Study — 2024-01-16    │
│ 1000 paths · Completed ✅                           │
├─────────────────────────────────────────────────────┤
│ ┌──────────┐  ┌──────────┐  ┌──────────────────┐  │
│ │Verdict   │  │Pass Rate │  │Ruin Probability  │  │
│ │🟢 Robust │  │  91%     │  │     2.1%         │  │
│ └──────────┘  └──────────┘  └──────────────────┘  │
├─────────────────────────────────────────────────────┤
│ 📈 P10 / P50 / P90 equity fan chart (full width)   │
├─────────────────────────────────────────────────────┤
│ Percentile Summary                                  │
│  P10 end equity: $108,200  P50: $121,400           │
│  P90 end equity: $137,600  Worst path DD: 18.3%    │
├─────────────────────────────────────────────────────┤
│ [Export CSV]  [Re-run with more paths]              │
└─────────────────────────────────────────────────────┘
```

### Walk-Forward Study Detail
```
┌─────────────────────────────────────────────────────┐
│ ← EURUSD MR v3 / Walk-Forward Study — 2024-01-14   │
│ 8 windows · Completed ✅                            │
├─────────────────────────────────────────────────────┤
│ ┌──────────┐  ┌──────────┐  ┌──────────────────┐  │
│ │Avg OOS   │  │IS/OOS    │  │Parameter Drift   │  │
│ │Sharpe    │  │Ratio     │  │     Low          │  │
│ │  0.89    │  │  63%     │  │  (stable)        │  │
│ └──────────┘  └──────────┘  └──────────────────┘  │
├─────────────────────────────────────────────────────┤
│ 📈 Composite OOS equity curve (full width)          │
├─────────────────────────────────────────────────────┤
│ Per-Window Results                                  │
│ ┌────────┬──────────┬──────────┬──────────────┐   │
│ │ Window │ OOS Start│ OOS Sharpe│ OOS Max DD  │   │
│ ├────────┼──────────┼──────────┼──────────────┤   │
│ │ W1     │ 2021-01  │ 1.12     │ 6.2%        │   │
│ │ W2     │ 2021-07  │ 0.74     │ 11.1%       │   │
│ │ ...    │ ...      │ ...      │ ...         │   │
│ └────────┴──────────┴──────────┴──────────────┘   │
│ [Export CSV]                                        │
└─────────────────────────────────────────────────────┘
```

### Sensitivity / Parameter Sweep Study Detail
```
┌─────────────────────────────────────────────────────┐
│ ← EURUSD MR v3 / Sensitivity Study — 2024-01-12    │
│ Fast period × Slow period · Completed ✅            │
├─────────────────────────────────────────────────────┤
│ Stability Score: 0.76 (High) ✅                     │
│ ✅ Strategy does not sit on a parameter island      │
├─────────────────────────────────────────────────────┤
│ 📊 Sharpe heatmap: Fast Period (x) × Slow (y)      │
│    (colour-coded: green = high Sharpe, red = low)  │
├─────────────────────────────────────────────────────┤
│ [Export Results CSV]                                │
└─────────────────────────────────────────────────────┘
```

---

# Acceptance Checklist for Kiro

After completing all tasks above, verify every item:

**Part A — Major Gaps**
- [ ] §0 exists and explicitly declares single-user or multi-user deployment model
- [ ] `RunStatus` enum defined: `Pending`, `Running`, `Completed`, `Failed`, `Cancelled`
- [ ] `FailureDetail` field (nullable string) exists on `BacktestResult`
- [ ] `DataFileRecord` and `ValidationStatus` enum defined in Application
- [ ] `IProgressReporter` interface defined in Application
- [ ] `IsPartial`, `CompletedCount`, `TotalCount` fields exist on `StudyRecord`
- [ ] `IReportExporter` interface defined in Application with `ExportMarkdownAsync`,
  `ExportTradeCsvAsync`, `ExportJsonAsync`
- [ ] `MigrationService` listed in Phase 1 of the roadmap (§11)
- [ ] Custom rule pack JSON includes `"IsBuiltIn": false` flag
- [ ] `AppSettings` record defined with all configurable paths and timeouts
- [ ] Requirements Index table REQ-V3-01 through REQ-V3-15 exists in §10
- [ ] Dashboard wireframe exists in §6
- [ ] Settings screen wireframe and spec exists in §5
- [ ] Data Files screen wireframe and `DataFileRecord` spec exists in §5
- [ ] Execution Status Bar spec exists in §5
- [ ] All 10 major gaps have a corresponding numbered REQ entry

**Part B — Minor Gaps**
- [ ] Minimum supported viewport (1024px+) is declared in §5
- [ ] Mobile is explicitly declared out of scope for V3
- [ ] Global keyboard shortcuts table exists in §8
- [ ] WCAG 2.1 AA baseline requirements are listed in §8
- [ ] All charts have a screen-reader accessible data table requirement
- [ ] REQ-V3-16 (Research Explorer), REQ-V3-17 (Study Detail Screens), and
  REQ-V3-18 (Persistence Layer) exist in §10 with acceptance criteria
- [ ] Strategy Detail screen wireframe exists in §6
- [ ] Monte Carlo Study Detail wireframe exists in §6
- [ ] Walk-Forward Study Detail wireframe exists in §6
- [ ] Sensitivity/Sweep Study Detail wireframe exists in §6
