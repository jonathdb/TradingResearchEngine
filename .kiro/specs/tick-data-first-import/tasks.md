# Implementation Plan: Tick Data First Import

## Overview

This plan implements the tick-first architecture for Dukascopy market data downloads. The system downloads raw tick data, caches it as per-day CSV files, and generates bar timeframes on demand. Implementation proceeds bottom-up: records/interfaces first, then infrastructure, then application services, then DI wiring, then UI, then tests.

## Tasks

- [x] 1. Create Application layer records, enums, and value types
  - [x] 1.1 Create `TickImportStatus` enum in `Application/TickImport/TickImportStatus.cs`
    - Define Running, Completed, Failed, Cancelled members with XML doc comments
    - _Requirements: 9.1_
  - [x] 1.2 Create `TickCsvRow` readonly record struct in `Application/TickImport/TickCsvRow.cs`
    - Fields: Timestamp (DateTimeOffset), Bid (decimal), Ask (decimal), BidVolume (decimal), AskVolume (decimal)
    - _Requirements: 2.2, 2.3, 2.4_
  - [x] 1.3 Create `TickImportRecord` sealed record in `Application/TickImport/TickImportRecord.cs`
    - Implement `IHasId` interface, include all fields from design (ImportId, Source, Symbol, RequestedStart, RequestedEnd, Status, TotalTickCount, ErrorDetail, CreatedAt, CompletedAt)
    - _Requirements: 9.1_
  - [x] 1.4 Create `GeneratedTimeframeRecord` sealed record in `Application/TickImport/GeneratedTimeframeRecord.cs`
    - Implement `IHasId` interface, include all fields from design (RecordId, TickImportId, Timeframe, OutputFilePath, OutputFileId, BarCount, FirstBar, LastBar, GeneratedAt)
    - _Requirements: 10.1_
  - [x] 1.5 Create `TickImportOptions` class in `Application/TickImport/TickImportOptions.cs`
    - Include SectionName constant, MaxConcurrency (default 10), MaxConnectionsPerServer (default 10), MaxRetryAttempts (default 3), CacheDirectory (default "data/tick-cache")
    - _Requirements: 13.1, 13.6_
  - [x] 1.6 Create `TickImportProgressUpdate` and `TickImportCompletionUpdate` records in `Application/TickImport/TickImportProgressUpdate.cs`
    - TickImportProgressUpdate(ImportId, Current, Total, Label)
    - TickImportCompletionUpdate(ImportId, Status, ErrorMessage)
    - ActiveTickImport(ImportId, Symbol, Current, Total, StartedAt)
    - _Requirements: 1.2_

- [x] 2. Create Application layer interfaces
  - [x] 2.1 Create `ITickCacheService` interface in `Application/TickImport/ITickCacheService.cs`
    - Methods: GetMissingDaysAsync, GetCoverageAsync, WriteDayTicksAsync, ReadTicksAsync, GetTickCountAsync
    - All methods accept CancellationToken
    - ReadTicksAsync returns IAsyncEnumerable<TickCsvRow>
    - _Requirements: 2.1, 2.5, 3.1, 3.4_
  - [x] 2.2 Create `ITickImportRepository` interface in `Application/TickImport/ITickImportRepository.cs`
    - Methods: GetAsync, ListAsync, SaveAsync, DeleteAsync
    - _Requirements: 9.2_
  - [x] 2.3 Create `IGeneratedTimeframeRepository` interface in `Application/TickImport/IGeneratedTimeframeRepository.cs`
    - Methods: GetAsync, ListByImportAsync, SaveAsync, DeleteAsync
    - _Requirements: 10.2_

