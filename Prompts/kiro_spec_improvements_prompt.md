# Kiro Spec Improvement Prompt — TradingResearchEngine V3

## Context

You are working on the V3 specification for **TradingResearchEngine**, a Blazor Server backtesting and research platform. The existing `requirements.md` defines a solid domain model, UX architecture, wireframes, and implementation roadmap covering five phases.

A review of the spec identified **10 gaps** that are either missing entirely or underspecified. Your task is to produce additions and amendments to `requirements.md` that fill every gap. Do not rewrite sections that are already correct — append or amend only what is missing.

---

## Gap 1 — Authentication & Multi-User Scope

**Problem:** The spec never states whether this is a single-user local tool or a multi-user application. This changes persistence, routing, identity namespacing (`StrategyId` uniqueness), and whether a `UserId` is needed throughout the model.

**Task:** Add a new section **§0. Product Scope & Deployment Model** before §1, specifying:

1. Explicitly declare the deployment target: single-user local desktop app (no login required) OR multi-user hosted app (login required).
2. If single-user: state that `StrategyId` is unique within the local data directory, no authentication layer is needed, and all repositories read/write from a configured local path.
3. If multi-user: amend `StrategyIdentity`, `StrategyVersion`, `StudyRecord`, and all repository interfaces to include a `UserId` scope parameter. Add a `REQ-V3-06: Authentication & User Isolation` requirement with acceptance criteria covering login, session, and data isolation.
4. Regardless of choice: document the minimum supported runtime and browser (e.g. .NET 9, Blazor Server on localhost, Chrome/Edge).

---

## Gap 2 — Error Handling & Failed Run UX

**Problem:** The spec describes successful backtest flows in detail but has no specification for failure states — what happens when a backtest crashes mid-execution, a data file is malformed, or a study times out.

**Task:** Add a new subsection **§8.x — Error & Failure States** inside §8 (Backtest and Analytics UX):

1. Define the three failure categories:
   - **Configuration error** — invalid `ScenarioConfig` caught before execution starts (e.g. missing data file, invalid date range). Show an inline validation error on the Run Detail screen. Do not create a `BacktestResult` record.
   - **Runtime error** — unhandled exception thrown during backtest execution (e.g. data parsing failure, strategy exception). Create a `BacktestResult` with `Status = Failed` and store the exception message and stack trace in a `FailureDetail` field.
   - **Timeout** — study execution exceeds a configurable time limit (default: 5 minutes for studies, 60 seconds for single runs). Treat as runtime error.
2. Specify the Run Detail screen failure state wireframe:
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
3. Specify a `RunStatus` enum in the domain model: `Pending`, `Running`, `Completed`, `Failed`, `Cancelled`.
4. Add `REQ-V3-07: Run Failure Visibility` with acceptance criteria: (a) failed runs appear in the Strategy's run list with a ❌ badge, (b) the full error message is accessible but collapsed by default, (c) users can re-launch from the failure screen.

---

## Gap 3 — Data Files Screen

**Problem:** "Data Files" appears in the primary navigation wireframe but has no wireframe, no acceptance criteria, and no functional spec. It is a critical dependency for the Strategy Builder (Step 2 references it).

**Task:** Add a new section **§5.x — Data Files Screen** (insert after the Strategy Detail screen description in §5):

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
2. **Add File flow:** User provides a file path (local) or uploads via drag-and-drop. On add, the system reads the first and last row to auto-detect: symbol hint, timeframe hint, date range, total bar count, and column format (OHLCV order). Show a preview of the first 10 rows.
3. **Validation rules:** (a) File must have at least 4 columns (Date, O, H, L, C), (b) dates must be monotonically increasing, (c) no more than 5% missing bars for the detected timeframe, (d) O/H/L/C values must be positive decimals. Validation failures show per-rule details.
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
5. **Add `REQ-V3-08: Data File Management`** with acceptance criteria: (a) users can add a CSV file by path or drag-and-drop, (b) the system auto-detects metadata on add, (c) the Strategy Builder Step 2 data source picker shows only validated files, (d) deleting a file used by an existing run shows a warning but does not delete the run record.

---

## Gap 4 — Persistence Migration for Existing V2/V2.1 Data

**Problem:** Phase 1 adds new repositories and JSON files but there is no migration plan for existing `BacktestResult` records that were persisted under V2/V2.1. These records have no `StrategyVersionId`, no `StudyId`, and no `RunStatus`.

**Task:** Add a new subsection **§11.x — Migration Strategy** inside §11 (Implementation Roadmap):

