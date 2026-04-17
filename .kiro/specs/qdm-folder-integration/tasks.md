# Implementation Plan: QDM Folder Integration

## Overview

Add QuantDataManager (QDM) CSV import support to the TradingResearchEngine. This involves extending `CsvFormatConverter` with a new `QuantDataManager` source format, updating `DetectFormat` to accept a lines array for date-separator disambiguation, adding a configurable `QdmWatchDirectory` to `AppSettings`, merging QDM files into `DataFileService.ListFiles()`, and exposing the setting in the Settings page. No changes to the backtesting core.

## Tasks

- [x] 1. Extend CsvFormatConverter with QDM format support
  - [x] 1.1 Add `QuantDataManager` member to the `SourceFormat` enum
    - Add the new enum member after `MetaTrader` with XML doc comment describing the QDM CSV format
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/CsvFormatConverter.cs`
    - _Requirements: 1.1_

  - [x] 1.2 Change `DetectFormat` signature to accept `string[] lines` and add disambiguation logic
    - Rename existing `DetectFormat(string headerLine)` to the new `DetectFormat(string[] lines)` signature
    - Extract header from `lines[0]`; keep all existing detection logic unchanged
    - In the MetaTrader branch (`lower.Contains("date") && lower.Contains("time") && !lower.Contains("timestamp")`), peek at `lines[1]` to check if the 5th character is `.` (QDM) or `-` (MetaTrader)
    - Add a backward-compatible overload `DetectFormat(string headerLine)` that wraps the single string in a one-element array and delegates to the new overload
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/CsvFormatConverter.cs`
    - _Requirements: 1.2, 1.3, 1.4_

  - [x] 1.3 Add `ConvertQuantDataManager` private method and wire into `ConvertLine`
    - Implement `ConvertQuantDataManager(string line)`: split on comma, replace dots with dashes in date, append `:00` to time, combine into ISO 8601 UTC timestamp via `DateTimeOffset.Parse` with `InvariantCulture`, preserve OHLCV fields
    - Add `SourceFormat.QuantDataManager => ConvertQuantDataManager(line)` case to the `ConvertLine` switch
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/CsvFormatConverter.cs`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

  - [x] 1.4 Update `Convert` method to pass full lines array to `DetectFormat`
    - Change `DetectFormat(lines[0])` to `DetectFormat(lines)` in the `Convert` method's auto-detect path
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/CsvFormatConverter.cs`
    - _Requirements: 1.4, 2.6_

- [x] 2. Add unit tests for CsvFormatConverter QDM support
  - [x] 2.1 Write example-based unit tests for QDM detection and conversion
    - Create test class `CsvFormatConverterQdmTests` in `src/TradingResearchEngine.UnitTests/V3/`
    - `DetectFormat_QdmHeader_ReturnsQuantDataManager` — lines array with QDM header + dot-separated data row
    - `DetectFormat_Mt5Header_StillReturnsMetaTrader` — regression: lines array with MT5 header + dash-separated data row
    - `DetectFormat_SingleLineOnly_DefaultsToMetaTrader` — backward compat: single-element array with Date/Time header, no data row
    - `ConvertQuantDataManager_KnownRow_ProducesCorrectTimestamp` — concrete conversion with known input/output
    - `Convert_QdmFile_OutputHeaderIsEngineFormat` — full file conversion, verify header is `Timestamp,Open,High,Low,Close,Volume`
    - `ConvertLine_QdmMalformedRow_ReturnsNull` — line with fewer than 7 fields returns null
    - _Requirements: 1.2, 1.3, 2.1, 2.2, 2.3, 2.4, 2.5, 2.6_

  - [x] 2.2 Write property test: Format detection disambiguation (Property 1)
    - **Property 1: Format detection disambiguation**
    - **Validates: Requirements 1.2, 1.3**
    - Create test class `CsvFormatConverterQdmProperties` in `src/TradingResearchEngine.UnitTests/V3/`
    - `// Feature: qdm-folder-integration, Property 1: Format detection disambiguation`
    - `[Property(MaxTest = 100)]` — generate random valid dates (year 1970–2030), random OHLCV decimals, build CSV lines array with either dot or dash separator, assert correct `SourceFormat` enum value

  - [x] 2.3 Write property test: QDM conversion timestamp round-trip (Property 2)
    - **Property 2: QDM conversion timestamp round-trip**
    - **Validates: Requirements 2.1, 2.2, 2.3, 3.2**
    - `// Feature: qdm-folder-integration, Property 2: QDM conversion timestamp round-trip`
    - `[Property(MaxTest = 100)]` — generate random valid QDM date (`yyyy.MM.dd`) and time (`HH:mm`), random OHLCV, convert via `ConvertQuantDataManager`, parse resulting timestamp with `DateTimeOffset.Parse` using `InvariantCulture`, compare year/month/day/hour/minute

  - [x] 2.4 Write property test: QDM conversion OHLCV preservation (Property 3)
    - **Property 3: QDM conversion OHLCV preservation**
    - **Validates: Requirements 2.4**
    - `// Feature: qdm-folder-integration, Property 3: QDM conversion OHLCV preservation`
    - `[Property(MaxTest = 100)]` — generate random decimal OHLCV values and valid QDM date/time, convert, split output, compare OHLCV fields are string-equal after trimming

  - [x] 2.5 Write property test: Malformed line rejection (Property 4)
    - **Property 4: Malformed line rejection**
    - **Validates: Requirements 2.5**
    - `// Feature: qdm-folder-integration, Property 4: Malformed line rejection`
    - `[Property(MaxTest = 100)]` — generate random strings with 0–6 comma-separated fields, assert `ConvertLine` with `SourceFormat.QuantDataManager` returns null

