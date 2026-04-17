# Implementation Plan

- [x] 1. Write bug condition exploration test
  - **Property 1: Bug Condition** - Default Data Paths Resolve to AppData (Non-Portable)
  - **CRITICAL**: This test MUST FAIL on unfixed code — failure confirms the bug exists
  - **DO NOT attempt to fix the test or the code when it fails**
  - **NOTE**: This test encodes the expected behavior — it will validate the fix when it passes after implementation
  - **GOAL**: Surface counterexamples that demonstrate default paths point to `%LOCALAPPDATA%` instead of project-relative `./data/`
  - **Scoped PBT Approach**: Scope the property to the concrete failing cases — constructing `DataFileService`, `DukascopyDataProvider`, and `DukascopyImportProvider` with no explicit path override
  - Test file: `src/TradingResearchEngine.IntegrationTests/DataProviders/DefaultPathBugConditionTests.cs`
  - Property: For `DataFileService(null)`, assert `DataDirectory` ends with platform-appropriate `data` path segment AND does not contain `AppData` or `LocalApplicationData`
  - Property: For `DukascopyDataProvider` constructed without `cacheDir`, assert the cache directory ends with `data/dukascopy-cache` (or platform equivalent) AND does not contain `AppData`
  - Property: For `CsvDataProvider` given a non-existent file path, assert `GetBars` throws `FileNotFoundException` with a message containing the file path AND guidance text (e.g., "data directory" or "configuration")
  - Run test on UNFIXED code
  - **EXPECTED OUTCOME**: Test FAILS (this is correct — it proves the bug exists: paths resolve to AppData, and CsvDataProvider throws a raw exception without guidance)
  - Document counterexamples found to understand root cause
  - Mark task complete when test is written, run, and failure is documented
  - _Requirements: 1.1, 1.2, 1.3, 1.5_

- [x] 2. Write preservation property tests (BEFORE implementing fix)
  - **Property 2: Preservation** - Explicit Path Override and Valid CSV Read Behavior
  - **IMPORTANT**: Follow observation-first methodology
  - Test file: `src/TradingResearchEngine.IntegrationTests/DataProviders/PathPreservationTests.cs`
  - Observe: `DataFileService("/custom/path")` sets `DataDirectory` to `/custom/path` on unfixed code
  - Observe: `CsvDataProvider` reading a valid fixture CSV (`src/TradingResearchEngine.IntegrationTests/fixtures/bars.csv`) returns bars with correct OHLCV values
  - Observe: `DukascopyHelpers.SaveToCsv` + `LoadFromCsv` round-trips bar records identically
  - Write property-based test (FsCheck.Xunit, `[Property(MaxTest = 100)]`): For all non-empty strings `customDir`, `DataFileService(customDir).DataDirectory == customDir` (explicit override preserved)
  - Write property-based test: For all generated `BarRecord` lists, `SaveToCsv` then `LoadFromCsv` round-trips produce identical OHLCV and timestamp values (cache format unchanged)
  - Write example test: `CsvDataProvider` with the fixture CSV returns expected bar count and values
  - Verify tests pass on UNFIXED code
  - **EXPECTED OUTCOME**: Tests PASS (this confirms baseline behavior to preserve)
  - Mark task complete when tests are written, run, and passing on unfixed code
  - _Requirements: 3.1, 3.2, 3.3_