- [x] 3. Checkpoint - Verify Application layer compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement Infrastructure layer — TickCacheService
  - [x] 4.1 Create `TickCacheService` class in `Infrastructure/TickImport/TickCacheService.cs`
    - Implement `ITickCacheService`
    - Store tick data at path `{CacheDir}/{Symbol}/ticks/{yyyy}/{MM}/{dd}.csv`
    - CSV format: Timestamp (ISO 8601 with ms precision), Bid, Ask, BidVolume, AskVolume using InvariantCulture
    - GetMissingDaysAsync: enumerate existing day files, return weekdays in range not present
    - GetCoverageAsync: scan directory for earliest/latest cached day
    - WriteDayTicksAsync: write CSV with header row, overwrite if exists
    - ReadTicksAsync: stream rows from day files in date order as IAsyncEnumerable
    - GetTickCountAsync: count lines (minus headers) across day files in range
    - Inject IOptions<TickImportOptions> for CacheDirectory
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 3.1, 3.4_

- [x] 5. Implement Infrastructure layer — JSON Repositories
  - [x] 5.1 Create `JsonTickImportRepository` in `Infrastructure/TickImport/JsonTickImportRepository.cs`
    - Follow existing `JsonFileRepository<T>` pattern — one JSON file per entity
    - Implement ITickImportRepository (GetAsync, ListAsync, SaveAsync, DeleteAsync)
    - _Requirements: 9.2_
  - [x] 5.2 Create `JsonGeneratedTimeframeRepository` in `Infrastructure/TickImport/JsonGeneratedTimeframeRepository.cs`
    - Follow existing `JsonFileRepository<T>` pattern — one JSON file per entity
    - Implement IGeneratedTimeframeRepository (GetAsync, ListByImportAsync, SaveAsync, DeleteAsync)
    - _Requirements: 10.2_

- [x] 6. Implement Infrastructure layer — DukascopyTickDownloader
  - [x] 6.1 Create `DukascopyTickDownloader` class in `Infrastructure/TickImport/DukascopyTickDownloader.cs`
    - Flatten all hour-files across all trading days into a single work queue
    - Skip weekend hours (Saturday and Sunday) entirely
    - Use configurable concurrency via SemaphoreSlim (from TickImportOptions.MaxConcurrency)
    - Download Dukascopy h_ticks.bi5 files, decompress LZMA, parse 20-byte binary tick records using existing `DukascopyHelpers.ParseTicks()`
    - Convert parsed ticks to TickCsvRow values
    - Yield TickDownloadResult(Date, Hour, Ticks) as IAsyncEnumerable
    - Use Polly for retry: exponential backoff, retry on 5xx/network errors, skip 404s
    - Configure HttpClient MaxConnectionsPerServer from options
    - Report progress via IProgress<(int current, int total)>
    - _Requirements: 13.1, 13.2, 13.3, 13.4, 13.5_

- [x] 7. Checkpoint - Verify Infrastructure layer compiles
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Implement Application layer — TickImportService
  - [x] 8.1 Create `TickImportService` class in `Application/TickImport/TickImportService.cs`
    - Singleton service, only one import at a time (reject concurrent imports)
    - StartTickImportAsync: validate request (start < end, supported symbol), detect missing days via ITickCacheService, create Running record, launch background download
    - Orchestrate DukascopyTickDownloader, accumulate ticks per day, write via ITickCacheService.WriteDayTicksAsync
    - Fire OnProgress events during download, OnCompleted when done
    - CancelImport: cancel via CancellationTokenSource, set status to Cancelled
    - GetActiveImport: return snapshot of running import or null
    - RecoverOnStartupAsync: find Running records, reset to Failed with "Interrupted by application restart"
    - Handle errors: network failure → Failed status with ErrorDetail; individual hour 404/decompression errors → skip and continue
    - Pipeline disk writes so I/O does not block HTTP requests
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2.5, 3.1, 3.2, 3.3, 9.3, 12.1, 12.4, 13.5_

