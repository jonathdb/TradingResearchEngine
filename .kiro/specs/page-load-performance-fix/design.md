# Page Load Performance Fix — Bugfix Design

## Overview

Multiple pages across the TradingResearchEngine web application suffer from severe performance degradation caused by unfiltered full-dataset loads, N+1 query patterns, blocking startup initialization, and sequential file I/O. The fix replaces these patterns with targeted indexed queries (already available on `IBacktestResultRepository`), batch operations (already available on `IStrategyRepository`), non-blocking startup, and parallel I/O — all without changing the data correctness guarantees or the underlying JSON + SQLite persistence model.

## Glossary

- **Bug_Condition (C)**: A page load request that triggers expensive unfiltered data access — calling `ListAsync()` when a targeted query exists, or looping per-entity when a batch method exists
- **Property (P)**: The desired behavior — pages use indexed/filtered/paginated queries completing data fetch in under 2 seconds with zero full-dataset loads
- **Preservation**: Data correctness, ordering, and display content remain identical after the fix
- **N+1 Query**: A pattern where one query fetches N entities, then N additional queries fetch related data per entity
- **`SqliteIndexRepository`**: The `IBacktestResultRepository` implementation in `Infrastructure/Persistence/` that provides O(log n) indexed lookups over JSON files
- **`JsonStudyRepository`**: The `IStudyRepository` implementation that reads all JSON files sequentially
- **`JsonStrategyRepository`**: The `IStrategyRepository` implementation with batch methods (`GetVersionCountsAsync`, `ListAllVersionsAsync`)
- **`ListAsync()`**: The full-collection load method that deserializes every JSON file — the root cause of performance issues
- **`GetLastRunPerStrategyAsync()`**: SQLite-indexed query returning one result per strategy type via `GROUP BY`
- **`ListRecentAsync(count)`**: SQLite-indexed query returning the N most recent results via `LIMIT`
- **`ListByVersionAsync(versionId)`**: SQLite-indexed query returning results for a specific version
- **`ListPagedAsync(...)`**: Paginated query with optional filters (currently loads all then filters in-memory — needs SQLite optimization)

## Bug Details

### Bug Condition

The bug manifests when any page loads and triggers repository calls that deserialize the entire JSON file collection into memory. The affected pages call `ListAsync()` on `IBacktestResultRepository` or `IStudyRepository` when targeted indexed queries already exist but are not being used. Additionally, the Dashboard iterates per-strategy calling `GetVersionsAsync()` when batch methods (`GetVersionCountsAsync`) are available.

**Formal Specification:**
```
FUNCTION isBugCondition(input)
  INPUT: input of type PageLoadRequest
  OUTPUT: boolean
  
  RETURN input.Page IN {Dashboard, StrategyLibrary, StrategyDetail, RobustnessHub,
                        ResearchExplorer, BacktestHistory, BacktestList, ResultPicker, MultiResultPicker}
    AND (input.TotalBacktestResults > 10 OR input.TotalStrategies > 3 OR input.TotalStudies > 10)
    AND (input.UsesListAsync = true OR input.UsesPerEntityLoop = true)
END FUNCTION
```

### Examples

- **Dashboard**: With 50 backtest results and 5 strategies, `OnInitializedAsync` calls `ResultRepo.ListAsync()` (deserializes 50 JSON files), then calls `StrategyRepo.GetVersionsAsync()` 5 times (reads version directories per strategy). Expected: uses `GetLastRunPerStrategyAsync()` + `ListRecentAsync(10)` + `GetVersionCountsAsync(ids)` — 3 indexed calls total.
- **Strategy Library**: With 50 results, calls `ResultRepo.ListAsync()` to build `_lastRunMap`. Expected: uses `GetLastRunPerStrategyAsync()` — 1 indexed call.
- **Strategy Detail**: With 50 results, calls `ResultRepo.ListAsync()` then filters to 3 runs for the selected version. Expected: uses `ListByVersionAsync(versionId)` — 1 indexed call returning only 3 results.
- **ResultPicker**: With 50 results, loads all into a dropdown. Expected: uses `ListRecentAsync(50)` — 1 indexed call with bounded output.

## Expected Behavior

### Preservation Requirements

