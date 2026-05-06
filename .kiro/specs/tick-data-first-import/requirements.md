# Requirements Document

## Introduction

This feature refactors the Dukascopy market data download workflow to use tick data as the master source. Instead of downloading pre-aggregated candle data at a specific timeframe, the system downloads raw tick data (20-byte binary records from Dukascopy's `h_ticks.bi5` files) and stores it as per-day CSV files. Users then generate any bar timeframe (1m, 5m, 15m, 30m, 1H, 4H, Daily) on demand from the stored tick data. The master tick data is internal — only generated timeframe files appear in the Data Files inventory.

## Glossary

- **Tick_Import_Service**: The Application-layer orchestration service that manages the lifecycle of tick data downloads, including progress reporting, cancellation, and incremental detection.
- **Tick_Cache**: The internal per-day CSV file storage for downloaded tick data, organized by symbol and date. Not visible to users in the Data Files page.
- **Tick_Import_Record**: A persistent metadata record tracking a tick data import job (symbol, date range, status, coverage).
- **Timeframe_Generator**: The service that reads cached tick data and produces aggregated bar CSV files at a requested timeframe.
- **Generated_Timeframe_Record**: A persistent metadata record linking a generated bar CSV file to its source tick import and timeframe.
- **Tick_Cache_Registry**: The metadata layer that tracks which days of tick data have been downloaded for each symbol, enabling incremental download detection.
- **Market_Data_Page**: The Blazor Server page at `/market-data` where users initiate tick imports and manage generated timeframes.
- **Data_Files_Page**: The Blazor Server page at `/data` that lists user-visible CSV data files.
- **Import_Detail_View**: A drill-down UI panel shown when a user clicks a completed tick import in the history, displaying coverage info and timeframe generation controls.

## Requirements

### Requirement 1: Tick Data Download

**User Story:** As a researcher, I want to download raw tick data from Dukascopy for a symbol and date range, so that I have a master dataset from which any bar timeframe can be generated.

#### Acceptance Criteria

1. WHEN the user submits a tick import request with a valid symbol and date range, THE Tick_Import_Service SHALL create a Running import record and begin downloading tick data from Dukascopy.
2. WHILE a tick import is running, THE Market_Data_Page SHALL display a progress indicator showing the current day being downloaded and the total number of trading days.
3. WHEN a trading day's tick data is successfully downloaded and decompressed, THE Tick_Import_Service SHALL write the ticks to a per-day CSV file in the Tick_Cache directory.
4. WHEN all trading days in the requested range have been processed, THE Tick_Import_Service SHALL update the Tick_Import_Record status to Completed and record the total tick count.
5. IF a network failure occurs during download after retries are exhausted, THEN THE Tick_Import_Service SHALL set the import status to Failed with an error detail message.
6. WHEN the user requests cancellation of a running tick import, THE Tick_Import_Service SHALL stop downloading, set the status to Cancelled, and retain any already-cached day files.
7. THE Tick_Import_Service SHALL reject requests where the start date is on or after the end date with a validation error.
8. THE Tick_Import_Service SHALL reject requests for symbols not present in the supported symbols list with a validation error.
9. WHILE a tick import is already running, THE Tick_Import_Service SHALL reject new import requests with an error indicating an import is already in progress.

### Requirement 2: Per-Day Tick Cache Storage

**User Story:** As a researcher, I want tick data stored as per-day CSV files, so that incremental downloads are efficient and only missing days need to be fetched.

#### Acceptance Criteria

1. THE Tick_Cache SHALL store tick data in per-day CSV files at the path `{CacheDir}/{Symbol}/ticks/{yyyy}/{MM}/{dd}.csv`.
2. WHEN a per-day tick CSV is written, THE Tick_Cache SHALL include columns: Timestamp, Bid, Ask, BidVolume, AskVolume.
3. THE Tick_Cache SHALL use ISO 8601 format with millisecond precision for the Timestamp column.
4. THE Tick_Cache SHALL use InvariantCulture decimal formatting for all numeric columns.
5. WHEN a day file already exists in the Tick_Cache for a given symbol and date, THE Tick_Import_Service SHALL skip downloading that day.
6. THE Tick_Cache files SHALL NOT appear in the Data_Files_Page inventory.

### Requirement 3: Incremental Download Detection

**User Story:** As a researcher, I want the system to detect existing tick data coverage and only download missing days, so that extending a date range does not re-download data I already have.

#### Acceptance Criteria

1. WHEN a tick import is requested for a symbol that already has cached tick data, THE Tick_Import_Service SHALL identify which trading days in the requested range are already cached.
2. WHEN cached days are detected, THE Tick_Import_Service SHALL download only the missing trading days.
3. WHEN all requested trading days are already cached, THE Tick_Import_Service SHALL complete immediately with zero downloads and update the Tick_Import_Record accordingly.
4. THE Tick_Cache_Registry SHALL provide the date range of existing coverage for a given symbol.

### Requirement 4: Timeframe Generation from Tick Data

**User Story:** As a researcher, I want to generate bar data at any supported timeframe from my stored tick data, so that I can produce 1m, 5m, 15m, 30m, 1H, 4H, or Daily bars without re-downloading.

#### Acceptance Criteria

1. WHEN the user requests timeframe generation for a completed tick import, THE Timeframe_Generator SHALL read the cached tick data for the import's symbol and date range.
2. THE Timeframe_Generator SHALL aggregate ticks into bars using time-based boundaries aligned to UTC midnight.
3. THE Timeframe_Generator SHALL support the following timeframes: 1m, 5m, 15m, 30m, 1H, 4H, Daily.
4. WHEN aggregation is complete, THE Timeframe_Generator SHALL write the bars to a CSV file in the canonical engine format (Timestamp, Open, High, Low, Close, Volume).
5. WHEN the output CSV is written, THE Timeframe_Generator SHALL register a DataFileRecord so the file appears in the Data_Files_Page.
6. WHEN the output CSV is written, THE Timeframe_Generator SHALL create a Generated_Timeframe_Record linking the file to the source tick import and timeframe.
7. THE Timeframe_Generator SHALL use the first tick's bid price as the bar Open, the highest bid as High, the lowest bid as Low, and the last tick's bid price as Close.
8. THE Timeframe_Generator SHALL compute bar Volume as the sum of bid volumes for all ticks in the aggregation window.
9. IF no ticks exist for a given aggregation window, THEN THE Timeframe_Generator SHALL skip that window and produce no bar.

### Requirement 5: Timeframe Generation Output Naming

**User Story:** As a researcher, I want generated timeframe files to have descriptive names, so that I can identify them in the Data Files inventory.

#### Acceptance Criteria

1. THE Timeframe_Generator SHALL name output files using the format `dukascopy_{Symbol}_{Timeframe}_{StartYYYYMMDD}_{EndYYYYMMDD}.csv`.
2. WHEN a file with the same name already exists, THE Timeframe_Generator SHALL overwrite it and update the existing DataFileRecord metadata.
3. THE Timeframe_Generator SHALL write to a temporary file first, then atomically rename to the final path on success.

### Requirement 6: Tick-to-Bar Round-Trip Consistency

**User Story:** As a researcher, I want confidence that generating bars from tick data produces correct results, so that my research is based on accurate data.

#### Acceptance Criteria

1. FOR ALL valid tick datasets, writing ticks to CSV then reading them back SHALL produce an equivalent tick sequence (round-trip property for tick CSV serialization).
2. FOR ALL valid tick datasets, generating bars at a given timeframe SHALL produce bars where each bar's High is greater than or equal to its Low.
3. FOR ALL valid tick datasets, generating bars at a given timeframe SHALL produce bars in strictly ascending timestamp order.
4. FOR ALL valid tick datasets, the total number of ticks consumed by bar generation SHALL equal the number of ticks in the source data that fall within trading windows.

### Requirement 7: Market Data Page UI Changes

**User Story:** As a researcher, I want the Market Data page to support the tick-first workflow, so that I can download tick data and then generate timeframes from it.

#### Acceptance Criteria

1. THE Market_Data_Page import form SHALL remove the Timeframe selector for the initial download step.
2. THE Market_Data_Page import form SHALL retain the Source, Symbol, Start Date, End Date, and Quick preset controls.
3. WHEN a tick import completes successfully, THE Market_Data_Page SHALL display it in the import history with a status badge, symbol, date range, and tick count.
4. WHEN the user clicks a completed tick import in the history, THE Market_Data_Page SHALL open the Import_Detail_View.
5. WHILE a tick import is running, THE Market_Data_Page SHALL disable the import form inputs and Start Download button.

### Requirement 8: Import Detail View

**User Story:** As a researcher, I want a detail view for completed tick imports where I can see coverage and generate timeframes, so that I can manage my data from one place.

#### Acceptance Criteria

1. THE Import_Detail_View SHALL display the symbol, source, date range, total tick count, and download timestamp of the tick import.
2. THE Import_Detail_View SHALL list all previously generated timeframes for this import with their file name, bar count, and generation timestamp.
3. THE Import_Detail_View SHALL provide a timeframe selector and a Generate button to create a new timeframe from the tick data.
4. WHEN the user clicks Generate, THE Import_Detail_View SHALL invoke the Timeframe_Generator and display a progress indicator until complete.
5. WHEN timeframe generation completes, THE Import_Detail_View SHALL refresh the generated timeframes list to include the new entry.
6. IF a timeframe has already been generated for this import, THEN THE Import_Detail_View SHALL show a warning that regenerating will overwrite the existing file.

### Requirement 9: Tick Import Record Persistence

**User Story:** As a researcher, I want tick import metadata persisted across application restarts, so that my import history and coverage information are not lost.

#### Acceptance Criteria

1. THE Tick_Import_Record SHALL include: ImportId, Source, Symbol, RequestedStart, RequestedEnd, Status, TotalTickCount, CreatedAt, CompletedAt, ErrorDetail.
2. THE Tick_Import_Record SHALL be persisted as a JSON file following the existing JsonFileRepository pattern.
3. WHEN the application starts and finds a Tick_Import_Record with status Running, THE Tick_Import_Service SHALL reset it to Failed with error detail "Interrupted by application restart".
4. FOR ALL valid Tick_Import_Record instances, serializing to JSON then deserializing SHALL produce an equivalent record (round-trip property).

### Requirement 10: Generated Timeframe Record Persistence

**User Story:** As a researcher, I want generated timeframe metadata persisted, so that the Import Detail View can show which timeframes have been generated.

#### Acceptance Criteria

1. THE Generated_Timeframe_Record SHALL include: RecordId, TickImportId, Timeframe, OutputFilePath, OutputFileId, BarCount, FirstBar, LastBar, GeneratedAt.
2. THE Generated_Timeframe_Record SHALL be persisted as a JSON file following the existing JsonFileRepository pattern.
3. FOR ALL valid Generated_Timeframe_Record instances, serializing to JSON then deserializing SHALL produce an equivalent record (round-trip property).

### Requirement 11: Data Files Page Integration

**User Story:** As a researcher, I want only generated timeframe files to appear in the Data Files page, so that my file inventory is clean and contains only research-ready bar data.

#### Acceptance Criteria

1. THE Data_Files_Page SHALL display generated timeframe CSV files registered via DataFileRecord.
2. THE Data_Files_Page SHALL NOT display raw tick cache CSV files.
3. WHEN a generated timeframe file is deleted from the Data_Files_Page, THE system SHALL remove the DataFileRecord and the Generated_Timeframe_Record but SHALL NOT delete the source tick cache data.

### Requirement 12: Concurrency and Safety

**User Story:** As a researcher, I want the system to handle concurrent operations safely, so that my data is not corrupted.

#### Acceptance Criteria

1. WHILE a tick import is running, THE Tick_Import_Service SHALL prevent a second tick import from starting.
2. WHILE a timeframe generation is running for a specific tick import, THE Timeframe_Generator SHALL prevent a second generation for the same import from starting.
3. THE Timeframe_Generator SHALL write output to a temporary file and rename atomically, so that a crash during generation does not leave a corrupt file in the data directory.
4. IF the application crashes during a tick import, THEN THE Tick_Import_Service SHALL retain all fully-written per-day cache files and only mark the import record as Failed on next startup.

### Requirement 13: Download Performance Optimization

**User Story:** As a researcher, I want tick data downloads to be as fast as possible, so that importing large date ranges (multiple years) completes in reasonable time.

#### Acceptance Criteria

1. THE Tick_Import_Service SHALL use a configurable maximum concurrency level for simultaneous HTTP downloads, defaulting to 10 concurrent requests.
2. THE Tick_Import_Service SHALL flatten all hour-files across all trading days into a single work queue rather than processing one day at a time, so that downloads are not blocked at day boundaries.
3. THE Tick_Import_Service SHALL configure `HttpClient.MaxConnectionsPerServer` to match or exceed the configured concurrency level, preventing socket-level throttling.
4. THE Tick_Import_Service SHALL skip downloading hours that fall on weekends (Saturday and Sunday), eliminating unnecessary 404 round-trips for hours where Dukascopy returns no data.
5. THE Tick_Import_Service SHALL pipeline download and disk-write operations so that writing cached tick files to disk does not block the next batch of HTTP requests.
6. THE maximum concurrency level SHALL be configurable via `IOptions<T>`-bound configuration (not a magic number).
7. WHEN the concurrency level is set to 1, THE Tick_Import_Service SHALL download files sequentially (useful for debugging or rate-limit scenarios).