- [x] 9. Implement Application layer — TimeframeGeneratorService
  - [x] 9.1 Create `TimeframeGeneratorService` class in `Application/TickImport/TimeframeGeneratorService.cs`
    - GenerateTimeframeAsync: read ticks from ITickCacheService.ReadTicksAsync, aggregate into bars
    - Aggregation rules: Open=first bid, High=max bid, Low=min bid, Close=last bid, Volume=sum bid volumes
    - Time boundaries aligned to UTC midnight using interval minutes (1, 5, 15, 30, 60, 240, 1440)
    - Skip empty windows (no bar produced)
    - Write output CSV in canonical format (Timestamp, Open, High, Low, Close, Volume)
    - Output filename: `dukascopy_{Symbol}_{Timeframe}_{StartYYYYMMDD}_{EndYYYYMMDD}.csv`
    - Write to temp file first, then atomic rename to final path
    - Register DataFileRecord via IDataFileRepository
    - Create GeneratedTimeframeRecord via IGeneratedTimeframeRepository
    - Prevent concurrent generation for same import (throw InvalidOperationException)
    - If file already exists, overwrite and update existing records
    - _Requirements: 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 4.8, 4.9, 5.1, 5.2, 5.3, 12.2, 12.3_

- [x] 10. Register DI services and configuration
  - [x] 10.1 Add `TickImport` configuration section to `appsettings.json` in the Web project
    - Add TickImport section with MaxConcurrency, MaxConnectionsPerServer, MaxRetryAttempts, CacheDirectory defaults
    - _Requirements: 13.6_
  - [x] 10.2 Register tick import services in Infrastructure `ServiceCollectionExtensions`
    - Bind IOptions<TickImportOptions> from configuration section "TickImport"
    - Register ITickCacheService → TickCacheService (Singleton)
    - Register ITickImportRepository → JsonTickImportRepository (Singleton)
    - Register IGeneratedTimeframeRepository → JsonGeneratedTimeframeRepository (Singleton)
    - Register DukascopyTickDownloader (Singleton) with named HttpClient configured with MaxConnectionsPerServer and Polly retry policy
    - Register TickImportService (Singleton)
    - Register TimeframeGeneratorService (Singleton)
    - Call TickImportService.RecoverOnStartupAsync on app startup
    - _Requirements: 13.1, 13.3, 13.6_

- [x] 11. Checkpoint - Verify full backend compiles and DI resolves
  - Ensure all tests pass, ask the user if questions arise.

- [x] 12. Implement Web UI — MarketData.razor amendments
  - [x] 12.1 Amend `MarketData.razor` to remove Timeframe selector from import form
    - Remove the timeframe dropdown from the import form
    - Retain Source, Symbol, Start Date, End Date, and Quick preset controls
    - Wire Start Download button to TickImportService.StartTickImportAsync
    - Disable form inputs and button while import is running
    - _Requirements: 7.1, 7.2, 7.5_
  - [x] 12.2 Add import history section to `MarketData.razor`
    - Display completed/failed/cancelled imports with status badge, symbol, date range, tick count
    - Show running import with progress bar (current/total hours), cancel button
    - Subscribe to TickImportService.OnProgress and OnCompleted events, call StateHasChanged via InvokeAsync
    - Add "View Details" link on completed imports that navigates to ImportDetail
    - _Requirements: 7.3, 7.4, 1.2_

- [x] 13. Implement Web UI — ImportDetail.razor new component
  - [x] 13.1 Create `ImportDetail.razor` component in `Web/Components/Pages/ImportDetail.razor`
    - Accept import ID as route parameter
    - Display symbol, source, date range, total tick count, download timestamp
    - Show "Back to Market Data" navigation link
    - _Requirements: 8.1_
  - [x] 13.2 Add timeframe generation controls to ImportDetail.razor
    - Timeframe selector dropdown (1m, 5m, 15m, 30m, 1H, 4H, Daily)
    - Generate button that invokes TimeframeGeneratorService.GenerateTimeframeAsync
    - Progress indicator during generation
    - Warning message when regenerating an existing timeframe
    - _Requirements: 8.3, 8.4, 8.6_
  - [x] 13.3 Add generated timeframes list to ImportDetail.razor
    - List all GeneratedTimeframeRecords for this import via IGeneratedTimeframeRepository.ListByImportAsync
    - Show file name, bar count, generation timestamp for each
    - Refresh list after successful generation
    - _Requirements: 8.2, 8.5_

