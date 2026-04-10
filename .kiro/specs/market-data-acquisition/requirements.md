# Requirements — Market Data Acquisition Workflow

## Introduction

TradingResearchEngine currently has a `DukascopyDataProvider` that downloads and caches market data inline during backtest execution, and a `DataFileService` that manages local CSV files. However, there is no first-class workflow for acquiring market data as a standalone operation — users must either manually import CSV files or rely on the inline download path that couples data acquisition with backtest execution.

This spec adds a Market Data Acquisition workflow that lets users fetch historical candles from external providers (starting with Dukascopy), normalize them into the engine's canonical CSV format, and register them as validated Data Files. The model is: **download → normalize → approved CSV → DataFileRecord → validation → analytics consumption**.

Market Data is an adjacent workflow to Data Files: Market Data is the factory for generating approved CSVs, while Data Files remains the library for previewing, validating, and selecting those files for research. This separation avoids overloading the Data Files screen with acquisition concerns.

### Key Architecture Decisions

1. All timestamps are UTC. Daily bars use UTC midnight boundaries (not New York close). Provider-specific session semantics are not preserved.
2. Dukascopy data is BID-basis. The candle basis (Bid/Ask/Mid) is recorded on the import record but not embedded in the CSV itself. The engine's canonical CSV schema is basis-agnostic.
3. V1 uses provider-native timeframes only. No local aggregation from tick/minute data — the existing `DukascopyDataProvider` already handles minute-to-interval aggregation, and the import workflow reuses that capability.
4. The canonical CSV schema is: `Timestamp,Open,High,Low,Close,Volume` with ISO 8601 UTC timestamps, strictly ascending rows, and positive OHLC values.
5. Core is untouched. All new concepts live in Application, Infrastructure, and Web.
6. Date range semantics: `RequestedStart` is inclusive, `RequestedEnd` is exclusive. A request for `2020-01-01` to `2025-01-01` includes bars on 2020-01-01 but not on 2025-01-01.
7. Output file naming: `{source}_{symbol}_{timeframe}_{startYYYYMMDD}_{endYYYYMMDD}.csv` (e.g. `dukascopy_EURUSD_1H_20200101_20250101.csv`).
8. Duplicate imports: if an import with the same (source, symbol, timeframe, range) already exists as Completed, the user is warned but may proceed. The new file overwrites the previous output. The old `DataFileRecord` is updated, not duplicated.
9. Failed validation after download: the CSV file is retained on disk but the `DataFileRecord` is created with `ValidationStatus = Invalid`. The import record is set to `Status = Completed` with the file linked. The user can inspect the file in Data Files.
10. Concurrency: only one import job may run at a time. Starting a second import while one is running SHALL show an inline error. This simplifies progress tracking and avoids provider rate-limit issues.
11. Startup recovery: on startup, any import records with `Status = Running` are reset to `Status = Failed` with `ErrorDetail = "Interrupted by application restart"`. Orphaned temp files in the staging directory are deleted.
12. Progress percentages are based on completed download chunks (days or months) out of total scheduled chunks.

---

## Requirements

### Requirement 1 — Market Data Import Screen

**User Story:** As a researcher, I want a dedicated Market Data screen where I can configure and launch data imports from external providers, so that I can acquire validated data files without leaving the application.

#### Acceptance Criteria

1. A Market Data screen SHALL be accessible from the sidebar navigation under the SETTINGS group, positioned above Data Files.
2. THE screen SHALL display an import configuration form with: source selector (initially only Dukascopy), symbol selector (from a curated list), timeframe selector, start date, and end date.
3. THE symbol selector SHALL present symbols from the selected provider's supported list with human-readable labels (e.g. "EURUSD — Euro/US Dollar").
4. THE timeframe selector SHALL present only timeframes supported by the selected provider: 1m, 5m, 15m, 30m, 1H, 4H, Daily.
5. DATE range input SHALL use explicit start/end date pickers with quick presets: 1Y, 3Y, 5Y, 10Y from today.
6. A [Start Download] button SHALL launch the import as a background job.
7. VALIDATION: start date must be before end date, symbol must be from the supported list, timeframe must be supported. Invalid inputs SHALL show inline errors and block the download.

---

### Requirement 2 — Import Job Execution

**User Story:** As a researcher, I want import jobs to run in the background with progress reporting and cancellation, so that I can continue working while data downloads.

#### Acceptance Criteria

