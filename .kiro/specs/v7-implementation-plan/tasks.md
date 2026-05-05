# Implementation Plan: V7 TradingResearchEngine Improvements

## Overview

This plan covers 25 improvements across five phases for the TradingResearchEngine platform. Tasks are ordered by dependency (leaf items first within each phase), with sub-task order following: Application interfaces → Infrastructure implementations → Web/UI → Tests. Each task builds incrementally on previous work with no orphaned code.

**Technology**: .NET 8 / C# 12, Blazor Server (MudBlazor), xUnit + FsCheck, JSON repositories with SQLite index layer.

---

## Phase 1 — Fix Broken Functionality

Items 1–6 are all independent and can be executed in parallel.

---

### Task P1-5-1: Fix IStrategy XML Doc Comment

- **Item**: 5
- **Blocked by**: None
- **Effort**: XS (< 30 min)
- **Files to touch**:
  - `src/TradingResearchEngine.Core/Strategy/IStrategy.cs`
- **What to do**:
  1. Open `IStrategy.cs` and locate the `<summary>` and `<remarks>` XML doc blocks.
  2. Remove any text containing "all strategies are long-only" or "Short-selling is out of scope".
  3. Replace with accurate V6+ documentation describing `Direction.Long`, `Direction.Short`, `Direction.Flat` signal semantics, full position lifecycle (enter long, enter short, exit flat), and reversal support via `AllowReversals` on `ExecutionConfig`.
  4. Ensure the doc comment references `SignalEvent` and `OrderEvent` as outputs.
- **Done when**:
  - The XML doc on `IStrategy` accurately describes V6+ capabilities.
  - No mention of "long-only" or "short-selling out of scope" remains.
  - _Requirements: 5.1, 5.2, 5.3_

---

### Task P1-6-1: Delete Orphaned `git` File

- **Item**: 6
- **Blocked by**: None
- **Effort**: XS (< 15 min)
- **Files to touch**:
  - `git` (delete)
  - `.gitignore`
- **What to do**:
  1. Delete the zero-byte file named `git` at the repository root.
  2. Add the rule `/git` to `.gitignore` to prevent recurrence.
- **Done when**:
  - The `git` file no longer exists at the repo root.
  - `.gitignore` contains a rule ignoring `/git`.
  - _Requirements: 6.1, 6.2_

---

### Task P1-1-1: Fix Fork Handler in Step1ChooseStartingPoint

- **Item**: 1
- **Blocked by**: None
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Builder/Step1ChooseStartingPoint.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- **What to do**:
  1. In `Step1ChooseStartingPoint.razor`, add `[Parameter] public EventCallback<StrategyIdentity> OnForkSelected { get; set; }`.
  2. When the user selects a strategy in fork mode, invoke `OnForkSelected` with the full `StrategyIdentity`.
  3. In `StrategyBuilder.razor`, wire `OnForkSelected="HandleForkSelected"`.
  4. Implement `HandleForkSelected(StrategyIdentity identity)`:
     - Call `StrategyRepo.GetLatestVersionAsync(identity.StrategyId)`.
     - If null, show `Snackbar.Add("No versions to fork", Severity.Warning)` and return.
     - Populate `_vm.StrategyType`, `_vm.Parameters`, `_vm.Timeframe`, `_vm.Hypothesis` from the version.
     - Set `_vm.SourceType = SourceType.Fork`, `_vm.IsDirty = true`.
     - Call `LoadSchemaForCurrentStrategy()` and `StateHasChanged()`.
- **Done when**:
  - Selecting a strategy in fork mode populates `BuilderViewModel` with the forked strategy's configuration.
  - If the strategy has no versions, a warning is displayed and no population occurs.
  - _Requirements: 1.1, 1.2, 1.3, 1.4_

---

### Task P1-2-1: Fix Import JSON — Parse and Apply

- **Item**: 2
- **Blocked by**: None
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Builder/Step1ChooseStartingPoint.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- **What to do**:
  1. In `Step1ChooseStartingPoint.razor`, add `[Parameter] public EventCallback<string> OnImportJsonChanged { get; set; }` and bind it to the import text field's value change.
  2. In `StrategyBuilder.razor`, before advancing from Step 1 in Import mode:
     - Attempt `JsonSerializer.Deserialize<ScenarioConfig>(_importJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })`.
     - If deserialization fails, show `Snackbar.Add($"Invalid JSON: {ex.Message}", Severity.Error)` and remain on Step 1.
     - If successful, call `PopulateFromImport(config)`.
  3. Implement `PopulateFromImport(ScenarioConfig config)`:
     - Map `StrategyType`, `Parameters`, `SlippageModelType`, `CommissionModelType`, `InitialCash`, `AnnualRiskFreeRate` to `_vm`.
     - Set `_vm.SourceType = SourceType.Import`, `_vm.IsDirty = true`.
     - Call `LoadSchemaForCurrentStrategy()`.
- **Done when**:
  - Valid JSON is deserialized and populates the builder, advancing to Step 2.
  - Invalid JSON shows an error and remains on Step 1.
  - _Requirements: 2.1, 2.2, 2.3, 2.4_

---

### Task P1-3-1: Fix GoToStep — Allow Navigation to Previously Visited Steps

- **Item**: 3
- **Blocked by**: None
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Builder/BuilderViewModel.cs`
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
  - `src/TradingResearchEngine.Web/Components/Shared/BuilderStepIndicator.razor`
- **What to do**:
  1. Add `public int MaxVisitedStep { get; set; } = 1;` to `BuilderViewModel`.
  2. In `NextStep()`, after incrementing `CurrentStep`, set `MaxVisitedStep = Math.Max(MaxVisitedStep, CurrentStep)`.
  3. Replace the `GoToStep` guard: allow navigation when `step >= 1 && step <= _vm.MaxVisitedStep`.
  4. Update `BuilderStepIndicator.razor` to accept `MaxVisitedStep` parameter and render three visual states:
     - **Current**: filled primary colour.
     - **Visited** (step ≤ MaxVisitedStep, step ≠ CurrentStep): outlined primary, clickable.
     - **Not reached** (step > MaxVisitedStep): greyed out, `disabled`.
- **Done when**:
  - Users can jump to any previously visited step.
  - Steps beyond `MaxVisitedStep` are non-interactive.
  - The step indicator visually differentiates all three states.
  - _Requirements: 3.1, 3.2, 3.3, 3.4_

---

### Task P1-4-1: Fix N+1 Queries — Application Interface

- **Item**: 4
- **Blocked by**: None
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/Strategy/IStrategyRepository.cs`
- **What to do**:
  1. Add `Task<IReadOnlyDictionary<string, int>> GetVersionCountsAsync(IEnumerable<string> strategyIds, CancellationToken ct = default)` to `IStrategyRepository`.
  2. Add `Task<IReadOnlyList<StrategyVersion>> ListAllVersionsAsync(CancellationToken ct = default)` to `IStrategyRepository`.
  3. Add XML doc comments for both methods.
- **Done when**:
  - `IStrategyRepository` exposes both batch methods with proper signatures and documentation.
  - _Requirements: 4.2, 4.3_

---

### Task P1-4-2: Fix N+1 Queries — Infrastructure Implementation