- [x] 14. Checkpoint - Verify UI compiles and renders
  - Ensure all tests pass, ask the user if questions arise.

- [x] 15. Property-based tests
  - [x]* 15.1 Write property test: Tick CSV Serialization Round-Trip
    - **Property 1: Tick CSV Serialization Round-Trip**
    - **Validates: Requirements 6.1, 2.2, 2.3, 2.4**
    - Class: `TickCsvSerializationProperties` in `UnitTests/TickImport/`
    - Generate arbitrary valid TickCsvRow sequences, write to CSV via TickCacheService logic, read back, assert equivalence
  - [x]* 15.2 Write property test: Incremental Detection Correctness
    - **Property 2: Incremental Detection Correctness**
    - **Validates: Requirements 2.5, 3.1, 3.2**
    - Class: `TickCacheDetectionProperties` in `UnitTests/TickImport/`
    - Generate arbitrary pre-cached day sets and date ranges, verify GetMissingDaysAsync returns exactly weekdays not in cached set
  - [x]* 15.3 Write property test: Bar Aggregation OHLC and Volume Correctness
    - **Property 3: Bar Aggregation OHLC and Volume Correctness**
    - **Validates: Requirements 4.7, 4.8**
    - Class: `TickToBarAggregationProperties` in `UnitTests/TickImport/`
    - Generate arbitrary non-empty tick sequences within a single window, verify Open=first bid, High=max bid, Low=min bid, Close=last bid, Volume=sum bid volumes
  - [x]* 15.4 Write property test: Bar Timestamp Alignment
    - **Property 4: Bar Timestamp Alignment**
    - **Validates: Requirements 4.2**
    - Class: `TickToBarAggregationProperties` in `UnitTests/TickImport/`
    - Generate arbitrary tick datasets and timeframes, verify every bar timestamp % intervalMinutes == 0
  - [x]* 15.5 Write property test: Bar High >= Low Invariant
    - **Property 5: Bar High >= Low Invariant**
    - **Validates: Requirements 6.2**
    - Class: `TickToBarAggregationProperties` in `UnitTests/TickImport/`
    - Generate arbitrary tick datasets, verify High >= Low for every generated bar
  - [x]* 15.6 Write property test: Bars in Strictly Ascending Timestamp Order
    - **Property 6: Bars in Strictly Ascending Timestamp Order**
    - **Validates: Requirements 6.3**
    - Class: `TickToBarAggregationProperties` in `UnitTests/TickImport/`
    - Generate arbitrary tick datasets, verify each bar timestamp > previous bar timestamp
  - [x]* 15.7 Write property test: Tick Conservation
    - **Property 7: Tick Conservation**
    - **Validates: Requirements 6.4**
    - Class: `TickToBarAggregationProperties` in `UnitTests/TickImport/`
    - Generate arbitrary tick datasets, verify total ticks consumed equals source tick count within trading windows
  - [x]* 15.8 Write property test: TickImportRecord JSON Round-Trip
    - **Property 8: TickImportRecord JSON Round-Trip**
    - **Validates: Requirements 9.4**
    - Class: `TickImportRecordProperties` in `UnitTests/TickImport/`
    - Generate arbitrary valid TickImportRecord instances, serialize to JSON, deserialize, assert equivalence
  - [x]* 15.9 Write property test: GeneratedTimeframeRecord JSON Round-Trip
    - **Property 9: GeneratedTimeframeRecord JSON Round-Trip**
    - **Validates: Requirements 10.3**
    - Class: `GeneratedTimeframeRecordProperties` in `UnitTests/TickImport/`
    - Generate arbitrary valid GeneratedTimeframeRecord instances, serialize to JSON, deserialize, assert equivalence
  - [x]* 15.10 Write property test: Work Queue Excludes Weekend Hours
    - **Property 10: Work Queue Excludes Weekend Hours**
    - **Validates: Requirements 13.4, 13.2**
    - Class: `DownloadWorkQueueProperties` in `UnitTests/TickImport/`
    - Generate arbitrary date ranges, verify zero Saturday/Sunday entries and count == tradingDays × 24