1. WHEN an import is started, THEN the system SHALL create a `MarketDataImportRecord` with `Status = Running` and persist it immediately.
2. THE import job SHALL run in the background (non-blocking) using the existing `IProgressReporter` pattern.
3. PROGRESS SHALL be reported as determinate when the provider supports chunked downloads (e.g. "Downloading month 7 of 60 — 11%").
4. A [Cancel] button SHALL be available on running imports. Cancellation SHALL stop the download cleanly, remove any partial output files, and set `Status = Cancelled`.
5. WHEN the download completes successfully, THEN the system SHALL normalize the data into the canonical CSV format and write it to the managed data directory using the deterministic filename convention.
6. WHEN normalization completes, THEN the system SHALL create a `DataFileRecord` for the output file with metadata (symbol, timeframe, first/last bar, bar count) derived from the written output stream, then run the existing validation pipeline.
7. IF validation passes, THEN the `DataFileRecord` SHALL have `ValidationStatus = Valid` and the import record SHALL be updated with `Status = Completed`, `OutputFilePath`, and `OutputFileId`.
8. IF validation fails, THEN the `DataFileRecord` SHALL have `ValidationStatus = Invalid`, the CSV SHALL be retained on disk, and the import record SHALL still be set to `Status = Completed` with the file linked. The user can inspect the invalid file in Data Files.
9. WHEN the download or normalization fails (before a CSV is written), THEN the import record SHALL be updated with `Status = Failed` and `ErrorDetail` containing the error message. No `DataFileRecord` is created.
10. THE import service SHALL be a singleton distinct from `BackgroundStudyService` to avoid mixing research jobs and acquisition jobs.
11. ONLY one import job may run at a time. Attempting to start a second import while one is running SHALL show an inline error.

---

### Requirement 3 — Import History

**User Story:** As a researcher, I want to see a history of all import jobs with their status, so that I can track what data I've acquired and retry failed imports.

#### Acceptance Criteria

1. THE Market Data screen SHALL display a "Recent Imports" list below the import form, showing all `MarketDataImportRecord` entries ordered by creation date descending.
2. EACH import entry SHALL show: status badge (✅ Completed, 🔄 Running with %, ❌ Failed, ⚠️ Cancelled), source, symbol, timeframe, date range, bar count (if completed), and output file path (if completed).
3. COMPLETED imports SHALL show actions: [View in Data Files] (navigates to Data Files filtered to the output file) and [Re-import] (pre-fills the form with the same parameters).
4. RUNNING imports SHALL show: progress bar with percentage and [Cancel] button.
5. FAILED imports SHALL show: error detail (collapsed by default) and [Retry] button.
6. CANCELLED imports SHALL show: [Retry] button.

---

### Requirement 4 — Canonical CSV Contract

**User Story:** As a developer, I want a canonical CSV schema that all providers normalize to, so that imported files are consistent regardless of source and compatible with the existing engine and validation pipeline.

#### Acceptance Criteria

1. THE canonical CSV schema SHALL have the header: `Timestamp,Open,High,Low,Close,Volume`.
2. TIMESTAMPS SHALL be ISO 8601 format with UTC timezone suffix `Z` (e.g. `2020-01-01T00:00:00Z`).
3. ROWS SHALL be strictly ascending by timestamp with no duplicates.
4. OPEN, HIGH, LOW, CLOSE values SHALL be positive decimals formatted with InvariantCulture.
5. VOLUME SHALL be present on every row. If the provider does not supply volume, it SHALL be written as `0`.
6. THE schema SHALL match the existing engine format already used by `CsvFormatConverter.SourceFormat.Engine` and the `DukascopyDataProvider` cache format.
7. ALL provider adapters SHALL normalize to this schema before writing the output file.

---

### Requirement 5 — Market Data Provider Abstraction

**User Story:** As a developer, I want a provider-agnostic interface for market data sources, so that additional providers can be added later without redesigning the import workflow.

#### Acceptance Criteria

1. AN `IMarketDataProvider` interface SHALL be defined in Application with methods: `SourceName` (string property), `GetSupportedSymbolsAsync`, and `DownloadToFileAsync`.
2. `GetSupportedSymbolsAsync` SHALL return a list of `MarketSymbolInfo` records with: `Symbol`, `DisplayName`, `SupportedTimeframes` (string array).
3. `DownloadToFileAsync` SHALL accept: symbol, timeframe, start date, end date, output file path, optional `IProgressReporter`, and `CancellationToken`. It SHALL write the normalized canonical CSV directly to the output path.
4. THE Dukascopy implementation SHALL reuse the existing download, decompression, and aggregation logic from `DukascopyDataProvider`, refactored into the new interface.
5. THE provider abstraction SHALL live in Application. The Dukascopy implementation SHALL live in Infrastructure.
6. PROVIDERS SHALL be registered in DI and discoverable by source name.

---

### Requirement 6 — MarketDataImportRecord Persistence

**User Story:** As a developer, I want import job records persisted as JSON files, so that import history survives application restarts.

#### Acceptance Criteria

1. A `MarketDataImportRecord` record SHALL be defined in Application with fields: `ImportId`, `Source`, `Symbol`, `Timeframe`, `RequestedStart`, `RequestedEnd`, `Status` (enum: Running, Completed, Failed, Cancelled), `OutputFilePath` (nullable), `OutputFileId` (nullable), `DownloadedChunkCount` (nullable int), `TotalChunkCount` (nullable int), `ErrorDetail` (nullable), `CandleBasis` (string, default "Bid"), `CreatedAt`, `CompletedAt` (nullable).
2. A `MarketDataImportStatus` enum SHALL be defined: Running, Completed, Failed, Cancelled.
3. AN `IMarketDataImportRepository` interface SHALL be defined in Application with standard CRUD signatures matching the existing `IDataFileRepository` pattern.
4. A `JsonMarketDataImportRepository` SHALL be implemented in Infrastructure, storing records as JSON files in an `imports/` subdirectory.
5. THE record SHALL implement `IHasId` with `Id => ImportId`.