1. Define migration scope: all existing `BacktestResult` JSON files in the configured results directory.
2. Specify a **non-destructive migration**: on first startup after V3 upgrade, the system creates a synthetic `StrategyIdentity` called `"Imported (Pre-V3)"` with a single `StrategyVersion` called `"v0 (imported)"`. All existing `BacktestResult` records are linked to this version by writing their `StrategyVersionId` field. Original JSON files are not deleted.
3. Specify that migrated runs appear in the Strategy Library under an `"Imported"` group, visually distinct (e.g. greyed out, no version history available).
4. Specify a `MigrationService` in Infrastructure that runs once on startup, checks for a `migration_v3.lock` file, and skips if already run.
5. Add `REQ-V3-09: V2 Data Migration` with acceptance criteria: (a) existing results are visible after upgrade with no data loss, (b) migrated results are clearly labelled as pre-V3, (c) migration runs at most once, (d) migration failure is logged and does not crash the app.

---

## Gap 5 — Real-Time Backtest Progress

**Problem:** The spec describes launching a backtest but never addresses what the UI shows during execution. For Monte Carlo studies with 1000 paths or walk-forward studies with many windows, this is a significant UX gap.

**Task:** Add a new subsection **§8.x — Real-Time Execution Progress** inside §8:

1. **Single-run progress:** Show an indeterminate spinner with "Running… (142 bars processed)" updated via Blazor SignalR or a polling interval. Elapsed time displayed. A **[Cancel]** button is available during execution.
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
4. Specify `IProgressReporter` interface in Application with `Report(int current, int total, string label)` method. The `RunScenarioUseCase` and study workflows accept an optional `IProgressReporter`.
5. Blazor implementation uses `InvokeAsync(StateHasChanged)` to push progress updates to the UI component on each `Report()` call.
6. On cancellation, set `RunStatus = Cancelled` and persist partial results where meaningful (e.g. completed walk-forward windows).
7. Add `REQ-V3-10: Execution Progress & Cancellation` with acceptance criteria: (a) single runs show bar-count progress, (b) studies show percent-complete, (c) any running execution can be cancelled, (d) cancelled runs are stored with `Status = Cancelled`.

---

## Gap 6 — Export & Reporting Spec

**Problem:** The Run Detail wireframe shows `[Export Markdown]` but the spec never defines what is exported, to what formats, or what the Markdown report structure contains.

**Task:** Add a new section **§8.x — Export & Report Formats**:

1. **Markdown Run Report** — exported as a single `.md` file. Structure:
   ```markdown
   # Run Report: {StrategyName} v{VersionNumber}
   **Date:** {RunDate}  **Status:** Completed  **Duration:** 1.2s

   ## Configuration
   | Field | Value |
   |-------|-------|
   | Symbol | EURUSD |
   | Timeframe | Daily |
   | Date Range | 2020-01-01 – 2024-12-31 |
   | Initial Cash | $100,000 |

   ## Key Metrics
   | Metric | Value |
   |--------|-------|
   | Sharpe Ratio | 1.42 |
   | Max Drawdown | 8.3% |
   | Win Rate | 61% |
   | Total Trades | 23 |

   ## Robustness Notes
   (Auto-populated from warning badges and robustness verdict if studies exist)

   ## Trade Log
   (Full trade table: entry date, exit date, direction, size, P&L, MFE, MAE)
   ```
2. **CSV Trade Log** — exported as `{StrategyName}_v{N}_{RunDate}_trades.csv`. Columns: `EntryDate, ExitDate, Direction, Symbol, Quantity, EntryPrice, ExitPrice, GrossPnL, NetPnL, MAE, MFE, HoldingPeriodBars`.
3. **JSON Raw Result** — the full `BacktestResult` object serialized as `{RunId}.json`. Intended for programmatic use and re-import.
4. **Study Export** — Monte Carlo exports a CSV of all path summary statistics (P10/P50/P90 end equity, ruin flag per path). Walk-forward exports per-window OOS metrics as CSV.
5. Specify an `IReportExporter` interface in Application with methods `ExportMarkdown(BacktestResult)`, `ExportTradeCsv(BacktestResult)`, `ExportJson(BacktestResult)`.
6. Add `REQ-V3-11: Export & Reports` with acceptance criteria: (a) any completed run can be exported to Markdown and CSV from the Run Detail screen, (b) exported Markdown renders correctly in GitHub and Obsidian, (c) trade CSV opens cleanly in Excel, (d) JSON export round-trips through `IResultRepository` without data loss.

---

## Gap 7 — Study Cancellation & Partial Results