- [x] 3. Fix for non-portable AppData default paths and raw FileNotFoundException

  - [x] 3.1 Update `DataFileService` default path
    - Change default `_dataDir` from `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` to `Path.Combine(Directory.GetCurrentDirectory(), "data")`
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/DataFileService.cs`
    - _Bug_Condition: isBugCondition(X) where X.explicitPathOverride = NULL — default path resolves to AppData_
    - _Expected_Behavior: Default path resolves to `./data/` relative to working directory_
    - _Preservation: Explicit `dataDir` parameter continues to be used when provided_
    - _Requirements: 2.2, 3.1_

  - [x] 3.2 Update `DukascopyDataProvider` cache to instance field with project-relative default
    - Replace `static readonly CacheDir` with instance field `_cacheDir`
    - Add optional `cacheDir` constructor parameter defaulting to `Path.Combine(Directory.GetCurrentDirectory(), "data", "dukascopy-cache")`
    - Convert `GetAggregatedCachePath` from static to instance method
    - Update all references from `CacheDir` to `_cacheDir`
    - Ensure `Directory.CreateDirectory(_cacheDir)` is called in constructor
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/DukascopyDataProvider.cs`
    - _Bug_Condition: isBugCondition(X) where static CacheDir hardcodes AppData path_
    - _Expected_Behavior: Cache directory defaults to `./data/dukascopy-cache/` and is configurable_
    - _Preservation: Cache file format and directory structure unchanged (per-day CSV with symbol/priceType/year/month hierarchy)_
    - _Requirements: 2.3, 3.2_

  - [x] 3.3 Update `DukascopyImportProvider` default cache path
    - Change default `_cacheDir` from `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` to `Path.Combine(Directory.GetCurrentDirectory(), "data", "dukascopy-day-cache")`
    - File: `src/TradingResearchEngine.Infrastructure/MarketData/DukascopyImportProvider.cs`
    - _Bug_Condition: isBugCondition(X) where default cache path resolves to AppData_
    - _Expected_Behavior: Default cache path resolves to `./data/dukascopy-day-cache/`_
    - _Requirements: 2.3_

  - [x] 3.4 Add file-existence check to `CsvDataProvider` with descriptive error
    - Add private `OpenFileOrThrow()` method that checks `File.Exists(_filePath)` before opening
    - Throw `FileNotFoundException` with message including the file path and guidance text
    - Replace `new StreamReader(_filePath)` in both `GetBars` and `GetTicks` with `OpenFileOrThrow()`
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/CsvDataProvider.cs`
    - _Bug_Condition: isBugCondition(X) where X.fileExistsAtAppData = FALSE — raw FileNotFoundException propagated_
    - _Expected_Behavior: Descriptive FileNotFoundException with resolved path and guidance_
    - _Preservation: Valid CSV files continue to be read identically_
    - _Requirements: 2.1, 2.5, 3.3_

  - [x] 3.5 Update `ServiceCollectionExtensions` to use project-relative paths
    - Introduce local helper: `static string DataSubDir(string subfolder) => Path.Combine(Directory.GetCurrentDirectory(), "data", subfolder);`
    - Replace `IStrategyRepository` path: `./data/strategies`
    - Replace `IStudyRepository` path: `./data/studies`
    - Replace `SettingsService` path: `./data/settings.json`
    - Replace `IDataFileRepository` path: `./data/datafiles`
    - Replace `IReportExporter` path: `./data/exports`
    - Replace `IPropFirmEvaluationRepository` path: `./data/prop-firm-evaluations`
    - Replace `IMarketDataImportRepository` path: `./data/imports`
    - File: `src/TradingResearchEngine.Infrastructure/ServiceCollectionExtensions.cs`
    - _Bug_Condition: isBugCondition(X) where all 7 service registrations hardcode AppData paths_
    - _Expected_Behavior: All paths resolve under `./data/` relative to working directory_
    - _Requirements: 2.4_

  - [x] 3.6 Verify bug condition exploration test now passes
    - **Property 1: Expected Behavior** - Default Data Paths Are Project-Relative
    - **IMPORTANT**: Re-run the SAME test from task 1 — do NOT write a new test
    - The test from task 1 encodes the expected behavior
    - When this test passes, it confirms the expected behavior is satisfied
    - Run bug condition exploration test from step 1
    - **EXPECTED OUTCOME**: Test PASSES (confirms bug is fixed — all default paths are project-relative, CsvDataProvider throws descriptive error)
    - _Requirements: 2.1, 2.2, 2.3, 2.5_

  - [x] 3.7 Verify preservation tests still pass
    - **Property 2: Preservation** - Explicit Path Override and Valid CSV Read Behavior
    - **IMPORTANT**: Re-run the SAME tests from task 2 — do NOT write new tests
    - Run preservation property tests from step 2
    - **EXPECTED OUTCOME**: Tests PASS (confirms no regressions)
    - Confirm all tests still pass after fix (no regressions)

- [x] 4. Checkpoint - Ensure all tests pass
  - Run full test suite to confirm no regressions across the solution
  - Ensure all tests pass, ask the user if questions arise.