**Unchanged Behaviors:**
- All data written to JSON files and SQLite index remains identical in format and content
- Dashboard displays the same strategies, Sharpe values, robustness warnings, and suggested actions
- Strategy Library cards show the same version counts, last-run metrics, staleness status, and robustness warnings
- Strategy Detail shows all runs for the selected version with correct metrics and ordering
- Research Explorer shows correct study-to-strategy associations with proper filtering
- Robustness Hub shows the same warnings with correct severity and strategy association
- ResultPicker/MultiResultPicker show results with correct metadata for user selection
- `SaveAsync` continues to persist JSON + update SQLite index atomically
- Ordering of results (by RunDate descending) is preserved in all views

**Scope:**
All inputs that do NOT involve page load data fetching should be completely unaffected by this fix. This includes:
- Saving new backtest results, strategies, or studies
- Running backtests (engine execution)
- Prop firm evaluation logic
- Strategy builder wizard flow
- Export functionality
- All non-page-load API operations

## Hypothesized Root Cause

Based on the code analysis, the root causes are:

1. **Unused Indexed Queries**: `IBacktestResultRepository` already exposes `GetLastRunPerStrategyAsync()`, `ListRecentAsync()`, `ListByVersionAsync()`, and `GetRecentRunsAsync()` — but pages still call `ListAsync()` which deserializes every JSON file via the SQLite index file paths.

2. **`ListPagedAsync` Implementation Deficiency**: The current `ListPagedAsync` on `SqliteIndexRepository` calls `ListAsync()` internally and then filters in-memory, negating the benefit of pagination. It should use SQL `LIMIT/OFFSET` with `WHERE` clauses directly.

3. **N+1 Loop in Dashboard**: The Dashboard's `foreach (var strategy in _strategies)` loop calls `GetVersionsAsync(strategy.StrategyId)` per strategy to compute checklists. The `GetVersionCountsAsync` batch method exists but isn't used for checklist computation; the checklist service itself requires a `StrategyVersionId`, forcing per-strategy version lookups.

4. **Blocking Startup Initialization**: `Program.cs` awaits `sqliteRepo.InitializeAsync()` which scans and deserializes every JSON result file sequentially. This blocks the first HTTP request.

5. **Sequential File I/O in JSON Repositories**: `JsonStudyRepository.ListAsync()` and `JsonStrategyRepository.ListAsync()` read files one-by-one in a `foreach` loop. With 100+ files, this compounds disk latency.

6. **`ResultRepo.ListAsync()` Called Multiple Times Per Page**: The Dashboard calls it once for `_recentRuns` (via `ListPagedAsync` which internally calls `ListAsync`) and again directly for `_lastRunMap` and `_warningRuns` — two full-collection loads per page render.

## Correctness Properties

Property 1: Bug Condition - Targeted Queries Return Equivalent Data

_For any_ set of backtest results where the bug condition holds (pages that previously called `ListAsync()` and filtered client-side), the targeted indexed query (`GetLastRunPerStrategyAsync`, `ListByVersionAsync`, `ListRecentAsync`) SHALL return the same data subset that the original `ListAsync()` + client-side filter produced.

**Validates: Requirements 2.1, 2.3, 2.4, 2.8, 2.10, 2.11**

Property 2: Preservation - Non-Targeted Query Behavior Unchanged

_For any_ input where the bug condition does NOT hold (save operations, engine runs, small datasets below threshold), the fixed code SHALL produce exactly the same behavior as the original code, preserving all data persistence, query results, and display correctness.

**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9**

## Fix Implementation

### Changes Required

Assuming our root cause analysis is correct:

**File**: `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`

**Function**: `LoadDashboardData()`

**Specific Changes**:
1. **Replace `ResultRepo.ListAsync()` for `_lastRunMap`**: Use `ResultRepo.GetLastRunPerStrategyAsync()` which returns one result per strategy type via SQLite `GROUP BY` — eliminates full-collection load.
2. **Replace `ResultRepo.ListAsync()` for `_warningRuns`**: Use `ResultRepo.GetRecentRunsAsync(10)` (already limited to 10 completed runs for warning checks) instead of loading all and taking 10.
3. **Replace `ResultRepo.ListAsync()` for `_recentSharpes`**: Derive from the already-loaded `_lastRunMap` values or use `GetRecentRunsAsync(5)`.
4. **Batch checklist computation**: Use `StrategyRepo.ListAllVersionsAsync()` to get all versions in one call, then compute checklists from the pre-loaded version map instead of calling `GetVersionsAsync` per strategy.

**File**: `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`

**Function**: `OnInitializedAsync()`

**Specific Changes**:
1. **Replace `ResultRepo.ListAsync()` for `_lastRunMap`**: Use `ResultRepo.GetLastRunPerStrategyAsync()` — single indexed query.

**File**: `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyDetail.razor`

