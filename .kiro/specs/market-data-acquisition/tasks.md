# Implementation Plan — Market Data Acquisition Workflow

- [x] 1. Application layer domain types


  - [x] 1.1 Create `MarketDataImportStatus` enum in Application/MarketData


    - Values: Running, Completed, Failed, Cancelled
    - _Requirements: 6.2_
  - [x] 1.2 Create `MarketDataImportRecord` record in Application/MarketData


    - All fields from Requirement 6.1, implements IHasId
    - _Requirements: 6.1, 6.5_
  - [x] 1.3 Create `MarketSymbolInfo` record in Application/MarketData


    - Fields: Symbol, DisplayName, SupportedTimeframes (string array)
    - _Requirements: 5.2_
  - [x] 1.4 Create `CsvWriteResult` record in Application/MarketData


    - Fields: FilePath, Symbol, Timeframe, FirstBar, LastBar, BarCount





    - _Requirements: 8.4_
  - [x] 1.5 Write unit tests for MarketDataImportRecord JSON round-trip


    - _Requirements: 10.1_





- [x] 2. Application layer interfaces






  - [ ] 2.1 Create `IMarketDataProvider` interface in Application/MarketData
    - SourceName property, GetSupportedSymbolsAsync, DownloadToFileAsync


    - DownloadToFileAsync accepts IProgressReporter and CancellationToken
    - _Requirements: 5.1, 5.2, 5.3_
  - [ ] 2.2 Create `IMarketDataImportRepository` interface in Application/MarketData
    - Standard CRUD: GetAsync, ListAsync, SaveAsync, DeleteAsync
    - _Requirements: 6.3_

- [ ] 3. Infrastructure persistence
  - [x] 3.1 Implement `JsonMarketDataImportRepository` in Infrastructure/MarketData


    - JSON file store at imports/{importId}.json


    - _Requirements: 6.4_
  - [ ] 3.2 Write integration tests for JsonMarketDataImportRepository CRUD
    - _Requirements: 10.2_

- [ ] 4. Dukascopy import provider
  - [ ] 4.1 Extract shared download helpers from DukascopyDataProvider
    - Move Decompress, ParseCandles, Aggregate, PointSizes to a shared static helper class (DukascopyHelpers)
    - Keep existing DukascopyDataProvider functional by calling the shared helpers
    - _Requirements: 5.4, 11.1_
  - [ ] 4.2 Implement `DukascopyImportProvider` in Infrastructure/MarketData
    - Implements IMarketDataProvider with SourceName = "Dukascopy"
    - GetSupportedSymbolsAsync returns 15 symbols with display names and all 7 timeframes
    - DownloadToFileAsync: downloads minute BID candles, aggregates, writes canonical CSV to temp path
    - Includes a `CanonicalCsvWriter` helper method in the provider (or shared helper) for writing Timestamp,Open,High,Low,Close,Volume rows
    - Returns CsvWriteResult with metadata (FirstBar, LastBar, BarCount) derived from the written stream
    - Reports progress per day chunk via IProgressReporter
    - Retries transient HTTP failures (3 retries, exponential backoff)
    - Skips weekends
    - _Requirements: 9.1, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 4.1, 4.2, 4.3, 4.4, 4.5_
  - [ ] 4.3 Verify existing DukascopyDataProvider still works after helper extraction
    - Run existing backtest/provider tests to confirm no regression
    - _Requirements: 11.1_
  - [ ] 4.4 Write unit tests for DukascopyImportProvider
    - Test with fixture data (no real HTTP calls)
    - Verify canonical CSV output format, unsupported timeframe rejection, symbol validation
    - _Requirements: 10.1, 10.3_

- [x] 5. MarketDataImportService orchestration


  - [x] 5.1 Implement `MarketDataImportService` in Application/MarketData


    - Singleton, one-at-a-time concurrency guard
    - StartImportAsync: validate, create Running record, launch background task, return importId
    - Background task: call provider, create DataFileRecord, update import record
    - Temp-file-then-rename write pattern
    - CancelImport, GetActiveImport, OnProgress/OnCompleted events


    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5, 7.6_

  - [ ] 5.2 Implement startup recovery in MarketDataImportService
    - RecoverOnStartupAsync: reset Running → Failed, delete orphaned .tmp files
    - _Requirements: 7.7_


  - [ ] 5.3 Implement duplicate import detection
    - Check existing Completed imports with matching (source, symbol, timeframe, range)
    - Find and update existing DataFileRecord by OutputFileId from previous import


    - _Requirements: design duplicate handling_

  - [x] 5.4 Write unit tests for MarketDataImportService


    - Start creates Running record, completion creates DataFileRecord, failure sets Failed
    - Concurrency guard throws on second start, cancel sets Cancelled
    - Startup recovery resets Running to Failed

    - _Requirements: 10.1_
  - [ ] 5.5 Wire MarketDataImportService and DukascopyImportProvider into DI
    - Register in ServiceCollectionExtensions


    - Call RecoverOnStartupAsync on Web startup

    - _Requirements: 5.6_





- [x] 6. Web UI — Market Data screen


  - [x] 6.1 Create `MarketData.razor` page at /market-data

    - Import form: source selector, symbol selector, timeframe selector, date pickers, quick presets


    - Validation: inline errors for invalid range, unsupported symbol

    - Start Download button, disabled while import running
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7_
  - [ ] 6.2 Add import history list to MarketData.razor
    - Recent Imports section with status badges, progress bars, action buttons
    - Subscribe to OnProgress/OnCompleted events for live updates
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6_
  - [ ] 6.3 Add Market Data nav link to NavMenu.razor
    - Under SETTINGS group, above Data Files, with CloudDownload icon
    - _Requirements: 1.1, 11.5_

- [ ] 7. Data Files and Builder integration
  - [ ] 7.1 Add [Import from Market Source] button to Data Files page
    - Navigates to /market-data
    - _Requirements: 8.2_
  - [ ] 7.2 Verify Data Files page refreshes when import-created DataFileRecord is registered
    - Confirm existing reactive binding picks up new records, or add refresh wiring if needed
    - _Requirements: 8.1_
  - [ ] 7.3 Add helper link to Strategy Builder Step 2
    - "Can't find your data? Import market data →" below file selector
    - _Requirements: 8.3_

- [ ] 8. Integration tests
  - [ ] 8.1 Write end-to-end import test with mock provider
    - Mock IMarketDataProvider writes a known CSV, verify DataFileRecord created with Valid status
    - _Requirements: 10.2_
  - [ ] 8.2 Write failed import integration test
    - Mock IMarketDataProvider throws during download, verify import record persisted with Failed status and ErrorDetail
    - _Requirements: 10.2_
