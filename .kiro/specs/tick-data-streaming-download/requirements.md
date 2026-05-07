# Requirements Document

## Introduction

This document specifies the requirements for per-day tick data caching in the `DukascopyDataProvider`. The feature ensures that tick data downloads are persisted to disk on a per-day basis, eliminating redundant re-downloads on subsequent runs, bounding memory usage to one day's worth of ticks at a time, and preserving the existing `IAsyncEnumerable<TickRecord>` streaming contract unchanged.

## Glossary

- **Provider**: The `DukascopyDataProvider` class in the Infrastructure layer that downloads and streams tick data from Dukascopy's CDN.
- **Tick_Cache**: The per-day CSV file stored on disk containing all tick records for a single trading day and symbol.
- **Cache_Path**: The file system path following the convention `{cacheDir}/{symbol}/ticks/{year:D4}/{month:D2}/{day:D2}.csv`.
- **Day_Ticks**: The complete set of tick records for a single trading day (all 24 hours merged and sorted).
- **Helpers**: The `DukascopyHelpers` static class providing tick CSV serialization, deserialization, and cache path generation.
- **Cache_Validity_Check**: The `IsCacheFileValid` method that verifies a cache file exists and exceeds 60 bytes in size.
- **Hour_Batch**: A group of up to 4 concurrent HTTP requests downloading hourly `.bi5` tick files from Dukascopy's CDN.

## Requirements

### Requirement 1: Per-Day Tick Cache Path Generation

**User Story:** As a developer, I want tick cache files stored in a predictable path convention, so that cache files are organized consistently with the existing bar caching pattern.

#### Acceptance Criteria

1. THE Helpers SHALL provide a `GetTickCachePath` method that returns a path matching the pattern `{cacheDir}/{symbol}/ticks/{year:D4}/{month:D2}/{day:D2}.csv`
2. WHEN `GetTickCachePath` is called, THE Helpers SHALL create the directory structure if it does not already exist
3. WHEN `GetTickCachePath` is called with the same inputs, THE Helpers SHALL return an identical path string every time

### Requirement 2: Tick CSV Serialization

**User Story:** As a developer, I want tick records written to CSV in a canonical format, so that cached data can be reliably loaded on subsequent runs.

#### Acceptance Criteria

1. THE Helpers SHALL provide a `SaveTicksToCsv` method that writes tick records to a CSV file with columns: Timestamp, BidPrice, BidSize, AskPrice, AskSize, LastPrice, LastSize
2. WHEN `SaveTicksToCsv` writes a file with at least one tick, THE Helpers SHALL produce a file that passes the Cache_Validity_Check (file size exceeds 60 bytes)
3. THE Helpers SHALL write all decimal values using `InvariantCulture` formatting to avoid locale-dependent parsing issues
4. THE Helpers SHALL write timestamps in ISO 8601 round-trip format (`O` specifier)

### Requirement 3: Tick CSV Deserialization

**User Story:** As a developer, I want tick records loaded from CSV reliably, so that cached data is restored without data loss.

#### Acceptance Criteria

1. THE Helpers SHALL provide a `LoadTicksFromCsv` method that reads a tick CSV file and returns a list of `TickRecord` objects
2. WHEN a CSV row contains fewer than 7 columns, THE Helpers SHALL skip that row without throwing an exception
3. WHEN a CSV row contains unparseable values, THE Helpers SHALL skip that row without throwing an exception
4. WHEN `LoadTicksFromCsv` reads a file written by `SaveTicksToCsv`, THE Helpers SHALL produce tick records with identical field values to the original input

### Requirement 4: Serialization Round-Trip Fidelity

**User Story:** As a developer, I want serialization and deserialization to be lossless, so that cached tick data is identical to the originally downloaded data.

#### Acceptance Criteria

1. FOR ALL valid lists of tick records, saving via `SaveTicksToCsv` then loading via `LoadTicksFromCsv` SHALL produce a list with identical count and field values to the original input
2. THE Helpers SHALL preserve full decimal precision for all price and size fields across the round-trip

### Requirement 5: Cache-First Tick Streaming

**User Story:** As a developer, I want `GetTicks` to check the disk cache before downloading, so that previously fetched days load instantly without network access.

#### Acceptance Criteria

1. WHEN `GetTicks` is called for a date range, THE Provider SHALL check the Tick_Cache for each trading day before attempting any download
2. WHEN a valid Tick_Cache file exists for a trading day, THE Provider SHALL load ticks from disk instead of downloading from the network
3. WHEN no valid Tick_Cache file exists for a trading day, THE Provider SHALL download all 24 hours of tick data from Dukascopy's CDN
4. WHEN a day's ticks are successfully downloaded, THE Provider SHALL persist them to the Tick_Cache before yielding ticks to the consumer
5. WHEN `GetTicks` completes, THE Provider SHALL log cache hit and miss counts for observability

### Requirement 6: Bounded Memory Usage