- **Item**: 4
- **Blocked by**: P1-4-1
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Infrastructure/Strategy/JsonStrategyRepository.cs`
- **What to do**:
  1. Implement `GetVersionCountsAsync`: scan the versions directory once, group by `StrategyId`, return counts as `IReadOnlyDictionary<string, int>`.
  2. Implement `ListAllVersionsAsync`: read all version JSON files in one pass, return as `IReadOnlyList<StrategyVersion>`.
  3. Ensure both methods accept and honour `CancellationToken`.
- **Done when**:
  - Both methods execute in a single I/O pass (no per-strategy loops).
  - Results are correct for any number of strategies and versions.
  - _Requirements: 4.3, 4.4_

---

### Task P1-4-3: Fix N+1 Queries — Web Layer

- **Item**: 4
- **Blocked by**: P1-4-2
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Research/ResearchExplorer.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
- **What to do**:
  1. In `ResearchExplorer.razor`:
     - Replace per-row `GetVersionAsync` + `GetAsync` loop with: `ListAsync()` for studies, `ListAsync()` for strategies, `ListAllVersionsAsync()` for versions.
     - Build in-memory lookup `Dictionary<string, string>` mapping `StrategyVersionId → StrategyId`, then join with strategies for name resolution.
  2. In `StrategyLibrary.razor`:
     - Replace per-strategy `GetVersionsAsync` loop with a single `GetVersionCountsAsync(strategyIds)` call.
     - Use the returned dictionary for version count display.
- **Done when**:
  - `ResearchExplorer` loads with O(1) repository calls regardless of row count.
  - `StrategyLibrary` uses a single batch call for version counts.
  - _Requirements: 4.1, 4.2, 4.4_

---

### Task P1-4-4: Fix N+1 Queries — Property Test *

- **Item**: 4
- **Blocked by**: P1-4-2
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/StrategyRepositoryProperties.cs`
- **What to do**:
  1. Create `StrategyRepositoryProperties.cs` in `UnitTests/V7/`.
  2. Implement property test:
     - **Property 4: GetVersionCountsAsync batch correctness**
     - For any set of strategies with varying numbers of versions, `GetVersionCountsAsync` returns a dictionary where each strategy ID maps to the exact count of versions that exist.
  3. Use FsCheck generators for random strategy/version sets with in-memory fakes.
  4. Tag: `// Feature: v7-implementation-plan, Property 4: GetVersionCountsAsync batch correctness`
  5. Use `[Property(MaxTest = 100)]`.
- **Done when**:
  - Property test passes for arbitrary strategy/version combinations.
  - **Property 4: GetVersionCountsAsync batch correctness**
  - **Validates: Requirements 4.2, 4.3**

---

### Task P1-1-2: Fork and Import — Property Tests *

- **Item**: 1, 2
- **Blocked by**: P1-1-1, P1-2-1
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/BuilderViewModelProperties.cs` (new)
  - `src/TradingResearchEngine.UnitTests/V7/ScenarioConfigProperties.cs` (new)
- **What to do**:
  1. Create `BuilderViewModelProperties.cs`:
     - **Property 1: BuilderViewModel population preserves source fields** — For any valid `StrategyVersion`, populating a `BuilderViewModel` preserves `StrategyType`, `Parameters`, `Timeframe`, `Hypothesis`. For any valid `ScenarioConfig`, `PopulateFromImport` preserves all mapped fields.
  2. Create `ScenarioConfigProperties.cs`:
     - **Property 2: ScenarioConfig JSON round-trip** — For any valid `ScenarioConfig`, serializing to JSON and deserializing back produces a structurally equivalent config.
  3. Tag: `// Feature: v7-implementation-plan, Property 1/2`
  4. Use `[Property(MaxTest = 100)]`.
- **Done when**:
  - Both property tests pass for arbitrary inputs.
  - **Property 1: BuilderViewModel population — Validates: Requirements 1.2, 2.2**
  - **Property 2: ScenarioConfig JSON round-trip — Validates: Requirement 2.1**

---

### Task P1-3-2: GoToStep Navigation — Property Test *

- **Item**: 3
- **Blocked by**: P1-3-1
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/BuilderNavigationProperties.cs` (new)
- **What to do**:
  1. Create `BuilderNavigationProperties.cs`:
     - **Property 3: MaxVisitedStep navigation invariant** — For any sequence of `NextStep` and `GoToStep(N)` operations, `GoToStep(N)` sets `CurrentStep` to N iff N ≤ MaxVisitedStep. When N > MaxVisitedStep, `CurrentStep` remains unchanged. `MaxVisitedStep` always equals the maximum step index ever reached.
  2. Use FsCheck generators for random sequences of step operations.
  3. Tag: `// Feature: v7-implementation-plan, Property 3: MaxVisitedStep navigation invariant`
  4. Use `[Property(MaxTest = 100)]`.
- **Done when**:
  - Property test passes for arbitrary step operation sequences.
  - **Property 3: MaxVisitedStep navigation invariant — Validates: Requirements 3.1, 3.2, 3.3**

---

## Phase 2 — Navigation & Discovery

Items 7–10 are all independent and can be executed in parallel.

---

### Task P2-7-1: Restructure NavMenu

- **Item**: 7
- **Blocked by**: None
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Layout/NavMenu.razor`
  - `src/TradingResearchEngine.Web/wwwroot/js/navMenuState.js` (new)
- **What to do**:
  1. Replace the entire `NavMenu.razor` content with a structured hierarchy using `MudNavGroup` for collapsible sections:
     - DASHBOARD → `/`
     - STRATEGIES (group): My Strategies → `/strategies/library`, New Strategy → `/strategies/builder`
     - RESEARCH (group): Explorer → `/research/explorer`, Parameter Sweep → `/research/sweep`, Monte Carlo → `/research/montecarlo`, Walk-Forward → `/research/walkforward`, Perturbation → `/research/perturbation`, Variance → `/research/variance`, Benchmark → `/research/benchmark`
     - BACKTESTS → `/backtests`
     - PROP FIRM LAB → `/propfirm/evaluate`
     - DATA (group): Market Data → `/market-data`, Data Files → `/data`
     - SETTINGS → `/settings`
  2. Create `navMenuState.js` with functions to read/write `MudNavGroup` expanded state to `localStorage` keyed by section name.
  3. In `NavMenu.razor`, bind each `MudNavGroup.Expanded` to a C# field initialised from `localStorage` on `OnAfterRenderAsync(firstRender: true)` via JS interop.
- **Done when**:
  - All application pages are reachable from the NavMenu.
  - Collapsible groups persist expanded/collapsed state across page navigation.
  - DATA section is separate from SETTINGS.
  - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

---

### Task P2-8-1: Add Launch Actions to Research Explorer

- **Item**: 8
- **Blocked by**: None
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Research/ResearchExplorer.razor`
- **What to do**:
  1. Add a toolbar above the studies table with a `MudMenu` "New Study" button listing all study types: Sweep, Monte Carlo, Walk-Forward, Perturbation, Variance, Benchmark.
  2. Implement `LaunchStudy(string type)` that navigates to `/research/{type}`, optionally appending `?StrategyId=` from a selected table row.
  3. Add a `MudSelect` "Filter by Strategy" dropdown that filters the studies table by strategy.
- **Done when**:
  - Users can launch any study type from the Research Explorer.
  - Optional `StrategyId` pre-seeding works from selected rows.
  - Strategy filter dropdown is functional.
  - _Requirements: 8.1, 8.2, 8.3, 8.4_

---

### Task P2-9-1: Add Backtests List Page

- **Item**: 9
- **Blocked by**: None
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Backtests/BacktestList.razor` (new)
- **What to do**:
  1. Create `BacktestList.razor` at route `/backtests`.
  2. Inject `IBacktestResultRepository` and `NavigationManager`.
  3. Render a `MudTable<BacktestResult>` with columns: Strategy Type, Run Date, Sharpe Ratio, Max Drawdown, Total Trades, Status.
  4. Support server-side sorting and client-side filtering by strategy type and status.
  5. Row click navigates to `/backtests/{id}`.
  6. Parse run date from `RunId` prefix using a shared `TryParseRunDate` helper.
- **Done when**:
  - `/backtests` page displays all backtest results in a sortable, filterable table.
  - Row click navigates to the detail page.
  - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5_

---

### Task P2-10-1: Implement Stale Strategy Status

- **Item**: 10
- **Blocked by**: None
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/Configuration/StalenessOptions.cs` (new)
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
  - `src/TradingResearchEngine.Web/appsettings.json`
  - `src/TradingResearchEngine.Web/Program.cs`