- [x] 3. Checkpoint — Verify CsvFormatConverter changes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Add `QdmWatchDirectory` to AppSettings and update SettingsService
  - [x] 4.1 Add nullable `QdmWatchDirectory` property to `AppSettings` record
    - Add `string? QdmWatchDirectory` parameter after `ExportDirectory` in the record constructor
    - Set default to `null` in `AppSettings.Default`
    - File: `src/TradingResearchEngine.Infrastructure/Settings/SettingsService.cs`
    - _Requirements: 4.1, 4.2, 4.4_

  - [x] 4.2 Write unit tests for AppSettings QdmWatchDirectory
    - Add tests to `CsvFormatConverterQdmTests` or a new `AppSettingsQdmTests` class in `src/TradingResearchEngine.UnitTests/V3/`
    - `AppSettings_Default_QdmWatchDirectoryIsNull` — verify default value is null
    - `AppSettings_OldJsonWithoutQdmField_DeserializesWithNull` — deserialize JSON missing the field, verify null
    - _Requirements: 4.1, 4.2, 4.4_

  - [x] 4.3 Write property test: AppSettings QdmWatchDirectory persistence round-trip (Property 5)
    - **Property 5: AppSettings QdmWatchDirectory persistence round-trip**
    - **Validates: Requirements 4.3**
    - `// Feature: qdm-folder-integration, Property 5: AppSettings QdmWatchDirectory persistence round-trip`
    - `[Property(MaxTest = 100)]` — generate random non-empty path strings, save via `SettingsService.Save` to a temp file, load via `SettingsService.Load`, compare `QdmWatchDirectory` values

- [x] 5. Update DataFileService to support QDM watch directory
  - [x] 5.1 Add `qdmWatchDir` constructor parameter to `DataFileService`
    - Add optional `string? qdmWatchDir = null` parameter to the constructor
    - Store as `_qdmWatchDir` field
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/DataFileService.cs`
    - _Requirements: 5.1, 5.3_

  - [x] 5.2 Update `ListFiles()` to merge QDM watch directory files
    - After scanning `DataDirectory` and `samples/data`, scan `_qdmWatchDir` if non-null, non-empty, and directory exists
    - Only scan `*.csv` files (no recursive subdirectory scan)
    - Deduplicate by file name — `DataDirectory` files take precedence
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/DataFileService.cs`
    - _Requirements: 5.1, 5.2, 5.3, 5.4_

  - [x] 5.3 Update `AnalyzeFile()` and `ValidateSchema()` to pass lines array to `DetectFormat`
    - In `AnalyzeFile`: read first two lines (header + first data row), pass as array to `DetectFormat`
    - In `ValidateSchema`: read first two lines, pass as array to `DetectFormat`
    - File: `src/TradingResearchEngine.Infrastructure/DataProviders/DataFileService.cs`
    - _Requirements: 1.4_

  - [x] 5.4 Update DI registration to pass `QdmWatchDirectory` from settings
    - Update the `DataFileService` singleton registration in `ServiceCollectionExtensions` to load `AppSettings` via `SettingsService` and pass `settings.QdmWatchDirectory` to the constructor
    - File: `src/TradingResearchEngine.Infrastructure/ServiceCollectionExtensions.cs`
    - _Requirements: 5.1_

- [x] 6. Add integration tests for DataFileService QDM watch directory
  - [x] 6.1 Write integration tests for QDM watch directory file discovery
    - Create test class `DataFileServiceQdmTests` in `src/TradingResearchEngine.IntegrationTests/DataProviders/`
    - `DataFileService_QdmWatchDirectory_ListsQdmFiles` — create temp directory with QDM CSVs, verify they appear in `ListFiles()`
    - `DataFileService_QdmWatchDirectory_DeduplicatesByFileName` — same filename in both dirs, DataDirectory version wins
    - `DataFileService_QdmWatchDirectory_NullOrMissing_NoChange` — verify no-op when not configured
    - `DataFileService_ConvertQdmFile_OutputInDataDirectory` — convert from watch dir, verify output location is DataDirectory
    - `DataFileService_ConvertQdmFile_OriginalUnmodified` — verify source file unchanged after conversion
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 6.1, 6.2_

- [x] 7. Checkpoint — Verify DataFileService and AppSettings changes
  - Ensure all tests pass, ask the user if questions arise.

- [x] 8. Update Settings.razor to expose QDM Watch Directory field
  - [x] 8.1 Add QDM Watch Directory text field to Settings page
    - Inject `SettingsService` into `Settings.razor`
    - Add a `MudTextField` bound to `QdmWatchDirectory` in the "Storage Paths" table section
    - Include helper text: "Optional — path to QDM export folder"
    - Add a Save button that calls `SettingsService.Save()` with the updated `AppSettings`
    - Display "(not configured)" when the value is null or empty
    - File: `src/TradingResearchEngine.Web/Components/Pages/Settings.razor`
    - _Requirements: 7.1, 7.2, 7.3, 7.4_

- [x] 9. Update Data.razor empty-state message to mention QDM
  - Update the `MudAlert` empty-state message to include QDM as a data source option
  - New text: "No CSV files found. Place CSV files in the data directory, use Dukascopy to download data, or configure a QDM Watch Directory in Settings to import QuantDataManager exports."
  - File: `src/TradingResearchEngine.Web/Components/Pages/Data.razor`
  - _Requirements: 8.1_

- [x] 10. Final checkpoint — Ensure all tests pass
  - Run full test suite to confirm no regressions across the solution
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each major component
- Property tests use FsCheck.Xunit with `[Property(MaxTest = 100)]` per testing standards
- Unit and property tests go in `UnitTests` project; integration tests go in `IntegrationTests` project
- The design uses C# throughout — no language selection needed
