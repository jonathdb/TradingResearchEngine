# Implementation Plan

- [ ] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Full Dataset Load on Page Initialization
  - **CRITICAL**: This test MUST FAIL on unfixed code - failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior - it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate pages call `ListAsync()` loading all results when targeted queries should be used
  - **Scoped PBT Approach**: Seed the SQLite index with 20+ backtest results across 5 strategies, then instrument page data-loading methods to verify they do NOT call `ListAsync()` on `IBacktestResultRepository`
  - Write an integration test using `WebApplicationFactory<Program>` that:
    - Seeds 20 backtest results across 5 strategies with versions
    - Wraps `IBacktestResultRepository` with a counting decorator that tracks `ListAsync()` calls
    - Triggers Dashboard `LoadDashboardData()` logic
    - Asserts `ListAsync()` call count is 0 (expected behavior from design)
  - Property: For all page loads where `isBugCondition(input)` holds (TotalBacktestResults > 10, TotalStrategies > 3), the number of `ListAsync()` calls on `IBacktestResultRepository` MUST be 0
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (ListAsync is called 2+ times on Dashboard alone — this proves the bug exists)
  - Document counterexamples: e.g., "Dashboard.LoadDashboardData calls ListAsync() 2 times, StrategyLibrary calls ListAsync() 1 time, loading all 20 results each time"
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.8, 1.10, 1.11_

