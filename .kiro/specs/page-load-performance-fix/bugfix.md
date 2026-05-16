# Bugfix Requirements Document

## Introduction

After the research-platform-v9 spec implementation, multiple pages across the application exhibit severe performance degradation. Page load times exceed acceptable thresholds (>2 seconds) due to N+1 query patterns, full dataset loads into memory, blocking startup initialization, and sequential file I/O during page render. The affected pages include Dashboard, Strategy Library, Strategy Detail, Robustness Hub, Research Explorer, Backtest History/List, and shared picker components. This bug impacts every user on every page load, making the application feel unresponsive.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN the Dashboard loads THEN the system calls `ResultRepo.ListAsync()` to load ALL backtest results into memory (once for paginated display and again for `_lastRunMap`), causing redundant full-dataset reads

1.2 WHEN the Dashboard loads THEN the system calls `StrategyRepo.GetVersionsAsync()` in a loop for each strategy to compute checklists and suggested actions, creating an N+1 query pattern that reads all version JSON files per strategy

1.3 WHEN the Strategy Library page loads THEN the system calls `ResultRepo.ListAsync()` to load ALL backtest results into memory and then groups/filters client-side to build `_lastRunMap`

1.4 WHEN the Strategy Detail page loads or a version is switched THEN the system calls `ResultRepo.ListAsync()` to load ALL backtest results into memory and filters to the selected version client-side

1.5 WHEN the Strategy Detail page loads or a version is switched THEN the system calls `StudyRepo.ListByVersionAsync()` which internally calls `ListAsync()` loading ALL study JSON files and filtering in-memory

1.6 WHEN the application starts THEN `SqliteIndexRepository.InitializeAsync()` synchronously scans and deserializes every JSON backtest result file to rebuild the SQLite index, blocking the first page load

1.7 WHEN any JSON repository `ListAsync()` method executes THEN the system reads JSON files sequentially one-by-one rather than in parallel, compounding latency with file count

1.8 WHEN the Robustness Hub page loads THEN the system calls `ResultRepo.ListAsync()` to load ALL backtest results into memory, then filters to latest completed run per strategy type client-side

1.9 WHEN the Research Explorer page loads THEN the system calls `StudyRepo.ListAsync()` and `StrategyRepo.ListAllVersionsAsync()` to load ALL studies and ALL versions into memory for client-side joining

1.10 WHEN the Backtest History or BacktestList pages load THEN the system calls `ResultRepo.ListAsync()` to load ALL backtest results into memory for display

1.11 WHEN the ResultPicker or MultiResultPicker shared components render THEN the system calls `ResultRepo.ListAsync()` to load ALL backtest results into memory for a dropdown selection

### Expected Behavior (Correct)

2.1 WHEN the Dashboard loads THEN the system SHALL use targeted indexed queries (`ListRecentAsync`, `GetLastRunPerStrategyAsync`) instead of loading all results, completing data fetch in under 2 seconds

2.2 WHEN the Dashboard loads THEN the system SHALL use a batch operation (`GetVersionCountsAsync` or `ListAllVersionsAsync`) to retrieve version data for all strategies in a single pass, eliminating the N+1 loop

2.3 WHEN the Strategy Library page loads THEN the system SHALL use `GetLastRunPerStrategyAsync()` or equivalent indexed query to retrieve only the last run per strategy type without loading all results

2.4 WHEN the Strategy Detail page loads or a version is switched THEN the system SHALL use `ListByVersionAsync()` on the backtest result repository (SQLite-indexed) to load only runs for the selected version

2.5 WHEN the Strategy Detail page loads or a version is switched THEN the system SHALL use a filtered study query (direct file lookup or indexed query by version ID) instead of loading all studies

2.6 WHEN the application starts THEN the SQLite index initialization SHALL be non-blocking (fire-and-forget or background task) so that the first page load is not delayed by index rebuilding

2.7 WHEN JSON repository methods read multiple files THEN the system SHALL use parallel I/O (e.g., `Task.WhenAll` with bounded concurrency) to reduce total latency

2.8 WHEN the Robustness Hub page loads THEN the system SHALL use `GetLastRunPerStrategyAsync()` to retrieve only the latest run per strategy without loading all results

2.9 WHEN the Research Explorer page loads THEN the system SHALL use paginated study queries (`ListPagedAsync`) with server-side filtering instead of loading all studies into memory

2.10 WHEN the Backtest History or BacktestList pages load THEN the system SHALL use paginated queries (`ListPagedAsync`) or `ListRecentAsync` with a reasonable limit instead of loading all results

2.11 WHEN the ResultPicker or MultiResultPicker components render THEN the system SHALL use `ListRecentAsync` with a bounded limit (e.g., 50 most recent) or accept pre-filtered results instead of loading all results

### Unchanged Behavior (Regression Prevention)

3.1 WHEN a new backtest result is saved THEN the system SHALL CONTINUE TO persist it as a JSON file and update the SQLite index atomically

3.2 WHEN the Dashboard displays recent runs THEN the system SHALL CONTINUE TO show the same data (correct strategies, correct Sharpe values, correct robustness warnings) as before the fix

3.3 WHEN the Strategy Detail page displays version runs THEN the system SHALL CONTINUE TO show all runs for the selected version with correct metrics and ordering

3.4 WHEN the Strategy Library page displays strategy cards THEN the system SHALL CONTINUE TO show correct version counts, last-run metrics, staleness status, and robustness warnings

3.5 WHEN `StudyRepo.ListByVersionAsync()` is called THEN the system SHALL CONTINUE TO return only studies matching the specified version ID with correct data

3.6 WHEN the SQLite index is used for queries THEN the system SHALL CONTINUE TO return results consistent with the underlying JSON files (eventual consistency acceptable during startup rebuild)

3.7 WHEN the Robustness Hub displays warnings THEN the system SHALL CONTINUE TO show the same warnings with correct severity, strategy association, and counts

3.8 WHEN the Research Explorer displays studies THEN the system SHALL CONTINUE TO show correct study-to-strategy associations, filtering, and ordering

3.9 WHEN the ResultPicker displays available results THEN the system SHALL CONTINUE TO show results with correct metadata for user selection

---

## Bug Condition (Formal)

```pascal
FUNCTION isBugCondition(X)
  INPUT: X of type PageLoadRequest
  OUTPUT: boolean
  
  // Returns true when the page load triggers expensive unfiltered data access patterns
  RETURN X.Page IN {Dashboard, StrategyLibrary, StrategyDetail, RobustnessHub, 
                    ResearchExplorer, BacktestHistory, BacktestList, ResultPicker, MultiResultPicker}
    AND (X.TotalBacktestResults > 10 OR X.TotalStrategies > 3 OR X.TotalStudies > 10)
END FUNCTION
```

```pascal
// Property: Fix Checking — Page loads use indexed/filtered queries
FOR ALL X WHERE isBugCondition(X) DO
  result ← LoadPage'(X)
  ASSERT result.DataAccessPattern = FilteredQuery
    AND result.LoadTime < 2000ms
    AND result.FullDatasetLoadsCount = 0
END FOR
```

```pascal
// Property: Preservation Checking — Data correctness unchanged
FOR ALL X WHERE NOT isBugCondition(X) DO
  ASSERT LoadPage(X).DisplayedData = LoadPage'(X).DisplayedData
END FOR
```