**Problem:** Walk-forward and Monte Carlo studies are long-running. The spec does not address whether they can be cancelled mid-run or whether partial results are stored and surfaced.

**Task:** Amend §8 (Backtest and Analytics UX) and §11 (Roadmap) as follows:

1. Specify that cancellation is supported for all study types via the `[Cancel Study]` button introduced in Gap 5.
2. Specify partial result persistence rules:
   - **Monte Carlo:** Store all completed paths. On cancellation, show results for the completed paths with a "⚠️ Partial: 347 of 1000 paths completed" banner. Verdicts (Robust/Marginal/Fragile) are suppressed until at least 200 paths are complete.
   - **Walk-Forward:** Store completed windows. Show per-window results for completed windows. Composite OOS equity curve is drawn from completed windows only.
   - **Parameter Sweep:** Store completed combinations. Show the heatmap with completed cells filled and incomplete cells greyed out.
3. Add a `IsPartial` boolean and `CompletedCount` / `TotalCount` fields to `StudyRecord`.
4. Specify that partial studies are visually distinct in the Research Explorer list (amber badge: "Partial — 34%").

---

## Gap 8 — Custom Firm Rule Pack UI

**Problem:** §9 states users can create custom rule packs but there is no wireframe or acceptance criteria for this editor. `ChallengePhase` has enough fields to require a non-trivial form.

**Task:** Add a new subsection **§9.x — Custom Rule Pack Editor** inside §9 (Prop Firm Evaluation UX):

1. **Wireframe:**
   ```
   ┌─────────────────────────────────────────────────────┐
   │ New Rule Pack                                        │
   ├─────────────────────────────────────────────────────┤
   │ Firm Name:      [________________]                  │
   │ Challenge Name: [________________]  (e.g. "100k Eval")│
   │ Account Size:   [$_____________]                    │
   │ Payout Split:   [___] %                             │
   ├─────────────────────────────────────────────────────┤
   │ Phases                              [+ Add Phase]   │
   │ ┌─────────────────────────────────────────────────┐ │
   │ │ Phase 1                             [✕ Remove]  │ │
   │ │ Phase Name:          [Phase 1       ]           │ │
   │ │ Profit Target:       [____] %                   │ │
   │ │ Max Daily DD:        [____] %                   │ │
   │ │ Max Total DD:        [____] %                   │ │
   │ │ Min Trading Days:    [____]                     │ │
   │ │ Max Trading Days:    [____] (leave blank = none)│ │
   │ │ Consistency Rule:    [____] % (blank = no rule) │ │
   │ │ Trailing Drawdown:   [ ] Yes / [●] No           │ │
   │ └─────────────────────────────────────────────────┘ │
   │ ┌─────────────────────────────────────────────────┐ │
   │ │ Phase 2                             [✕ Remove]  │ │
   │ │ ...                                             │ │
   │ └─────────────────────────────────────────────────┘ │
   ├─────────────────────────────────────────────────────┤
   │ Notes: [__________________________________________] │
   │                                                     │
   │            [Cancel]  [Save Rule Pack]               │
   └─────────────────────────────────────────────────────┘
   ```
2. Validation: (a) Firm Name and Challenge Name are required, (b) at least one phase required, (c) Profit Target and Max DD fields are required per phase, (d) Consistency Rule %, if entered, must be between 1–100, (e) duplicate phase names within a pack are not allowed.
3. Custom packs are stored as JSON alongside built-in packs but with `"IsBuiltIn": false`. They are listed in the Prop Firm Lab firm selector with a "(Custom)" suffix.
4. Built-in packs are read-only — the editor opens in view-only mode for them, with a "Duplicate & Edit" button.
5. Add `REQ-V3-12: Custom Rule Pack Editor` with acceptance criteria matching the above validation rules.

---

## Gap 9 — Notifications & Background Execution

**Problem:** Studies may be launched and take several minutes. There is no spec for how users are notified when background work completes, and no guidance on whether studies block the UI or run in the background.

**Task:** Add a new subsection **§5.x — Background Execution & Notifications**:

1. Specify that all study executions are **non-blocking**: the user can navigate away from the progress screen and the study continues running.
2. Specify a persistent **Execution Status Bar** at the bottom of the app shell (always visible, 32px tall):
   ```
   ┌─────────────────────────────────────────────────────┐
   │ 🔄 Monte Carlo (EURUSD MR v3): 34% · [View] [Stop] │
   └─────────────────────────────────────────────────────┘
   ```
   - Hidden when no execution is running.
   - Shows the most recent active execution. If multiple are running (future), shows count: "2 studies running".
