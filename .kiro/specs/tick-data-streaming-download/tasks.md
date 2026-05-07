# Implementation Plan: Tick Data Streaming Download

## Overview

Add per-day tick data caching to `DukascopyDataProvider.GetTicks()` by extending `DukascopyHelpers` with tick-specific CSV serialization/deserialization and cache path generation, then refactoring the `GetTicks` method to use a cache-first per-day flow with bounded memory. The implementation mirrors the existing bar caching pattern exactly.

## Tasks

- [x] 1. Extend DukascopyHelpers with tick cache path and serialization methods
  - [x] 1.1 Add `GetTickCachePath` static method to `DukascopyHelpers`
    - Implement path generation following `{cacheDir}/{symbol}/ticks/{year:D4}/{month:D2}/{day:D2}.csv` convention
    - Create directory structure if it does not exist (same pattern as `GetDayCachePath`)
    - _Requirements: 1.1, 1.2, 1.3_

  - [x] 1.2 Add `SaveTicksToCsv` static method to `DukascopyHelpers`
    - Write header row: `Timestamp,BidPrice,BidSize,AskPrice,AskSize,LastPrice,LastSize`
    - Write each tick using `InvariantCulture` formatting and ISO 8601 round-trip (`O`) timestamps
    - Access `BidLevels[0]`, `AskLevels[0]`, and `LastTrade` fields from `TickRecord`
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [x] 1.3 Add `LoadTicksFromCsv` static method to `DukascopyHelpers`
    - Read CSV file, skip header line
    - Parse each row into a `TickRecord` with `BidLevel`, `AskLevel`, and `LastTrade`
    - Skip rows with fewer than 7 columns without throwing
    - Skip rows with unparseable values without throwing
    - Use `InvariantCulture` for all parsing
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 4.1, 4.2_

  - [x] 1.4 Write property test: Serialization round-trip fidelity
    - **Property 1: Serialization round-trip fidelity**
    - For any valid list of TickRecord objects, `SaveTicksToCsv` → `LoadTicksFromCsv` produces identical count and field values
    - Use temp file for each test run, clean up after
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 4.1, 4.2, 3.4, 2.3, 2.4**

  - [x] 1.5 Write property test: Cache file validity after write
    - **Property 2: Cache file validity after write**
    - For any non-empty list of TickRecord objects, file produced by `SaveTicksToCsv` passes `IsCacheFileValid` (size > 60 bytes)
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 2.2, 10.4**

  - [x] 1.6 Write property test: Cache path determinism
    - **Property 6: Cache path determinism**
    - For any valid `cacheDir`, `symbol`, and `date`, `GetTickCachePath` returns the same string and matches the expected pattern
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 1.1, 1.3**

  - [x] 1.7 Write unit tests for `GetTickCachePath` format validation
    - Verify path matches `{cacheDir}/{symbol}/ticks/{year:D4}/{month:D2}/{day:D2}.csv` for various dates
    - Verify directory creation side effect
    - _Requirements: 1.1, 1.2_

  - [x] 1.8 Write unit tests for `LoadTicksFromCsv` malformed row handling
    - Test rows with fewer than 7 columns are skipped
    - Test rows with unparseable decimals are skipped
    - Test rows with unparseable timestamps are skipped
    - Test empty file returns empty list
    - _Requirements: 3.2, 3.3_

- [x] 2. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Refactor `DukascopyDataProvider.GetTicks` to cache-first per-day flow
  - [x] 3.1 Add private method `FetchAndCacheDayTicksAsync` to `DukascopyDataProvider`
    - Download all 24 hours in batches of 4 concurrent requests (reuse existing `FetchHourTicksAsync`)
    - Merge all hour results into a single list
    - Sort merged ticks by timestamp (non-decreasing order)
    - Persist sorted ticks via `SaveTicksToCsv` only if count > 0 (wrap in try/catch, log on failure)
    - Return the merged/sorted list
    - If all 24 hours return empty, return empty list without writing cache file
    - _Requirements: 7.1, 7.2, 7.3, 8.1, 8.2, 11.1, 11.2, 14.1, 14.2, 14.3, 14.4_

  - [x] 3.2 Replace the body of `GetTicks` with cache-first day iteration
    - Iterate trading days sequentially (one day in memory at a time)
    - For each day: check `IsCacheFileValid` on `GetTickCachePath` result
    - Cache hit: load via `LoadTicksFromCsv`, if empty list treat as cache miss and re-download
    - Cache miss: call `FetchAndCacheDayTicksAsync`
    - Filter ticks to `[from, to]` range before yielding
    - Set `dayTicks = null` after yielding to release for GC
    - Check `ct.ThrowIfCancellationRequested()` at each day boundary
    - Log cache hit/miss counts after iteration completes
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 6.1, 6.2, 6.3, 9.1, 9.2, 10.1, 10.2, 10.3, 12.1, 12.2, 12.3, 13.1, 13.2, 13.3_

  - [x] 3.3 Write property test: Chronological ordering after merge
    - **Property 3: Chronological ordering after merge**
    - For any collection of tick lists from multiple hours, merging and sorting produces non-decreasing timestamp order
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 8.1, 8.2, 7.2**

  - [x] 3.4 Write property test: Timestamp range filtering
    - **Property 4: Timestamp range filtering**
    - For any list of ticks and any `from`/`to` range, filtered output contains only ticks within range and all qualifying ticks
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 9.1, 9.2**

  - [x] 3.5 Write property test: Malformed row resilience
    - **Property 5: Malformed row resilience**
    - For any CSV with a mix of valid and malformed rows, `LoadTicksFromCsv` returns exactly the valid rows without throwing
    - `[Property(MaxTest = 100)]`
    - **Validates: Requirements 3.2, 3.3, 10.2**

- [x] 4. Checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Integration tests for tick caching end-to-end
  - [x] 5.1 Write integration test: cache file creation on first download
    - Create `DukascopyDataProvider` with a temp cache directory
    - Mock HTTP responses for a small date range (1-2 days)
    - Call `GetTicks`, verify one CSV file per trading day is created in the expected path
    - Verify file content is valid (passes `IsCacheFileValid`, loadable via `LoadTicksFromCsv`)
    - _Requirements: 5.3, 5.4, 11.1_

  - [x] 5.2 Write integration test: cache hit skips network on second call
    - Pre-populate cache directory with valid tick CSV files
    - Create provider with no HTTP handler (or one that throws)
    - Call `GetTicks` for the cached date range
    - Verify ticks are returned from cache without network access
    - _Requirements: 5.1, 5.2_

  - [x] 5.3 Write integration test: corrupted cache triggers re-download
    - Write a truncated/invalid file (≤ 60 bytes) to the cache path
    - Call `GetTicks` with a working HTTP mock
    - Verify the day is re-downloaded and the cache file is overwritten with valid data
    - _Requirements: 10.1, 10.4_

  - [x] 5.4 Write integration test: partial hour failure still yields remaining ticks
    - Mock HTTP to fail for specific hours (e.g., hours 3, 7, 15)
    - Call `GetTicks` for that day
    - Verify ticks from successful hours are returned and cached
    - _Requirements: 7.3, 14.1, 14.2_

- [x] 6. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties from the design document (Properties 1-6)
- Unit tests validate specific examples and edge cases
- The public contract `IDataProvider.GetTicks` remains unchanged (Requirement 13)
- All tests use `FsCheck.Xunit` with `[Property(MaxTest = 100)]` per workspace testing standards
- Property test classes follow naming convention: `DukascopyTickCache*Properties`
- Unit test classes follow naming convention: `DukascopyTickCache*Tests`