- [x] 16. Unit tests
  - [x]* 16.1 Write `TickImportServiceTests` in `UnitTests/TickImport/`
    - Test: Start creates Running record (Req 1.1)
    - Test: Rejects start >= end (Req 1.7)
    - Test: Rejects unsupported symbol (Req 1.8)
    - Test: Rejects concurrent import (Req 1.9, 12.1)
    - Test: Cancellation sets Cancelled status (Req 1.6)
    - Test: Network failure sets Failed status (Req 1.5)
    - Test: Completion records tick count (Req 1.4)
    - Test: All days cached → immediate completion (Req 3.3)
    - Test: Startup recovery resets Running to Failed (Req 9.3, 12.4)
    - Mock ITickCacheService, ITickImportRepository, DukascopyTickDownloader
    - _Requirements: 1.1, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 3.3, 9.3, 12.1, 12.4_
  - [x]* 16.2 Write `TimeframeGeneratorServiceTests` in `UnitTests/TickImport/`
    - Test: Generates correct output filename (Req 5.1)
    - Test: Registers DataFileRecord on completion (Req 4.5)
    - Test: Creates GeneratedTimeframeRecord (Req 4.6)
    - Test: Overwrites existing file and updates record (Req 5.2)
    - Test: Rejects concurrent generation for same import (Req 12.2)
    - Test: Empty windows produce no bars (Req 4.9)
    - Test: Atomic write via temp file (Req 5.3, 12.3)
    - Mock ITickCacheService, IGeneratedTimeframeRepository, IDataFileRepository, ITickImportRepository
    - _Requirements: 4.5, 4.6, 4.9, 5.1, 5.2, 5.3, 12.2, 12.3_
  - [x]* 16.3 Write `TickCachePathTests` in `UnitTests/TickImport/`
    - Test: Path follows expected pattern `{CacheDir}/{Symbol}/ticks/{yyyy}/{MM}/{dd}.csv` (Req 2.1)
    - Test: Cache files not registered as DataFileRecords (Req 2.6, 11.2)
    - _Requirements: 2.1, 2.6, 11.2_

- [x] 17. Integration tests
  - [x]* 17.1 Write `JsonTickImportRepositoryTests` in `IntegrationTests/TickImport/`
    - CRUD operations against temp directory
    - _Requirements: 9.2_
  - [x]* 17.2 Write `JsonGeneratedTimeframeRepositoryTests` in `IntegrationTests/TickImport/`
    - CRUD operations against temp directory
    - _Requirements: 10.2_
  - [x]* 17.3 Write `TickCacheServiceTests` in `IntegrationTests/TickImport/`
    - Write and read tick files from disk using temp directory
    - _Requirements: 2.1, 2.2_
  - [x]* 17.4 Write `TickImportFlowTests` in `IntegrationTests/TickImport/`
    - Full import with mocked HTTP → cache files created
    - _Requirements: 1.3, 1.4_
  - [x]* 17.5 Write `TimeframeGenerationFlowTests` in `IntegrationTests/TickImport/`
    - End-to-end: cached ticks → generated CSV → DataFileRecord
    - _Requirements: 4.1, 4.4, 4.5, 4.6_
  - [x]* 17.6 Write `DeletionFlowTests` in `IntegrationTests/TickImport/`
    - Delete generated file removes records, keeps tick cache
    - _Requirements: 11.3_

- [x] 18. Final checkpoint - Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each layer is complete
- Property tests validate universal correctness properties from the design document
- Unit tests use Moq for all external dependencies (UnitTests references Application and Core only)
- Integration tests use real file system with temp directories and mocked HTTP
- The existing `MarketDataImportService` and `DukascopyDataProvider` remain untouched