3. On completion, the status bar transitions to a success or failure notification:
   ```
   ✅ Monte Carlo (EURUSD MR v3): Complete — 1000 paths · [View Results] [Dismiss]
   ❌ Walk-Forward (BTC H4 v1): Failed — timeout · [View Error] [Dismiss]
   ```
   Notification persists until dismissed or until a new execution starts.
4. Specify that single-run backtests (< 60s expected) are **inline** (user stays on the Run Detail screen and sees the spinner). Studies are always background.
5. Add `REQ-V3-13: Background Execution & Notification` with acceptance criteria: (a) users can navigate while a study runs, (b) the status bar is visible on all screens during execution, (c) a completion notification is shown on finish, (d) dismissed notifications are still accessible via the Strategy's study list.

---

## Gap 10 — Missing REQ Numbers, Dashboard Wireframe & Settings Screen

**Problem:** (a) REQ numbering stops at REQ-V3-05 while the task groups imply more. (b) The Dashboard is the primary screen but has no layout wireframe. (c) The Settings screen is in the nav but completely unspecified.

**Task A — REQ numbering:** Renumber the requirements defined in Gaps 1–9 above as REQ-V3-06 through REQ-V3-13 (already assigned in each gap above). Add a **Requirements Index** table to §10:

| ID | Title | Section |
|----|-------|---------|
| REQ-V3-01 | Strategy Identity Model | §10 |
| REQ-V3-02 | Study as First-Class Concept | §10 |
| REQ-V3-03 | Guided Strategy Builder | §10 |
| REQ-V3-04 | Prop Firm Rule Packs with Challenge Phases | §10 |
| REQ-V3-05 | Robustness Verdict | §10 |
| REQ-V3-06 | Authentication & User Isolation (or: Single-User Scope) | §0 |
| REQ-V3-07 | Run Failure Visibility | §8 |
| REQ-V3-08 | Data File Management | §5 |
| REQ-V3-09 | V2 Data Migration | §11 |
| REQ-V3-10 | Execution Progress & Cancellation | §8 |
| REQ-V3-11 | Export & Reports | §8 |
| REQ-V3-12 | Custom Rule Pack Editor | §9 |
| REQ-V3-13 | Background Execution & Notification | §5 |

**Task B — Dashboard wireframe:** Add a wireframe to §6 for the Dashboard screen:

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

**Task C — Settings screen:** Add a new subsection **§5.x — Settings Screen**:

```
┌─────────────────────────────────────────────────────┐
│ Settings                                            │
├─────────────────────────────────────────────────────┤
│ Data                                                │
│   Results Directory:  [/data/results        ] [📁]  │
│   Data Files Dir:     [/data/csv            ] [📁]  │
│   Rule Packs Dir:     [/data/rulepacks      ] [📁]  │
├─────────────────────────────────────────────────────┤
│ Execution Defaults                                  │
│   Default Realism Profile: [Standard       ▼]       │
│   Study Timeout (minutes): [5              ]        │
│   Single-Run Timeout (s):  [60             ]        │
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

Specify that all Settings values are stored in `appsettings.json` and exposed via a strongly-typed `AppSettings` record injected via `IOptions<AppSettings>`. Changes in the UI write back to `appsettings.json` at runtime using `IWritableOptions<AppSettings>`.

---

## Acceptance Checklist for Kiro

After completing all tasks above, verify:

- [ ] §0 exists and explicitly declares single-user or multi-user deployment model
- [ ] `RunStatus` enum is defined with: `Pending`, `Running`, `Completed`, `Failed`, `Cancelled`
- [ ] `FailureDetail` field exists on `BacktestResult` (nullable string)
- [ ] `DataFileRecord` and `ValidationStatus` enum are defined in Application
- [ ] `IProgressReporter` interface is defined in Application
- [ ] `IsPartial`, `CompletedCount`, `TotalCount` fields exist on `StudyRecord`
- [ ] `IReportExporter` interface is defined in Application with `ExportMarkdown`, `ExportTradeCsv`, `ExportJson`
- [ ] `MigrationService` is listed in Phase 1 of the roadmap
- [ ] Custom rule pack JSON includes `"IsBuiltIn": false` flag
- [ ] `AppSettings` record is defined in Infrastructure with all configurable paths and timeouts
- [ ] Requirements Index table REQ-V3-01 through REQ-V3-13 exists in §10
- [ ] Dashboard wireframe exists in §6
- [ ] Settings screen wireframe and spec exists in §5
- [ ] Data Files screen wireframe and `DataFileRecord` spec exists in §5
- [ ] All 10 gaps have a corresponding numbered REQ entry