---

### Requirement 7 — MarketDataImportService Orchestration

**User Story:** As a developer, I want a service that orchestrates the full import lifecycle (validate → download → normalize → register → validate), so that the Web layer only needs to call one method.

#### Acceptance Criteria

1. A `MarketDataImportService` SHALL be defined in Application as a singleton.
2. `StartImportAsync` SHALL: validate the request, create a `MarketDataImportRecord`, launch the download on a background task, and return the import ID immediately.
3. THE background task SHALL: call the provider's `DownloadToFileAsync`, create a `DataFileRecord` via `IDataFileRepository`, and update the import record on completion or failure.
4. `CancelImport(importId)` SHALL cancel the running import via `CancellationToken`.
5. `GetActiveImports()` SHALL return a list of currently running imports with progress.
6. THE service SHALL expose `OnProgress` and `OnCompleted` events for UI subscription, following the same pattern as `BackgroundStudyService`.
7. ON application startup, any import records with `Status = Running` SHALL be reset to `Status = Failed` with `ErrorDetail = "Interrupted by application restart"`. Orphaned temporary files in the staging directory SHALL be deleted.

---

### Requirement 8 — Data Files and Builder Integration

**User Story:** As a researcher, I want imported files to appear automatically in Data Files and the Strategy Builder, so that I can use them immediately after import without manual steps.

#### Acceptance Criteria

1. WHEN an import completes successfully and validation passes, THEN the output file SHALL appear in the Data Files list with `ValidationStatus = Valid`.
2. THE Data Files screen SHALL show an [Import from Market Source] button that navigates to the Market Data screen.
3. THE Strategy Builder Step 2 SHALL show a helper link "Can't find your data? Import market data →" when the file list is empty or as a persistent helper below the file selector.
4. THE `DataFileRecord` created by the import SHALL include `DetectedSymbol`, `DetectedTimeframe`, `FirstBar`, `LastBar`, and `BarCount` populated from the written output stream during normalization — not inferred solely from request parameters.

---

### Requirement 9 — Dukascopy Provider Implementation

**User Story:** As a researcher, I want to download historical data from Dukascopy's free datafeed, so that I can acquire forex, gold, and index data without an API key.

#### Acceptance Criteria

1. THE Dukascopy provider SHALL support the same symbols already defined in `DukascopyDataProvider.PointSizes`: EURUSD, GBPUSD, USDJPY, USDCHF, AUDUSD, NZDUSD, USDCAD, EURGBP, EURJPY, GBPJPY, XAUUSD, XAGUSD, USA500IDXUSD, USA30IDXUSD, USATECHIDXUSD.
2. THE provider SHALL support timeframes: 1m, 5m, 15m, 30m, 1H, 4H, Daily.
3. THE provider SHALL download minute-level BID candles from Dukascopy's datafeed and aggregate to the requested timeframe, reusing the existing aggregation logic.
4. THE provider SHALL report progress by day or month chunk during download.
5. THE provider SHALL handle network errors gracefully: retry transient failures (up to 3 retries per chunk with exponential backoff), skip days with no data (weekends, holidays), and report permanent failures clearly.
6. THE candle basis SHALL be recorded as "Bid" on the import record.
7. THE provider SHALL skip weekends (Saturday/Sunday) when building the download schedule, consistent with existing behavior.

---

### Requirement 10 — Unit and Integration Tests

**User Story:** As a developer, I want tests covering the import lifecycle, normalization, and persistence, so that the acquisition workflow is reliable and regressions are caught.

#### Acceptance Criteria

1. UNIT tests SHALL cover: `MarketDataImportRecord` JSON round-trip, import request validation (invalid range, unsupported symbol/timeframe), canonical CSV writer output format, and import service state transitions.
2. INTEGRATION tests SHALL cover: `JsonMarketDataImportRepository` CRUD, end-to-end import with a mock provider producing a valid CSV, and verification that the output appears in `IDataFileRepository`.
3. UNIT tests SHALL NOT make real HTTP calls to Dukascopy. Provider tests SHALL use fixture data or mocks.
4. ALL tests SHALL follow existing naming conventions: `<SubjectUnderTest>Tests` for classes, `<Method>_<Condition>_<Expected>` for methods.

---

### Requirement 11 — Backwards Compatibility

**User Story:** As an existing user, I want the new Market Data feature to not break any existing functionality, so that my current data files, strategies, and runs continue to work.

#### Acceptance Criteria

1. THE existing `DukascopyDataProvider` (used for inline backtest data) SHALL remain functional and unchanged. The new import workflow is a separate code path.
2. THE existing `DataFileService` SHALL continue to work for manually imported files.
3. THE existing `DataFileRecord` schema SHALL NOT be modified. Import-created records use the same fields.
4. NO existing Core types SHALL be modified.
5. THE new navigation item SHALL not displace or rename existing navigation entries.