- **What to do**:
  1. Create `StalenessOptions` class with `public int StalenessThresholdDays { get; set; } = 30;`.
  2. Add `"Staleness": { "StalenessThresholdDays": 30 }` to `appsettings.json`.
  3. Register `IOptions<StalenessOptions>` in `Program.cs`.
  4. In `StrategyLibrary.razor`:
     - Inject `IOptions<StalenessOptions>`.
     - Implement `IsStale(StrategyIdentity, BacktestResult?)` — returns true when last run date is older than threshold.
     - Enable the "Stale" filter chip (remove `Disabled="true"`).
     - Add `Color.Warning` "STALE" badge to strategy cards meeting criteria.
- **Done when**:
  - Staleness is computed based on configurable threshold.
  - "Stale" filter chip is functional.
  - Stale strategies display a warning badge.
  - _Requirements: 10.1, 10.2, 10.3, 10.4_

---

### Task P2-10-2: Stale Strategy Status — Property Test *

- **Item**: 10
- **Blocked by**: P2-10-1
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/StalenessProperties.cs` (new)
- **What to do**:
  1. Create `StalenessProperties.cs` in `UnitTests/V7/`.
  2. Implement property test:
     - **Property 5: Staleness computation**
     - For any strategy with a last run date and any positive staleness threshold, `IsStale` returns true iff the run date is older than `DateTime.UtcNow - threshold`. Strategies with no runs are never stale.
  3. Tag: `// Feature: v7-implementation-plan, Property 5: IsStale_CorrectForAnyDateAndThreshold`
  4. Use `[Property(MaxTest = 100)]`.
- **Done when**:
  - Property test passes for arbitrary dates and thresholds.
  - **Property 5: Staleness computation**
  - **Validates: Requirement 10.2**

---

## Checkpoint 1

- [ ] Ensure all tests pass, ask the user if questions arise.

---

## Phase 3 — Builder & Engine Quality

Items 11–16 are all independent (Item 15 depends on Item 9 for the BacktestDetail page existing, but the chart component itself is independent).

---

### Task P3-11-1: Schema-Driven Validation — Application Layer

- **Item**: 11
- **Blocked by**: None
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/Strategy/SchemaValidator.cs` (new)
- **What to do**:
  1. Create `SchemaValidator.cs` as a static class with:
     - `ValidateParameters(Dictionary<string, object> parameters, IReadOnlyList<StrategyParameterSchema> schemas)` → `IReadOnlyList<ValidationError>`.
     - `ValidateRiskProfile(Dictionary<string, object> riskParams, decimal stopLoss)` → `IReadOnlyList<ValidationError>`.
  2. Define `public sealed record ValidationError(string FieldName, string Message)`.
  3. `ValidateParameters` checks: required fields present, numeric values within `[Min, Max]` bounds from schema.
  4. `ValidateRiskProfile` checks: allocation percentages sum ≤ 100%, stop-loss > 0.
- **Done when**:
  - `SchemaValidator` correctly validates parameters against schema bounds.
  - `ValidationError` record is defined.
  - _Requirements: 11.1, 11.3_

---

### Task P3-11-2: Schema-Driven Validation — Web Integration

- **Item**: 11
- **Blocked by**: P3-11-1
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/Steps/Step3StrategyParameters.razor`
- **What to do**:
  1. In `StrategyBuilder.razor`, update `CanAdvance()`:
     - Step 3: `SchemaValidator.ValidateParameters(_vm.Parameters, _schemas).Count == 0`.
     - Step 4: `SchemaValidator.ValidateRiskProfile(_vm.PresetOverrides, stopLoss).Count == 0`.
  2. In `Step3StrategyParameters.razor`, pass validation errors and display per-field `MudTextField` validation messages.
- **Done when**:
  - Invalid parameters prevent advancement from Steps 3 and 4.
  - Per-field validation messages are displayed.
  - _Requirements: 11.2, 11.4, 11.5_

---

### Task P3-11-3: Schema Validation — Property Tests *

- **Item**: 11
- **Blocked by**: P3-11-1
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/SchemaValidatorProperties.cs` (new)
  - `src/TradingResearchEngine.UnitTests/V7/SchemaValidatorTests.cs` (new)
- **What to do**:
  1. Create `SchemaValidatorProperties.cs`:
     - **Property 6: Parameter schema validation** — For any numeric value and schema with Min/Max, returns error iff value is outside `[Min, Max]`.
     - **Property 7: Risk allocation validation** — For any set of percentages, returns error iff sum > 100% or stop-loss ≤ 0.
  2. Create `SchemaValidatorTests.cs` with example-based tests:
     - Out-of-range value fails; in-range passes; missing required field fails.
  3. Tag properties: `// Feature: v7-implementation-plan, Property 6/7`
  4. Use `[Property(MaxTest = 100)]`.
- **Done when**:
  - Both property tests pass.
  - Example-based tests cover edge cases.
  - **Property 6: Parameter schema validation — Validates: Requirement 11.1**
  - **Property 7: Risk allocation validation — Validates: Requirement 11.3**

---

### Task P3-12-1: Differentiate Quick Sanity from Standard Backtest

- **Item**: 12
- **Blocked by**: None
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Backtests/BacktestDetail.razor`
- **What to do**:
  1. In `StrategyBuilder.razor`, in the `OnLaunchAction("quick-sanity")` handler:
     - Apply transient overrides: `ExecutionRealismProfile.FastResearch`, reduced date range (last 2 years), `MaxTrials = 1`.
     - Pass the modified config to `RunUseCase.RunAsync`.
     - Navigate to result page with `?quickSanity=true` query parameter.
  2. In `BacktestDetail.razor`, check for `quickSanity=true` query param and display a "Quick Sanity — reduced dataset" banner.
  3. Ensure overrides are never persisted to `ConfigDraft` or `StrategyVersion`.
- **Done when**:
  - Quick Sanity runs with reduced config (fast feedback).
  - Standard Backtest uses full user config.
  - Result page shows a badge for Quick Sanity runs.
  - Overrides are transient only.
  - _Requirements: 12.1, 12.2, 12.3, 12.4_

---

### Task P3-13-1: Replace `dynamic` Result Type in HandleRunResult

- **Item**: 13
- **Blocked by**: None
- **Effort**: XS (30 min)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- **What to do**:
  1. Replace `HandleRunResult(dynamic result)` with `HandleRunResult(ScenarioRunResult result, bool isQuickSanity = false)`.
  2. Use typed property access: `result.IsSuccess`, `result.Result`, `result.Errors`.
  3. Remove the `Task.Run(() => RunUseCase.RunAsync(config))` wrapper — call `await RunUseCase.RunAsync(config)` directly.
  4. Handle success (navigate to result page) and failure (show error snackbar) with pattern matching.
- **Done when**:
  - No `dynamic` keyword in `HandleRunResult`.
  - Compile-time type safety is enforced.
  - `RuntimeBinderException` risk is eliminated.
  - _Requirements: 13.1, 13.2, 13.3_

---

### Task P3-14-1: Fix StrategyId Truncation — Application Layer

- **Item**: 14
- **Blocked by**: None
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/Strategy/StrategyIdGenerator.cs` (new)
- **What to do**:
  1. Create `StrategyIdGenerator.cs` with static method `Generate(string? strategyName)`:
     - If name is null/whitespace: return `$"strategy-{Guid.NewGuid().ToString("N")[..8]}"`.
     - Otherwise: slugify name (lowercase, replace spaces with hyphens, remove non-alphanumeric/hyphen chars), truncate to 20 chars, append `-{Guid.NewGuid().ToString("N")[..8]}`.
  2. Use compiled `Regex` for non-slug character removal.
  3. Ensure output matches `^[a-z0-9-]+$` (URL-safe).