**Function**: `LoadVersionData()`

**Specific Changes**:
1. **Replace `ResultRepo.ListAsync()` for `_versionRuns`**: Use `ResultRepo.ListByVersionAsync(_selectedVersion.StrategyVersionId)` — single indexed query returning only runs for the selected version.

**File**: `src/TradingResearchEngine.Web/Components/Pages/Research/RobustnessHub.razor`

**Function**: `OnInitializedAsync()`

**Specific Changes**:
1. **Replace `ResultRepo.ListAsync()`**: Use `ResultRepo.GetLastRunPerStrategyAsync()` — returns one result per strategy, which is exactly what the page needs for warning computation.

**File**: `src/TradingResearchEngine.Web/Components/Pages/Research/ResearchExplorer.razor`

**Function**: `OnInitializedAsync()`

**Specific Changes**:
1. **Replace `StudyRepo.ListAsync()`**: Use `StudyRepo.ListPagedAsync(page, pageSize)` with server-side pagination. Add pagination controls to the UI.
2. **Keep `StrategyRepo.ListAllVersionsAsync()`**: This is already a batch call (acceptable).

**File**: `src/TradingResearchEngine.Web/Components/Shared/ResultPicker.razor`

**Function**: `OnInitializedAsync()`

**Specific Changes**:
1. **Replace `ResultRepo.ListAsync()`**: Use `ResultRepo.ListRecentAsync(50)` — bounded to 50 most recent results for dropdown selection.

**File**: `src/TradingResearchEngine.Web/Components/Shared/MultiResultPicker.razor`

**Function**: `OnInitializedAsync()`

**Specific Changes**:
1. **Replace `ResultRepo.ListAsync()`**: Use `ResultRepo.ListRecentAsync(50)` via `IBacktestResultRepository` (requires changing the injected type from `IRepository<BacktestResult>` to `IBacktestResultRepository`).

**File**: `src/TradingResearchEngine.Infrastructure/Persistence/SqliteIndexRepository.cs`

**Function**: `InitializeAsync()`, `ListPagedAsync()`

**Specific Changes**:
1. **Non-blocking startup**: Change `Program.cs` to fire-and-forget the `InitializeAsync()` call (wrap in `Task.Run` without await, or use `IHostedService` background initialization).
2. **Fix `ListPagedAsync`**: Replace the internal `ListAsync()` call with a proper SQL query using `LIMIT`, `OFFSET`, and `WHERE` clauses on the SQLite index, with a `COUNT(*)` for total.
3. **Add `StrategyType` column to index**: The current index stores `StrategyId` (which maps to `ScenarioConfig.StrategyType`). Ensure `GetLastRunPerStrategyAsync` groups by the correct column for strategy-type-based lookups used by Dashboard and StrategyLibrary.

**File**: `src/TradingResearchEngine.Infrastructure/Persistence/JsonStudyRepository.cs`

**Function**: `ListAsync()`

**Specific Changes**:
1. **Parallel I/O**: Replace sequential `foreach` file reads with `Parallel.ForEachAsync` or `Task.WhenAll` with bounded concurrency (e.g., `SemaphoreSlim` with max 8 concurrent reads).

**File**: `src/TradingResearchEngine.Infrastructure/Persistence/JsonStrategyRepository.cs`

**Function**: `ListAsync()`, `ListAllVersionsAsync()`

**Specific Changes**:
1. **Parallel I/O**: Replace sequential `foreach` file reads with parallel I/O using bounded concurrency.

**File**: `src/TradingResearchEngine.Web/Program.cs`

**Function**: Startup sequence

**Specific Changes**:
1. **Non-blocking SQLite init**: Replace `await sqliteRepo.InitializeAsync()` with a fire-and-forget background task that logs completion/failure without blocking the first request.

## Testing Strategy

### Validation Approach

The testing strategy follows a two-phase approach: first, surface counterexamples that demonstrate the bug on unfixed code, then verify the fix works correctly and preserves existing behavior.

### Exploratory Bug Condition Checking

**Goal**: Surface counterexamples that demonstrate the bug BEFORE implementing the fix. Confirm or refute the root cause analysis. If we refute, we will need to re-hypothesize.

**Test Plan**: Write integration tests that instrument repository calls during page initialization to count how many times `ListAsync()` is called and how many JSON files are deserialized. Run these tests on the UNFIXED code to observe the N+1 patterns and full-collection loads.