- [ ] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Indexed Query Equivalence with Full Load
  - **IMPORTANT**: Follow observation-first methodology
  - **IMPORTANT**: Write these tests BEFORE implementing the fix
  - Observe behavior on UNFIXED code: seed 30 random backtest results, call `ListAsync()` and derive expected outputs via client-side grouping/filtering
  - Write FsCheck property-based tests in `src/TradingResearchEngine.UnitTests/` that verify:
    - `GetLastRunPerStrategyAsync()` returns the same map as `ListAsync().GroupBy(StrategyType).Select(latest)` for all generated result sets
    - `ListByVersionAsync(versionId)` returns the same subset as `ListAsync().Where(r => r.StrategyVersionId == versionId)` for all generated version IDs
    - `ListRecentAsync(N)` returns the same top-N as `ListAsync().OrderByDescending(RunDate).Take(N)` for all N in [1..50]
    - `GetRecentRunsAsync(limit)` returns the same results as `ListAsync().OrderByDescending(RunDate).Take(limit)` for all limits
  - These tests exercise the SQLite indexed queries against the full-load baseline to confirm equivalence
  - Property-based testing generates many random result sets for stronger guarantees that optimized queries don't alter visible data
  - Run tests on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (indexed queries already return correct data — the bug is that pages don't USE them)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7, 3.8, 3.9_

- [ ] 3. Fix page load performance issues

  - [ ] 3.1 Fix SqliteIndexRepository.ListPagedAsync to use SQL LIMIT/OFFSET
    - Replace internal `ListAsync()` call with proper SQL query using `LIMIT`, `OFFSET`, and `WHERE` clauses
    - Add `WHERE StrategyId = @strategyType` filter when `strategyTypeFilter` is provided
    - Add `WHERE Status = @status` filter when `statusFilter` is provided
    - Use `SELECT COUNT(*) FROM BacktestResultIndex WHERE ...` for total count
    - Use `SELECT FilePath FROM BacktestResultIndex WHERE ... ORDER BY RunDate DESC LIMIT @pageSize OFFSET @offset` for page items
    - _Bug_Condition: isBugCondition(input) where ListPagedAsync internally calls ListAsync() loading all results_
    - _Expected_Behavior: ListPagedAsync uses SQL pagination, only deserializes pageSize JSON files_
    - _Preservation: Query results remain identical — same items, same ordering, same total count_
    - _Requirements: 2.1, 2.10_

  - [ ] 3.2 Make SqliteIndexRepository.InitializeAsync non-blocking in Program.cs
    - In `src/TradingResearchEngine.Web/Program.cs`, replace `await sqliteRepo.InitializeAsync()` with a fire-and-forget background task
    - Use `_ = Task.Run(async () => { try { await sqliteRepo.InitializeAsync(); } catch (Exception ex) { logger.LogWarning(ex, "..."); } })`
    - Ensure the application starts serving requests immediately without waiting for index rebuild
    - _Bug_Condition: isBugCondition(input) where InitializeAsync blocks first page load_
    - _Expected_Behavior: Application starts and serves first request without waiting for index rebuild_
    - _Preservation: SQLite index is still built correctly (eventual consistency during startup)_
    - _Requirements: 2.6_

  - [ ] 3.3 Fix Dashboard.razor to use targeted queries
    - Replace `ResultRepo.ListAsync()` for `_lastRunMap` with `ResultRepo.GetLastRunPerStrategyAsync()`
    - Replace `ResultRepo.ListAsync()` for `_warningRuns` with `ResultRepo.GetRecentRunsAsync(10)`
    - Derive `_recentSharpes` from the already-loaded `_lastRunMap` values instead of a separate `ListAsync()` call
    - Replace per-strategy `GetVersionsAsync()` loop with `StrategyRepo.ListAllVersionsAsync()` batch call, then compute checklists from pre-loaded version map
    - Remove the redundant `var allRuns = await ResultRepo.ListAsync()` call entirely
    - _Bug_Condition: isBugCondition(input) where Dashboard calls ListAsync() 2+ times and GetVersionsAsync N times_
    - _Expected_Behavior: Dashboard uses GetLastRunPerStrategyAsync + GetRecentRunsAsync(10) + ListAllVersionsAsync — 0 ListAsync calls_
    - _Preservation: Dashboard displays same strategies, Sharpe values, robustness warnings, and suggested actions_
    - _Requirements: 2.1, 2.2, 1.1, 1.2_

  - [ ] 3.4 Fix StrategyLibrary.razor to use targeted queries
    - Replace `ResultRepo.ListAsync()` for `_lastRunMap` with `ResultRepo.GetLastRunPerStrategyAsync()`
    - Change injected type from `IRepository<BacktestResult>` to `IBacktestResultRepository`
    - Adapt `_lastRunMap` to use the dictionary keyed by StrategyType returned from `GetLastRunPerStrategyAsync()`
    - _Bug_Condition: isBugCondition(input) where StrategyLibrary calls ListAsync() loading all results_
    - _Expected_Behavior: StrategyLibrary uses GetLastRunPerStrategyAsync — single indexed query_
    - _Preservation: Strategy cards show same version counts, last-run metrics, staleness status, and robustness warnings_
    - _Requirements: 2.3, 1.3_

  - [ ] 3.5 Fix StrategyDetail.razor to use targeted queries
    - Replace `ResultRepo.ListAsync()` in `LoadVersionData()` with `ResultRepo.ListByVersionAsync(_selectedVersion.StrategyVersionId)`
    - Change injected type from `IRepository<BacktestResult>` to `IBacktestResultRepository`
    - Remove client-side `.Where(r => r.StrategyVersionId == ...)` filter since `ListByVersionAsync` already filters
    - _Bug_Condition: isBugCondition(input) where StrategyDetail calls ListAsync() loading all results then filters to one version_
    - _Expected_Behavior: StrategyDetail uses ListByVersionAsync(versionId) — single indexed query returning only version runs_
    - _Preservation: Strategy Detail shows all runs for the selected version with correct metrics and ordering_
    - _Requirements: 2.4, 2.5, 1.4, 1.5_

  - [ ] 3.6 Fix RobustnessHub.razor to use targeted queries
    - Replace `ResultRepo.ListAsync()` with `ResultRepo.GetLastRunPerStrategyAsync()`
    - Adapt warning computation to use the dictionary from `GetLastRunPerStrategyAsync()` instead of manual GroupBy
    - _Bug_Condition: isBugCondition(input) where RobustnessHub calls ListAsync() loading all results_
    - _Expected_Behavior: RobustnessHub uses GetLastRunPerStrategyAsync — single indexed query_
    - _Preservation: Robustness Hub shows same warnings with correct severity, strategy association, and counts_
    - _Requirements: 2.8, 1.8_

  - [ ] 3.7 Fix ResearchExplorer.razor to use paginated study queries
    - Replace `StudyRepo.ListAsync()` with `StudyRepo.ListPagedAsync(page, pageSize)` with server-side filtering
    - Add pagination state (`_currentPage`, `_pageSize`) and pagination controls to the UI
    - Keep `StrategyRepo.ListAllVersionsAsync()` as-is (already a batch call)
    - _Bug_Condition: isBugCondition(input) where ResearchExplorer calls StudyRepo.ListAsync() loading all studies_
    - _Expected_Behavior: ResearchExplorer uses paginated queries with server-side filtering_
    - _Preservation: Research Explorer shows correct study-to-strategy associations, filtering, and ordering_
    - _Requirements: 2.9, 1.9_

  - [ ] 3.8 Fix ResultPicker.razor to use bounded query
    - Replace `ResultRepo.ListAsync()` with `ResultRepo.ListRecentAsync(50)`
    - Results are already ordered by RunDate DESC from the indexed query
    - _Bug_Condition: isBugCondition(input) where ResultPicker calls ListAsync() loading all results for a dropdown_
    - _Expected_Behavior: ResultPicker uses ListRecentAsync(50) — bounded to 50 most recent results_
    - _Preservation: ResultPicker shows results with correct metadata for user selection_
    - _Requirements: 2.11, 1.11_

  - [ ] 3.9 Fix MultiResultPicker.razor to use bounded query
    - Change injected type from `IRepository<BacktestResult>` to `IBacktestResultRepository`
    - Replace `ResultRepo.ListAsync()` with `ResultRepo.ListRecentAsync(50)`
    - _Bug_Condition: isBugCondition(input) where MultiResultPicker calls ListAsync() loading all results_
    - _Expected_Behavior: MultiResultPicker uses ListRecentAsync(50) — bounded to 50 most recent results_
    - _Preservation: MultiResultPicker shows results with correct metadata for user selection_
    - _Requirements: 2.11, 1.11_

  - [ ] 3.10 Add parallel I/O to JsonStudyRepository.ListAsync
    - Replace sequential `foreach` file reads with `Parallel.ForEachAsync` or `Task.WhenAll` with bounded concurrency
    - Use `SemaphoreSlim` with max 8 concurrent reads to avoid file handle exhaustion
    - Maintain the same ordering (OrderByDescending CreatedAt) after parallel read
    - _Bug_Condition: isBugCondition(input) where sequential file reads compound latency_
    - _Expected_Behavior: Parallel I/O reduces total latency for ListAsync when it is still called_
    - _Preservation: Returns same results in same order as sequential implementation_
    - _Requirements: 2.7, 1.7_

  - [ ] 3.11 Add parallel I/O to JsonStrategyRepository.ListAsync and ListAllVersionsAsync
    - Replace sequential `foreach` file reads in `ListAsync()` with parallel I/O using bounded concurrency (SemaphoreSlim, max 8)
    - Replace sequential `foreach` file reads in `ListAllVersionsAsync()` with parallel I/O using bounded concurrency
    - Maintain the same ordering after parallel read
    - _Bug_Condition: isBugCondition(input) where sequential file reads compound latency_
    - _Expected_Behavior: Parallel I/O reduces total latency for strategy file reads_
    - _Preservation: Returns same results in same order as sequential implementation_
    - _Requirements: 2.7, 1.7_

  - [ ] 3.12 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Full Dataset Load Eliminated
    - **IMPORTANT**: Re-run the SAME test from task 1 - do NOT write a new test
    - The test from task 1 encodes the expected behavior (zero ListAsync calls on page load)
    - When this test passes, it confirms pages now use targeted indexed queries
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed — pages no longer call ListAsync)
    - _Requirements: 2.1, 2.3, 2.4, 2.8, 2.10, 2.11_

  - [ ] 3.13 Verify preservation tests still pass
    - **Property 2: Preservation** - Indexed Query Equivalence with Full Load
    - **IMPORTANT**: Re-run the SAME tests from task 2 - do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions — indexed queries still return correct data)
    - Confirm all tests still pass after fix (no regressions)

- [ ] 4. Checkpoint - Ensure all tests pass
  - Run full test suite: `dotnet test` from solution root
  - Verify all property-based tests pass (exploration test + preservation tests)
  - Verify all existing unit tests still pass (no regressions in V2 regression tests, research workflows, etc.)
  - Verify application builds without warnings
  - Ensure all tests pass, ask the user if questions arise.