- **Done when**:
  - `StrategyIdGenerator.Generate` produces human-readable, URL-safe IDs with ≥ 32 bits of randomness.
  - _Requirements: 14.1, 14.2, 14.3, 14.4_

---

### Task P3-14-2: Fix StrategyId Truncation — Web Integration

- **Item**: 14
- **Blocked by**: P3-14-1
- **Effort**: XS (15 min)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
- **What to do**:
  1. Replace the existing `$"strategy-{Guid.NewGuid():N}".Substring(0, 20)` call in `SaveDraftAndVersion` with `StrategyIdGenerator.Generate(_vm.StrategyName)`.
- **Done when**:
  - Strategy IDs are generated via the new `StrategyIdGenerator`.
  - _Requirements: 14.1_

---

### Task P3-14-3: Fix StrategyId Truncation — Property Test *

- **Item**: 14
- **Blocked by**: P3-14-1
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/StrategyIdGeneratorProperties.cs` (new)
  - `src/TradingResearchEngine.UnitTests/V7/StrategyIdGeneratorTests.cs` (new)
- **What to do**:
  1. Create `StrategyIdGeneratorProperties.cs`:
     - **Property 8: Strategy ID generation** — For any strategy name (including empty, whitespace, special chars), `Generate` produces an ID matching `^[a-z0-9-]+$` with ≥ 8 hex chars of randomness.
  2. Create `StrategyIdGeneratorTests.cs`:
     - Empty name gets "strategy-" prefix.
     - 10,000 rapid generations are unique.
     - Special characters are stripped.
  3. Tag: `// Feature: v7-implementation-plan, Property 8: Strategy ID generation`
- **Done when**:
  - Property test passes for arbitrary names.
  - Uniqueness test passes for 10,000 generations.
  - **Property 8: Strategy ID generation — Validates: Requirements 14.1, 14.2, 14.3, 14.4**

---

### Task P3-15-1: Add Equity Curve Chart to Backtest Result Page

- **Item**: 15
- **Blocked by**: P2-9-1 (BacktestDetail page must exist)
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Charts/EquityCurveChart.razor` (new)
  - `src/TradingResearchEngine.Web/Components/Pages/Backtests/BacktestDetail.razor`
- **What to do**:
  1. Create `EquityCurveChart.razor` using the existing `Plotly.Blazor` package:
     - Accept `[Parameter] public IReadOnlyList<EquityCurvePoint> EquityCurve { get; set; }`.
     - Build two traces: line trace for equity value (primary Y-axis), filled area trace for drawdown (secondary Y-axis).
  2. In `BacktestDetail.razor`, add the chart component above the metrics table:
     - Guard with `@if (Result.EquityCurve.Count > 0)`.
- **Done when**:
  - Equity curve chart renders when data is available.
  - Chart shows equity line and drawdown shading.
  - No error when equity curve data is empty.
  - _Requirements: 15.1, 15.2, 15.3, 15.4, 15.5_

---

### Task P3-16-1: Extract RobustnessAdvisoryService — Application Layer

- **Item**: 16
- **Blocked by**: None
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/Research/IRobustnessAdvisoryService.cs` (new)
  - `src/TradingResearchEngine.Application/Research/RobustnessAdvisoryService.cs` (new)
  - `src/TradingResearchEngine.Application/Research/RobustnessThresholds.cs` (new)
- **What to do**:
  1. Create `IRobustnessAdvisoryService` with `IReadOnlyList<string> GetWarnings(BacktestResult result)`.
  2. Create `RobustnessThresholds` options class with: `MaxSharpeRatio = 3.0m`, `MinTotalTrades = 30`, `MinKRatio = 0m`, `MaxDrawdownPercent = 0.20m`.
  3. Create `RobustnessAdvisoryService` implementing the interface:
     - Inject `IOptions<RobustnessThresholds>`.
     - Check each metric against its threshold, return warning strings for violations.
- **Done when**:
  - Service correctly evaluates all four thresholds.
  - Thresholds are configurable via `IOptions<T>`.
  - _Requirements: 16.1, 16.2_

---

### Task P3-16-2: Extract RobustnessAdvisoryService — Web Integration

- **Item**: 16
- **Blocked by**: P3-16-1
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
  - `src/TradingResearchEngine.Web/appsettings.json`
  - `src/TradingResearchEngine.Web/Program.cs` (or `ServiceCollectionExtensions.cs`)
- **What to do**:
  1. Register `IRobustnessAdvisoryService` as singleton in DI.
  2. Add `RobustnessThresholds` section to `appsettings.json`.
  3. In `Dashboard.razor`, replace inline threshold checks with `_robustnessService.GetWarnings(run)`.
  4. In `StrategyLibrary.razor`, replace inline threshold checks with the service call.
- **Done when**:
  - Both pages use the centralised service.
  - Threshold changes in config apply to both pages without code modification.
  - _Requirements: 16.3, 16.4, 16.5_

---

### Task P3-16-3: RobustnessAdvisoryService — Tests *

- **Item**: 16
- **Blocked by**: P3-16-1
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/RobustnessAdvisoryProperties.cs` (new)
  - `src/TradingResearchEngine.UnitTests/V7/RobustnessAdvisoryServiceTests.cs` (new)
- **What to do**:
  1. Create `RobustnessAdvisoryProperties.cs`:
     - **Property 9: RobustnessAdvisoryService threshold warnings** — For any `BacktestResult` metrics and any thresholds, `GetWarnings` returns a warning for each metric exceeding its threshold and none for metrics within bounds.
  2. Create `RobustnessAdvisoryServiceTests.cs`:
     - Test each threshold boundary individually (Sharpe=3.0, Trades=30, K-Ratio=0, DD=20%).
  3. Tag: `// Feature: v7-implementation-plan, Property 9`
- **Done when**:
  - Property test passes for arbitrary metrics and thresholds.
  - Boundary tests pass.
  - **Property 9: RobustnessAdvisoryService threshold warnings — Validates: Requirements 16.1, 16.2**

---

## Checkpoint 2

- [ ] Ensure all tests pass, ask the user if questions arise.

---

## Phase 4 — LLM Strategy Assistant

Dependency order: Item 21 (Prompt Mgmt) → Item 17 (LLM Translator) → Item 18 (Step 0). Item 19 (Indicators) is independent. Item 20 (Roslyn) depends on Item 19.

---

### Task P4-21-1: Prompt Management — Application Interface

- **Item**: 21
- **Blocked by**: None
- **Effort**: XS (30 min)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/AI/IPromptLoader.cs` (new)
- **What to do**:
  1. Create `IPromptLoader` interface with:
     - `string GetPrompt(string promptName)` — returns content of a named prompt file.
     - `string GetPrompt(string promptName, Dictionary<string, string> tokens)` — returns content with token interpolation.
  2. Add XML doc comments.
- **Done when**:
  - `IPromptLoader` interface is defined in the Application layer.
  - _Requirements: 21.1, 21.5_

---

### Task P4-21-2: Prompt Management — Infrastructure Implementation

- **Item**: 21
- **Blocked by**: P4-21-1
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Infrastructure/AI/PromptLoader.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/ServiceCollectionExtensions.cs`
- **What to do**:
  1. Create `PromptLoader` implementing `IPromptLoader`:
     - Constructor accepts `string promptsDirectory`.
     - On construction, read all `.md` files from the `Prompts/` directory and cache in a `Dictionary<string, string>` (key = filename without extension).
     - `GetPrompt(name)`: return cached content or throw `InvalidOperationException` listing available prompts.
     - `GetPrompt(name, tokens)`: get template, replace `{key}` with value for each token.
  2. Register as singleton in `ServiceCollectionExtensions.cs`.
- **Done when**:
  - Prompts are loaded from disk at startup and cached.
  - Missing prompts throw descriptive exceptions.
  - Token interpolation replaces all placeholders.
  - _Requirements: 21.2, 21.3, 21.4, 21.5_

---