**User Story:** As a developer, I want memory usage bounded to one day's worth of ticks at a time, so that multi-year tick downloads do not exhaust system memory.

#### Acceptance Criteria

1. WHILE iterating through trading days, THE Provider SHALL hold at most one day's worth of tick records in memory at any point
2. WHEN a day's ticks have been fully yielded to the consumer, THE Provider SHALL release references to that day's data to allow garbage collection before loading the next day
3. THE Provider SHALL process trading days sequentially (not loading multiple days concurrently into memory)

### Requirement 7: Concurrent Hour Downloads

**User Story:** As a developer, I want hourly tick files downloaded concurrently in batches, so that cache-miss days download quickly.

#### Acceptance Criteria

1. WHEN downloading a day's tick data, THE Provider SHALL issue HTTP requests for all 24 hours in batches of 4 concurrent requests
2. WHEN all hour batches for a day complete, THE Provider SHALL merge all hour results into a single sorted list before persisting
3. WHEN an individual hour download fails, THE Provider SHALL include ticks from all other successful hours in the day's result

### Requirement 8: Chronological Ordering

**User Story:** As a developer, I want ticks yielded in chronological order, so that consumers can rely on timestamp ordering.

#### Acceptance Criteria

1. WHEN a day's ticks are loaded from cache or downloaded, THE Provider SHALL yield them sorted by timestamp in non-decreasing order
2. WHEN merging ticks from multiple hours after download, THE Provider SHALL sort the merged result by timestamp before persisting to cache

### Requirement 9: Timestamp Filtering

**User Story:** As a developer, I want only ticks within the requested time range yielded, so that consumers receive exactly the data they asked for.

#### Acceptance Criteria

1. THE Provider SHALL yield only ticks with `Timestamp >= from` and `Timestamp <= to` as specified in the `GetTicks` call parameters
2. WHEN a cached day contains ticks outside the requested range, THE Provider SHALL filter them out before yielding

### Requirement 10: Graceful Handling of Corrupted Cache Files

**User Story:** As a developer, I want corrupted or partial cache files to trigger re-download, so that bad data on disk does not produce incorrect results.

#### Acceptance Criteria

1. WHEN a cache file exists but fails the Cache_Validity_Check (size ≤ 60 bytes), THE Provider SHALL treat the day as a cache miss and re-download
2. WHEN `LoadTicksFromCsv` returns an empty list from a cache file (all rows malformed), THE Provider SHALL treat the day as a cache miss and re-download
3. WHEN `SaveTicksToCsv` fails mid-write (disk full, permissions error), THE Provider SHALL still yield the downloaded ticks from memory to the consumer
4. IF a partial cache file is left on disk from a failed write, THEN THE Provider SHALL detect it as invalid on the next run via the Cache_Validity_Check and re-download the full day

### Requirement 11: Atomic Day Granularity

**User Story:** As a developer, I want cache files to represent complete days only, so that partial data never pollutes the cache.

#### Acceptance Criteria

1. THE Provider SHALL write the cache file only after all 24 hours of a day have been downloaded and merged
2. WHEN all 24 hours return empty data for a day, THE Provider SHALL not write a cache file for that day
3. WHEN the download is interrupted mid-day (cancellation or process termination), THE Provider SHALL not write a partial cache file for that day

### Requirement 12: Cancellation Support

**User Story:** As a developer, I want tick streaming to respect cancellation tokens, so that long-running downloads can be stopped gracefully.

#### Acceptance Criteria

1. WHEN a cancellation is requested, THE Provider SHALL stop processing at the next day boundary by throwing `OperationCanceledException`
2. WHEN cancellation occurs, THE Provider SHALL leave all previously completed cache files intact for future runs
3. WHEN cancellation occurs mid-day download, THE Provider SHALL not write a cache file for the interrupted day

### Requirement 13: Preserved Public Contract

**User Story:** As a developer, I want the `IDataProvider.GetTicks` interface unchanged, so that no consumers need modification.

#### Acceptance Criteria

1. THE Provider SHALL maintain the existing `IAsyncEnumerable<TickRecord> GetTicks(string symbol, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)` method signature without modification
2. THE Provider SHALL not introduce any new public methods or interfaces on `IDataProvider`
3. THE Provider SHALL not require changes to the Core or Application layers

### Requirement 14: Error Resilience

**User Story:** As a developer, I want individual hour or day failures handled gracefully, so that partial network issues do not abort the entire download.

#### Acceptance Criteria

1. WHEN an HTTP request for a specific hour fails after retry exhaustion, THE Provider SHALL return an empty list for that hour and continue with remaining hours
2. WHEN LZMA decompression fails for a specific hour, THE Provider SHALL log a warning and return an empty list for that hour
3. WHEN disk write fails for a day's cache file, THE Provider SHALL log the error and continue yielding ticks from memory
4. IF all 24 hours of a day fail to download, THEN THE Provider SHALL return an empty result for that day and not write a cache file