**Test Cases**:
1. **Dashboard Full-Load Test**: Seed 20 results and 5 strategies, load Dashboard, assert `ListAsync()` is called ≥2 times (will fail on unfixed code — demonstrates redundant loads)
2. **Dashboard N+1 Test**: Seed 5 strategies with versions, load Dashboard, count `GetVersionsAsync` calls — expect 5 individual calls (will fail on unfixed code — demonstrates N+1)
3. **Strategy Library Full-Load Test**: Seed 20 results, load StrategyLibrary, assert all 20 results are deserialized for a page that only needs 5 (will fail on unfixed code)
4. **Strategy Detail Full-Load Test**: Seed 20 results across 4 versions, load StrategyDetail for one version, assert all 20 are deserialized when only 5 are needed (will fail on unfixed code)

**Expected Counterexamples**:
- `ListAsync()` called multiple times per page load, each deserializing the full JSON collection
- Per-strategy loops generating N file system reads where 1 batch read suffices
- Possible causes: pages written before indexed query methods were added to the repository interface

### Fix Checking

**Goal**: Verify that for all inputs where the bug condition holds, the fixed function produces the expected behavior.

**Pseudocode:**
```
FOR ALL input WHERE isBugCondition(input) DO
  result := LoadPage_fixed(input)
  ASSERT result.FullDatasetLoadsCount = 0
  ASSERT result.UsesIndexedQueries = true
  ASSERT result.DisplayedData = LoadPage_original(input).DisplayedData
END FOR
```

### Preservation Checking

**Goal**: Verify that for all inputs where the bug condition does NOT hold, the fixed function produces the same result as the original function.

**Pseudocode:**
```
FOR ALL input WHERE NOT isBugCondition(input) DO
  ASSERT LoadPage_original(input).DisplayedData = LoadPage_fixed(input).DisplayedData
END FOR
```

**Testing Approach**: Property-based testing is recommended for preservation checking because:
- It generates many random backtest result sets and verifies that indexed queries return the same data as full-load + filter
- It catches edge cases (empty collections, single result, duplicate strategy types) that manual tests might miss
- It provides strong guarantees that the query optimization doesn't alter visible data

**Test Plan**: Observe behavior on UNFIXED code first for query results, then write property-based tests capturing that the optimized queries produce identical output.

**Test Cases**:
1. **GetLastRunPerStrategyAsync Equivalence**: Generate random result sets, verify `GetLastRunPerStrategyAsync()` returns the same map as `ListAsync()` grouped by strategy type taking the latest
2. **ListByVersionAsync Equivalence**: Generate random result sets with version IDs, verify `ListByVersionAsync(id)` returns the same subset as `ListAsync()` filtered by version
3. **ListRecentAsync Equivalence**: Generate random result sets, verify `ListRecentAsync(N)` returns the same top-N as `ListAsync()` ordered by date and limited
4. **ListPagedAsync Equivalence**: Generate random result sets, verify paginated SQL query returns same page as in-memory pagination

### Unit Tests

- Test `SqliteIndexRepository.ListPagedAsync` with SQL-based pagination returns correct page and total count
- Test `SqliteIndexRepository.GetLastRunPerStrategyAsync` returns exactly one result per strategy type (the most recent)
- Test `SqliteIndexRepository.ListByVersionAsync` returns only results matching the version ID
- Test `SqliteIndexRepository.ListRecentAsync` respects the count limit and ordering
- Test parallel I/O in `JsonStudyRepository.ListAsync` returns same results as sequential (order-independent)
- Test parallel I/O in `JsonStrategyRepository.ListAsync` returns same results as sequential
- Test non-blocking startup doesn't prevent page serving

### Property-Based Tests

- Generate random `BacktestResult` collections and verify `GetLastRunPerStrategyAsync` equivalence with full-load + group-by
- Generate random `BacktestResult` collections and verify `ListByVersionAsync` equivalence with full-load + filter
- Generate random `BacktestResult` collections and verify `ListRecentAsync(N)` equivalence with full-load + order + take
- Generate random `StudyRecord` collections and verify `ListPagedAsync` equivalence with full-load + skip + take
- Generate random file sets and verify parallel read produces same content set as sequential read

### Integration Tests

- Full page load test: seed data, render Dashboard via `WebApplicationFactory`, verify correct data displayed and no `ListAsync()` calls on `IBacktestResultRepository`
- Startup timing test: verify application starts and serves first request within 2 seconds regardless of JSON file count
- Strategy Detail version switch: verify only version-scoped results are loaded after switching versions
- ResultPicker bounded load: verify at most 50 results are loaded regardless of total count