### Task P4-21-3: Prompt Management — Tests *

- **Item**: 21
- **Blocked by**: P4-21-2
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/PromptLoaderProperties.cs` (new)
  - `src/TradingResearchEngine.UnitTests/V7/PromptLoaderTests.cs` (new)
- **What to do**:
  1. Create `PromptLoaderProperties.cs`:
     - **Property 13: Prompt token interpolation** — For any template with `{templates_json}` and `{schemas_json}` placeholders and any replacement values, all placeholders are replaced and none remain.
  2. Create `PromptLoaderTests.cs`:
     - Missing prompt throws with available prompt list.
     - No-token prompt returned as-is.
     - Interpolation replaces tokens correctly.
  3. Tag: `// Feature: v7-implementation-plan, Property 13`
- **Done when**:
  - Property test passes.
  - Example tests cover error cases.
  - **Property 13: Prompt token interpolation — Validates: Requirement 21.5**

---

### Task P4-17-1: LLM Translator — Application Interfaces

- **Item**: 17
- **Blocked by**: P4-21-1
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/AI/IStrategyIdeaTranslator.cs` (new)
  - `src/TradingResearchEngine.Application/AI/StrategyIdeaResult.cs` (new)
  - `src/TradingResearchEngine.Application/Configuration/LlmProviderOptions.cs` (new)
- **What to do**:
  1. Create `IStrategyIdeaTranslator` interface with `TranslateAsync(string userDescription, IReadOnlyList<StrategyTemplate> templates, IReadOnlyList<StrategyParameterSchema> schemas, CancellationToken ct)`.
  2. Create `StrategyIdeaResult` record with: `Success`, `SelectedTemplateId`, `StrategyType`, `SuggestedParameters`, `SuggestedHypothesis`, `SuggestedTimeframe`, `FailureReason`, `GeneratedStrategyCode`.
  3. Create `LlmProviderOptions` class with: `EnableAIStrategyAssist`, `Provider`, `BaseUrl`, `Model`, `ApiKey`, `FallbackProviders`, `OllamaBaseUrl`, `OllamaModel`, `GroqBaseUrl`, `GroqApiKey`, `GroqModel`.
- **Done when**:
  - All Application-layer types for LLM integration are defined.
  - _Requirements: 17.1, 17.2_

---

### Task P4-17-2: LLM Translator — Infrastructure Implementations ⚠️ Security

- **Item**: 17
- **Blocked by**: P4-17-1, P4-21-2
- **Effort**: L (4–6 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Infrastructure/AI/GoogleAiStudioTranslator.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/AI/GroqTranslator.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/AI/OllamaTranslator.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/AI/FallbackStrategyIdeaTranslator.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/ServiceCollectionExtensions.cs`
  - `src/TradingResearchEngine.Web/appsettings.json`
  - `Prompts/strategy-idea-translator-system-prompt.md` (new)
- **What to do**:
  1. Implement `GoogleAiStudioTranslator`: uses `IHttpClientFactory`, calls OpenAI-compatible `/chat/completions` endpoint, sets `response_format` for JSON schema enforcement, deserializes response to `StrategyIdeaResult`.
  2. Implement `GroqTranslator`: same pattern as Google, different base URL and auth header.
  3. Implement `OllamaTranslator`: calls `/api/generate` with `format: "json"`.
  4. Implement `FallbackStrategyIdeaTranslator`:
     - Wraps provider chain (configurable order).
     - When `EnableAIStrategyAssist` is false, returns failure immediately without HTTP calls.
     - Tries each provider in order, returns first success.
     - Logs warnings on provider failures.
  5. Create system prompt file `Prompts/strategy-idea-translator-system-prompt.md` with `{templates_json}` and `{schemas_json}` interpolation tokens.
  6. Add `LlmProvider` section to `appsettings.json`.
  7. Register all providers and `FallbackStrategyIdeaTranslator` as `IStrategyIdeaTranslator` in DI.
  8. **Security**: API keys sourced from configuration (env vars/user secrets) — never hardcoded. No user data sent when feature is disabled.
- **Done when**:
  - All three providers are implemented.
  - Fallback chain works correctly.
  - Feature flag prevents any HTTP calls when disabled.
  - System prompt is loaded from file via `IPromptLoader`.
  - _Requirements: 17.3, 17.4, 17.5, 17.6, 17.7_

---

### Task P4-17-3: LLM Translator — Tests *

- **Item**: 17
- **Blocked by**: P4-17-2
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/FallbackTranslatorProperties.cs` (new)
  - `src/TradingResearchEngine.UnitTests/V7/FallbackTranslatorTests.cs` (new)
- **What to do**:
  1. Create `FallbackTranslatorProperties.cs`:
     - **Property 10: Fallback translator chain** — For any ordered list of providers (success/fail), returns first success. All fail → failure. Feature disabled → immediate failure without invocation.
  2. Create `FallbackTranslatorTests.cs`:
     - Feature flag disabled returns immediately.
     - Single provider success.
     - All providers fail returns failure.
     - First success in chain is returned.
  3. Use Moq for provider mocks.
  4. Tag: `// Feature: v7-implementation-plan, Property 10`
- **Done when**:
  - Property test passes for arbitrary provider sequences.
  - Example tests cover all scenarios.
  - **Property 10: Fallback translator chain — Validates: Requirements 17.4, 17.5**

---

### Task P4-18-1: Add "Describe Your Idea" Step 0 to Strategy Builder

- **Item**: 18
- **Blocked by**: P4-17-2
- **Effort**: M (3–4 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/Steps/Step0IdeaDescription.razor` (new)
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyBuilder.razor`
  - `src/TradingResearchEngine.Web/Components/Shared/BuilderStepIndicator.razor`
  - `src/TradingResearchEngine.Web/Components/Builder/BuilderViewModel.cs`
- **What to do**:
  1. Create `Step0IdeaDescription.razor`:
     - Multi-line `MudTextField` with placeholder "Describe your trading idea in plain language...".
     - "Use AI to prefill →" button (disabled when `EnableAIStrategyAssist` is false, with tooltip).
     - "Skip, start manually →" link advancing to Step 1.
     - `MudProgressLinear` with "Analysing your idea…" during LLM call.
     - Error display with retry/skip options on failure.
  2. In `StrategyBuilder.razor`:
     - Add Step 0 case in the step switch.
     - On AI success: advance to Step 2 with fields pre-populated from `StrategyIdeaResult`, show dismissible `MudAlert`.
  3. Update `BuilderStepIndicator.razor` to support 6 steps (0–5): "Idea → Source → Data → Parameters → Realism → Launch".
  4. Update `BuilderViewModel.CurrentStep` default to `0`.
- **Done when**:
  - Step 0 is the first step in the builder.
  - AI prefill works when enabled.
  - Skip advances to Step 1 without LLM call.
  - Loading state and error handling work correctly.
  - _Requirements: 18.1, 18.2, 18.3, 18.4, 18.5, 18.6, 18.7_

---

### Task P4-19-1: Core Indicators Library — Interface and Implementations

- **Item**: 19
- **Blocked by**: None
- **Effort**: L (4–6 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Core/Indicators/IIndicator.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/SimpleMovingAverage.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/ExponentialMovingAverage.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/AverageTrueRange.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/RelativeStrengthIndex.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/BollingerBands.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/RollingZScore.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/DonchianChannel.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/BollingerBandsOutput.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/DonchianChannelOutput.cs` (new)
- **What to do**:
  1. Create `IIndicator<TOutput>` interface with: `Value` (nullable), `IsReady`, `Update(decimal close, decimal? high, decimal? low)`, `Reset()`.
  2. Implement all 7 indicators:
     - `SimpleMovingAverage`: circular buffer, O(1) per update, period warmup.
     - `ExponentialMovingAverage`: multiplier `2/(Period+1)`, period warmup.
     - `AverageTrueRange`: requires high/low, Wilder smoothing.
     - `RelativeStrengthIndex`: Wilder smoothing for avg gain/loss.
     - `BollingerBands`: returns `BollingerBandsOutput(Upper, Middle, Lower, BandWidth)`.
     - `RollingZScore`: `(value - mean) / stddev`.
     - `DonchianChannel`: returns `DonchianChannelOutput(Upper, Lower, Middle)`.
  3. Define output records: `BollingerBandsOutput`, `DonchianChannelOutput`.
  4. All indicators: `Value` is null until warmed up, `IsReady` is false until sufficient data.
  5. Add XML doc comments to all public types.
- **Done when**:
  - All 7 indicators are implemented with correct warmup behaviour.
  - `IIndicator<TOutput>` interface is defined.
  - Output records are defined.
  - _Requirements: 19.1, 19.2, 19.3_

---

### Task P4-19-2: Core Indicators — Migrate ZScoreMeanReversionStrategy

- **Item**: 19
- **Blocked by**: P4-19-1
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Core/Strategy/ZScoreMeanReversionStrategy.cs`
- **What to do**:
  1. Refactor `ZScoreMeanReversionStrategy` to use `RollingZScore` from the indicator library instead of inline rolling-window arithmetic.
  2. Create a `RollingZScore` instance internally with the configured period.
  3. Replace manual mean/stddev calculations with `_zScore.Update(close)` and `_zScore.Value`.
  4. Ensure existing tests still pass (behaviour should be identical).
- **Done when**:
  - `ZScoreMeanReversionStrategy` uses `RollingZScore` from the indicator library.
  - No inline rolling-window arithmetic remains for z-score calculation.
  - All existing strategy tests pass unchanged.
  - _Requirements: 19.5_

---

### Task P4-19-3: Core Indicators — Tests *

- **Item**: 19
- **Blocked by**: P4-19-1
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/IndicatorProperties.cs` (new)
  - `src/TradingResearchEngine.UnitTests/V7/IndicatorTests.cs` (new)
- **What to do**:
  1. Create `IndicatorProperties.cs`:
     - **Property 11: Indicator reset and replay determinism** — For any indicator and any data series, `Reset()` then replay produces identical output values.
  2. Create `IndicatorTests.cs`:
     - Test each indicator (SMA, EMA, ATR, RSI, BB, ZScore, Donchian) against known reference values (Excel-calculated) within tolerance of 1e-10.
     - Test warmup behaviour: `Value` is null and `IsReady` is false before sufficient data.
  3. Tag: `// Feature: v7-implementation-plan, Property 11`
  4. Use `[Property(MaxTest = 100)]` with random price series generators.
- **Done when**:
  - Property test passes for all indicators.
  - Reference value tests pass within tolerance.
  - **Property 11: Indicator reset and replay determinism — Validates: Requirement 19.6**

---

### Task P4-20-1: Dynamic Roslyn Strategy Compiler — Application Layer

- **Item**: 20
- **Blocked by**: P4-19-1
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/Strategy/DynamicStrategyRegistry.cs` (new)
  - `src/TradingResearchEngine.Application/Strategy/StrategyRegistry.cs`
- **What to do**:
  1. Create `DynamicStrategyRegistry` class:
     - Inject `StrategyRegistry`.
     - `Register(string name, Type strategyType)`: validate type implements `IStrategy`, then call `_registry.RegisterDynamic(name, strategyType)`.
  2. Extend `StrategyRegistry` with `RegisterDynamic(string name, Type type)`:
     - Same validation as `RegisterAssembly` but for a single type.
     - Add to internal name → type dictionary.
- **Done when**:
  - `DynamicStrategyRegistry` can register runtime-compiled strategies.
  - `StrategyRegistry` supports dynamic registration.
  - _Requirements: 20.7_

---

### Task P4-20-2: Dynamic Roslyn Strategy Compiler — Infrastructure ⚠️ Security

- **Item**: 20
- **Blocked by**: P4-20-1
- **Effort**: L (4–6 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Infrastructure/Compilation/IRoslynStrategyCompiler.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/Compilation/RoslynStrategyCompiler.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/Compilation/StrategyCodeSanitiser.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/Compilation/RestrictedAssemblyLoadContext.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/Compilation/CompilationOptions.cs` (new)
  - `src/TradingResearchEngine.Infrastructure/TradingResearchEngine.Infrastructure.csproj`
- **What to do**:
  1. Add `Microsoft.CodeAnalysis.CSharp` NuGet package to Infrastructure project.
  2. Create `CompilationOptions` with `EnableDynamicStrategyCompilation` (default: false).
  3. Create `StrategyCodeSanitiser` (static class):
     - Banned patterns: `HttpClient`, `WebClient`, `File.`, `Directory.`, `Process.`, `Environment.`, `Assembly.Load`, `Type.GetType`, `Activator.CreateInstance`, `Marshal`, `unsafe`, `extern`, `DllImport`.
     - `Sanitise(string sourceCode)` → `SanitiserResult(Accepted/Rejected, violations)`.
  4. Create `RestrictedAssemblyLoadContext`:
     - Only allows references to `TradingResearchEngine.Core` and `System.Runtime`.
     - No file system, network, or reflection APIs.
  5. Create `IRoslynStrategyCompiler` interface with `CompileAsync(string sourceCode, CancellationToken ct)`.
  6. Create `RoslynStrategyCompiler`:
     - Check `EnableDynamicStrategyCompilation` — return error if disabled.
     - Run sanitiser — reject if violations found.
     - Compile using Roslyn `CSharpCompilation`.
     - Load in `RestrictedAssemblyLoadContext`.
     - Verify compiled type implements `IStrategy`.
     - Log full source, sanitiser result, and diagnostics via `ILogger<RoslynStrategyCompiler>`.
     - Return `CompilationResult`.
  7. **Security**: Feature gated behind config flag. Restricted ALC. Full audit logging. Banned API deny-list.
- **Done when**:
  - Compiler compiles valid `IStrategy` source code at runtime.
  - Banned patterns are rejected.
  - Assembly loads in restricted context.
  - Feature flag prevents compilation when disabled.
  - _Requirements: 20.1, 20.2, 20.3, 20.4, 20.5, 20.6_

---

### Task P4-20-3: Dynamic Roslyn Compiler — Tests * ⚠️ Security

- **Item**: 20
- **Blocked by**: P4-20-2
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/StrategyCodeSanitiserProperties.cs` (new)
  - `src/TradingResearchEngine.UnitTests/V7/StrategyCodeSanitiserTests.cs` (new)
  - `src/TradingResearchEngine.IntegrationTests/V7/RoslynCompilerIntegrationTests.cs` (new)
- **What to do**:
  1. Create `StrategyCodeSanitiserProperties.cs`:
     - **Property 12: StrategyCodeSanitiser banned pattern rejection** — For any source code, rejects iff it contains at least one banned pattern.
  2. Create `StrategyCodeSanitiserTests.cs`:
     - Each banned pattern individually triggers rejection.
     - Clean code passes.
  3. Create `RoslynCompilerIntegrationTests.cs`:
     - Compile valid `IStrategy` source → success.
     - Compile code not implementing `IStrategy` → rejection.
     - Compile with banned API → rejection.
     - Feature disabled → error without compilation.
     - Restricted ALC prevents file system access.
  4. Tag: `// Feature: v7-implementation-plan, Property 12`
- **Done when**:
  - Property test passes.
  - Integration tests verify full compilation pipeline.
  - **Property 12: StrategyCodeSanitiser banned pattern rejection — Validates: Requirement 20.2**

---

## Checkpoint 3

- [ ] Ensure all tests pass, ask the user if questions arise.

---

## Phase 5 — Long-Term Architecture

Dependency order: Item 19 → Item 24 (Indicator Registry). Item 9 → Item 23 (CancellationToken needs BacktestList). Items 19, 20, 24 → Item 25 (Signal Composer).

---

### Task P5-22-1: SQLite Index Queries — Application Interface

- **Item**: 22
- **Blocked by**: None
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/Research/IBacktestResultRepository.cs`
- **What to do**:
  1. Add to `IBacktestResultRepository`:
     - `Task<IReadOnlyList<BacktestResult>> GetRecentRunsAsync(int limit, CancellationToken ct = default)`
     - `Task<IReadOnlyDictionary<string, BacktestResult>> GetLastRunPerStrategyAsync(CancellationToken ct = default)`
     - `Task<IReadOnlyList<BacktestResult>> GetRunSummariesByStrategyAsync(string strategyId, CancellationToken ct = default)`
  2. Add XML doc comments for each method.
- **Done when**:
  - `IBacktestResultRepository` exposes targeted query methods.
  - _Requirements: 22.1, 22.2, 22.3_

---

### Task P5-22-2: SQLite Index Queries — Infrastructure Implementation

- **Item**: 22
- **Blocked by**: P5-22-1
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Infrastructure/Persistence/SqliteIndexRepository.cs`
- **What to do**:
  1. Implement `GetRecentRunsAsync(int limit)`:
     - Query: `SELECT json_path FROM backtest_index ORDER BY run_date DESC LIMIT @limit`.
     - Deserialize only the requested JSON files.
  2. Implement `GetLastRunPerStrategyAsync()`:
     - Query: `SELECT json_path FROM backtest_index WHERE rowid IN (SELECT MAX(rowid) FROM backtest_index GROUP BY strategy_type)`.
  3. Implement `GetRunSummariesByStrategyAsync(string strategyId)`:
     - Query: `SELECT json_path FROM backtest_index WHERE strategy_id = @strategyId`.
  4. All methods honour `CancellationToken`.
- **Done when**:
  - All three methods execute targeted SQLite queries (not full collection scans).
  - Only required JSON files are deserialized.
  - _Requirements: 22.1, 22.2, 22.3_

---

### Task P5-22-3: SQLite Index Queries — Web Integration

- **Item**: 22
- **Blocked by**: P5-22-2
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
- **What to do**:
  1. In `Dashboard.razor`:
     - Replace `ResultRepo.ListAsync()` with `GetRecentRunsAsync(10)` and `GetLastRunPerStrategyAsync()`.
  2. In `StrategyLibrary.razor`:
     - Replace `ResultRepo.ListAsync()` with `GetRunSummariesByStrategyAsync(strategyId)` for per-strategy queries.
  3. Ensure `ListAsync()` is no longer called by either page.
- **Done when**:
  - Dashboard and StrategyLibrary use targeted SQLite queries.
  - `ListAsync()` full-collection deserialization is eliminated from both pages.
  - _Requirements: 22.4_

---

### Task P5-22-4: SQLite Index Queries — Integration Tests *

- **Item**: 22
- **Blocked by**: P5-22-2
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.IntegrationTests/V7/SqliteIndexQueryTests.cs` (new)
- **What to do**:
  1. Create `SqliteIndexQueryTests.cs`:
     - Set up a test SQLite DB with known backtest records.
     - Test `GetRecentRunsAsync` returns correct limit and order.
     - Test `GetLastRunPerStrategyAsync` returns one result per strategy type.
     - Test `GetRunSummariesByStrategyAsync` returns only matching strategy results.
- **Done when**:
  - Integration tests verify all three query methods against a real SQLite database.
  - _Requirements: 22.1, 22.2, 22.3_

---

### Task P5-23-1: Add CancellationToken to Blazor Page Initialisation

- **Item**: 23
- **Blocked by**: P2-9-1 (BacktestList must exist)
- **Effort**: M (2–3 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Research/ResearchExplorer.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`
  - `src/TradingResearchEngine.Web/Components/Pages/Backtests/BacktestList.razor`
- **What to do**:
  1. For each affected page:
     - Add `@implements IDisposable`.
     - Add `private readonly CancellationTokenSource _cts = new();`.
     - Pass `_cts.Token` to all async repository and service calls in `OnInitializedAsync`.
     - Add `public void Dispose() { _cts.Cancel(); _cts.Dispose(); }`.
  2. Ensure `OperationCanceledException` is caught silently (no state update after disposal).
- **Done when**:
  - All listed pages implement `IDisposable` with `CancellationTokenSource`.
  - All async calls pass the cancellation token.
  - Use-after-dispose exceptions are prevented.
  - _Requirements: 23.1, 23.2, 23.3, 23.4_

---

### Task P5-24-1: Indicator Registry for Visual Composer

- **Item**: 24
- **Blocked by**: P4-19-1
- **Effort**: S (1–2 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Core/Indicators/IndicatorDescriptor.cs` (new)
  - `src/TradingResearchEngine.Core/Indicators/IndicatorRegistry.cs` (new)
- **What to do**:
  1. Create `IndicatorDescriptor` record: `Name`, `Description`, `Parameters` (`IReadOnlyList<IndicatorParameterDescriptor>`), `OutputType`.
  2. Create `IndicatorParameterDescriptor` record: `Name`, `Type`, `Min`, `Max`, `DefaultValue`.
  3. Create `IndicatorRegistry` static class with `static IReadOnlyList<IndicatorDescriptor> All` property containing one descriptor for each of the 7 indicators (SMA, EMA, ATR, RSI, BollingerBands, ZScore, DonchianChannel).
  4. Each descriptor includes parameter metadata (period ranges, defaults) and output type.
  5. Pattern parallels `DefaultStrategyTemplates.All`.
- **Done when**:
  - `IndicatorRegistry.All` returns 7 descriptors with full metadata.
  - Format parallels existing template discovery pattern.
  - _Requirements: 24.1, 24.2, 24.3, 24.4_

---

### Task P5-24-2: Indicator Registry — Property Test *

- **Item**: 24
- **Blocked by**: P5-24-1, P4-19-1
- **Effort**: XS (30 min)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/IndicatorRegistryProperties.cs` (new)
- **What to do**:
  1. Create `IndicatorRegistryProperties.cs`:
     - **Property 14: IndicatorRegistry completeness** — For every indicator implementation in `TradingResearchEngine.Core.Indicators` namespace, `IndicatorRegistry.All` contains a descriptor with a matching `Name`.
  2. Use reflection to discover all `IIndicator<T>` implementations and verify each has a registry entry.
  3. Tag: `// Feature: v7-implementation-plan, Property 14`
- **Done when**:
  - Property test verifies registry completeness via reflection.
  - **Property 14: IndicatorRegistry completeness — Validates: Requirements 24.1, 24.3**

---

### Task P5-25-1: Signal Composer — Application Layer (Data Model + Translator)

- **Item**: 25
- **Blocked by**: P4-19-1, P4-20-2, P5-24-1
- **Effort**: M (3–4 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Application/Composer/ComposerGraph.cs` (new)
  - `src/TradingResearchEngine.Application/Composer/ComposerToStrategyTranslator.cs` (new)
- **What to do**:
  1. Create `ComposerGraph` record: `GraphId`, `Nodes` (`IReadOnlyList<ComposerNode>`), `Edges` (`IReadOnlyList<ComposerEdge>`).
  2. Create `ComposerNode` record: `NodeId`, `NodeType` (enum: Indicator, Rule, Action), `TypeName`, `Parameters`, `X`, `Y`.
  3. Create `ComposerEdge` record: `EdgeId`, `SourceNodeId`, `SourcePort`, `TargetNodeId`, `TargetPort`.
  4. Create `ComposerToStrategyTranslator`:
     - Inject `IRoslynStrategyCompiler`.
     - `TranslateAsync(ComposerGraph graph, CancellationToken ct)` → `CompilationResult`.
     - `GenerateStrategySource(ComposerGraph graph)`: walk graph topologically, generate C# source that creates indicators, evaluates rules, emits signals.
- **Done when**:
  - Data model supports arbitrary node-based strategy graphs.
  - Translator generates compilable C# from a graph.
  - _Requirements: 25.3, 25.4_

---

### Task P5-25-2: Signal Composer — Web UI Components

- **Item**: 25
- **Blocked by**: P5-25-1
- **Effort**: L (6–8 hours)
- **Files to touch**:
  - `src/TradingResearchEngine.Web/Components/Pages/Strategies/SignalComposer.razor` (new)
  - `src/TradingResearchEngine.Web/Components/Composer/ComposerCanvas.razor` (new)
  - `src/TradingResearchEngine.Web/Components/Composer/IndicatorNode.razor` (new)
  - `src/TradingResearchEngine.Web/Components/Composer/RuleNode.razor` (new)
  - `src/TradingResearchEngine.Web/Components/Composer/ActionNode.razor` (new)
- **What to do**:
  1. Create `SignalComposer.razor` at route `/strategies/composer`:
     - Hosts the canvas, indicator palette (from `IndicatorRegistry.All`), and toolbar.
  2. Create `ComposerCanvas.razor`:
     - SVG-based canvas with drag-and-drop node placement.
     - Edge drawing as SVG `<path>` elements with Bézier curves.
     - Node positioning via CSS transforms.
  3. Create `IndicatorNode.razor`: renders indicator node with parameter inputs.
  4. Create `RuleNode.razor`: renders rule nodes (`CrossAbove`, `CrossBelow`, `GreaterThan`, `LessThan`, `And`, `Or`).
  5. Create `ActionNode.razor`: renders action nodes (`Enter Long`, `Enter Short`, `Exit`, `Size by ATR`).
  6. Graph serialization to JSON via `System.Text.Json`.
  7. "Compile" button invokes `ComposerToStrategyTranslator.TranslateAsync`.
- **Done when**:
  - Visual canvas allows placing, connecting, and configuring nodes.
  - Graph can be compiled into a dynamic strategy.
  - Indicator nodes source from `IndicatorRegistry.All`.
  - _Requirements: 25.1, 25.2, 25.3, 25.5, 25.6_

---

### Task P5-25-3: Signal Composer — Property Test *

- **Item**: 25
- **Blocked by**: P5-25-1
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/ComposerGraphProperties.cs` (new)
- **What to do**:
  1. Create `ComposerGraphProperties.cs`:
     - **Property 15: ComposerGraph JSON round-trip** — For any valid `ComposerGraph` with arbitrary nodes, edges, and parameters, serializing to JSON and deserializing back produces a structurally equivalent graph.
  2. Create FsCheck generators for `ComposerGraph`, `ComposerNode`, `ComposerEdge`.
  3. Tag: `// Feature: v7-implementation-plan, Property 15`
  4. Use `[Property(MaxTest = 100)]`.
- **Done when**:
  - Property test passes for arbitrary graphs.
  - **Property 15: ComposerGraph JSON round-trip — Validates: Requirement 25.4**

---

## Checkpoint 4

- [ ] Ensure all tests pass, ask the user if questions arise.

---

## Final Verification

### Task FINAL-1: Create V7 Test Directory Structure and Verify All Tests Pass

- **Item**: All
- **Blocked by**: All previous tasks
- **Effort**: S (1 hour)
- **Files to touch**:
  - `src/TradingResearchEngine.UnitTests/V7/` (verify directory structure)
  - `src/TradingResearchEngine.IntegrationTests/V7/` (verify directory structure)
- **What to do**:
  1. Verify the V7 test directory structure exists:
     ```
     src/TradingResearchEngine.UnitTests/V7/
       BuilderViewModelProperties.cs
       BuilderNavigationProperties.cs
       ScenarioConfigProperties.cs
       SchemaValidatorProperties.cs
       SchemaValidatorTests.cs
       StrategyIdGeneratorProperties.cs
       StrategyIdGeneratorTests.cs
       RobustnessAdvisoryProperties.cs
       RobustnessAdvisoryServiceTests.cs
       FallbackTranslatorProperties.cs
       FallbackTranslatorTests.cs
       IndicatorProperties.cs
       IndicatorTests.cs
       StrategyCodeSanitiserProperties.cs
       StrategyCodeSanitiserTests.cs
       PromptLoaderProperties.cs
       PromptLoaderTests.cs
       IndicatorRegistryProperties.cs
       ComposerGraphProperties.cs
       StalenessProperties.cs

     src/TradingResearchEngine.IntegrationTests/V7/
       StrategyRepositoryIntegrationTests.cs
       LlmProviderIntegrationTests.cs
       RoslynCompilerIntegrationTests.cs
       SqliteIndexQueryTests.cs
     ```
  2. Run `dotnet test` across the entire solution.
  3. Verify all existing tests still pass (no regressions).
  4. Verify all new V7 property tests pass with `MaxTest = 100`.
  5. Verify the solution builds without warnings (`TreatWarningsAsErrors`).
- **Done when**:
  - All tests pass.
  - No build warnings.
  - V7 test directory structure is complete.
  - _Requirements: All_

---

## Sprint Allocation Table

The following table shows which items can run in parallel within each phase, organized into parallel tracks for team allocation.

| Sprint | Track A | Track B | Track C | Track D |
|--------|---------|---------|---------|---------|
| **Phase 1** | Item 1 (Fork) + Item 2 (Import) | Item 3 (GoToStep) | Item 4 (N+1 Queries) | Item 5 (XML Doc) + Item 6 (git file) |
| **Phase 2** | Item 7 (NavMenu) | Item 8 (Explorer Actions) + Item 9 (Backtests List) | Item 10 (Stale Status) | — |
| **Phase 3** | Item 11 (Schema Validation) + Item 14 (ID Truncation) | Item 12 (Quick Sanity) + Item 13 (Replace dynamic) | Item 15 (Equity Chart) | Item 16 (Robustness Svc) |
| **Phase 4** | Item 21 (Prompts) → Item 17 (LLM) → Item 18 (Step 0) | Item 19 (Indicators) → Item 20 (Roslyn) | — | — |
| **Phase 5** | Item 22 (SQLite Queries) + Item 23 (CancellationToken) | Item 24 (Indicator Registry) → Item 25 (Signal Composer) | — | — |

### Critical Path

```
Phase 4: Item 21 → Item 17 → Item 18 (longest sequential chain: ~10 hours)
Phase 4: Item 19 → Item 20 (parallel track: ~10 hours)
Phase 5: Item 19 → Item 24 → Item 25 (cross-phase dependency: requires Phase 4 completion)
```

### Parallel Execution Notes

- **Phase 1**: All 6 items are fully independent — maximum parallelism.
- **Phase 2**: All 4 items are fully independent — maximum parallelism.
- **Phase 3**: All 6 items are independent (Item 15 has a soft dependency on Item 9's BacktestDetail page existing, but the chart component itself can be built independently).
- **Phase 4**: Two parallel tracks: (21→17→18) and (19→20). Item 19 is independent of the LLM chain.
- **Phase 5**: Item 22 and 23 are independent. Item 24 depends on Item 19 (Phase 4). Item 25 depends on Items 19, 20, and 24.

### Security-Sensitive Tasks (⚠️)

- **P4-17-2**: LLM provider implementations — API key handling, feature flag enforcement.
- **P4-20-2**: Roslyn compiler — code sanitisation, restricted AssemblyLoadContext, audit logging.
- **P4-20-3**: Roslyn compiler tests — verify security controls work correctly.

---

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP delivery.
- Each task references specific requirements for traceability.
- Checkpoints ensure incremental validation between phases.
- Property tests validate universal correctness properties from the design document.
- Unit tests validate specific examples and edge cases.
- All code uses .NET 8 / C# 12 idioms, nullable reference types, and `IOptions<T>` configuration.
- No new NuGet packages beyond `Microsoft.CodeAnalysis.CSharp` (Item 20) are required.
