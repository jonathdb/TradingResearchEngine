# Requirements Document

## Introduction

This feature enables the TradingResearchEngine to consume CSV bar-data exports from QuantDataManager (QDM) without any manual format conversion. QDM exports use a MetaTrader 4 variant with dot-separated dates (`yyyy.MM.dd`) and no seconds in the time field (`HH:mm`). The integration is folder-based: a configurable watch directory is scanned for QDM CSV files, which appear in the Data Files page alongside existing data. The Convert action transforms them into engine-format CSV and writes the output to the engine's DataDirectory.

No changes are required to the backtesting core, CsvDataProvider, DataProviderFactory, or any strategy/research workflow.

## Glossary

- **Engine**: The TradingResearchEngine application
- **QDM**: QuantDataManager, a third-party tool that exports bar data as CSV files in MetaTrader 4 format with dot-separated dates
- **CsvFormatConverter**: The static class in Infrastructure that detects CSV source formats and converts them to the engine's standard schema (`Timestamp,Open,High,Low,Close,Volume`)
- **SourceFormat**: The enum within CsvFormatConverter that identifies the origin format of a CSV file
- **DetectFormat**: The method on CsvFormatConverter that inspects CSV content to determine its SourceFormat
- **DataFileService**: The Infrastructure service that discovers, inspects, validates, and converts CSV data files
- **AppSettings**: The settings record persisted to `settings.json` via SettingsService, containing directory paths and default configuration values
- **SettingsService**: The Infrastructure service that reads and writes AppSettings to a JSON file on disk
- **DataDirectory**: The engine's primary directory for CSV data files, configured in AppSettings
- **QdmWatchDirectory**: An optional secondary directory path where QDM exports its CSV files
- **Engine_Format**: The engine's canonical CSV schema: `Timestamp,Open,High,Low,Close,Volume` where Timestamp is ISO 8601 UTC
- **QDM_Format**: QDM's CSV export schema: `Date,Time,Open,High,Low,Close,Volume` where Date is `yyyy.MM.dd` and Time is `HH:mm`
- **Settings_Page**: The Settings.razor Blazor page that displays and allows editing of application settings
- **Data_Files_Page**: The Data.razor Blazor page that lists, previews, validates, and converts CSV data files

## Requirements

### Requirement 1: QDM Source Format Recognition

**User Story:** As a user, I want the engine to recognise QDM CSV exports as a distinct format, so that they are not misidentified as MetaTrader or Engine format and can be converted correctly.

#### Acceptance Criteria

1. THE SourceFormat enum SHALL include a `QuantDataManager` member that represents QDM CSV exports.
2. WHEN a CSV file has a header containing `Date` and `Time` columns (and not `Timestamp`) and the first data row contains a dot-separated date (`yyyy.MM.dd`), THE DetectFormat method SHALL return `SourceFormat.QuantDataManager`.
3. WHEN a CSV file has a header containing `Date` and `Time` columns (and not `Timestamp`) and the first data row contains a dash-separated date (`yyyy-MM-dd`), THE DetectFormat method SHALL return `SourceFormat.MetaTrader`.
4. THE DetectFormat method SHALL accept the full lines array (header plus data rows) instead of only the header line, so that the first data row can be inspected for date-separator disambiguation.

### Requirement 2: QDM to Engine Format Conversion

**User Story:** As a user, I want to convert QDM CSV files to engine format with a single click, so that I can use QDM-exported data in backtests without manual editing.

#### Acceptance Criteria

1. WHEN a line from a QDM CSV file is converted, THE CsvFormatConverter SHALL replace dot separators in the date field with dashes (`yyyy.MM.dd` to `yyyy-MM-dd`).
2. WHEN a line from a QDM CSV file is converted, THE CsvFormatConverter SHALL append `:00` to the time field to produce `HH:mm:ss`.
3. WHEN a line from a QDM CSV file is converted, THE CsvFormatConverter SHALL combine the date and time into an ISO 8601 UTC timestamp in the format produced by the `O` format specifier.
4. WHEN a line from a QDM CSV file is converted, THE CsvFormatConverter SHALL preserve the Open, High, Low, Close, and Volume values from the source row without modification.
5. WHEN a QDM CSV line contains fewer than seven comma-separated fields, THE CsvFormatConverter SHALL skip that line (return null from ConvertLine).
6. THE CsvFormatConverter SHALL output a header of `Timestamp,Open,High,Low,Close,Volume` for converted QDM files.

### Requirement 3: Converted File Validity

**User Story:** As a user, I want converted QDM files to pass the existing schema validation, so that I can use them in the Strategy Builder without additional steps.

#### Acceptance Criteria

1. WHEN a QDM CSV file is converted to engine format, THE converted file SHALL pass the existing ValidateSchema check on DataFileService without any modifications to the validator.
2. FOR ALL valid QDM CSV rows, converting to engine format and then parsing the Timestamp field with `DateTimeOffset.Parse` using `InvariantCulture` SHALL produce a valid DateTimeOffset (round-trip property).

### Requirement 4: QDM Watch Directory Setting

**User Story:** As a user, I want to configure an optional QDM watch directory, so that the engine can discover QDM exports from a folder of my choosing.

#### Acceptance Criteria

1. THE AppSettings record SHALL include a nullable `QdmWatchDirectory` property of type `string?`.
2. WHEN `QdmWatchDirectory` is not present in the persisted settings JSON, THE SettingsService SHALL deserialise AppSettings with `QdmWatchDirectory` set to null without error.
3. WHEN `QdmWatchDirectory` is set to a non-empty string and saved, THE SettingsService SHALL persist the value to the settings JSON file.
4. WHEN `QdmWatchDirectory` is set to null or an empty string, THE AppSettings.Default SHALL use null as the default value for `QdmWatchDirectory`.

### Requirement 5: QDM Watch Directory File Discovery

**User Story:** As a user, I want files in my QDM watch directory to appear in the Data Files table, so that I can preview, validate, and convert them from the same page as my other data files.

#### Acceptance Criteria

1. WHEN `QdmWatchDirectory` is configured with a non-empty path and the directory exists, THE DataFileService.ListFiles method SHALL include CSV files from that directory in the returned list.
2. WHEN a CSV file in QdmWatchDirectory has the same file name as a file already found in DataDirectory, THE DataFileService.ListFiles method SHALL keep the DataDirectory version and exclude the QdmWatchDirectory duplicate.
3. WHEN `QdmWatchDirectory` is null, empty, or points to a non-existent directory, THE DataFileService.ListFiles method SHALL return results identical to the current behaviour (DataDirectory and samples only).
4. THE DataFileService.ListFiles method SHALL scan only `*.csv` files in QdmWatchDirectory (no recursive subdirectory scan).

### Requirement 6: Conversion Output Location

**User Story:** As a user, I want converted QDM files to be written to the engine's DataDirectory, so that my QDM export folder remains unmodified and the converted files are immediately available for backtesting.

#### Acceptance Criteria

1. WHEN the Convert action is invoked on a file originating from QdmWatchDirectory, THE DataFileService SHALL write the converted output file to DataDirectory.
2. WHEN the Convert action is invoked on a file originating from QdmWatchDirectory, THE DataFileService SHALL leave the original file in QdmWatchDirectory unmodified.

### Requirement 7: Settings Page — QDM Watch Directory Field

**User Story:** As a user, I want to view and edit the QDM Watch Directory setting from the Settings page, so that I can configure the path without manually editing JSON files.

#### Acceptance Criteria

1. THE Settings_Page SHALL display a text input field labelled "QDM Watch Directory" in the Storage Paths section.
2. THE Settings_Page SHALL indicate that the QDM Watch Directory field is optional (e.g., via helper text or placeholder).
3. WHEN the user enters a path in the QDM Watch Directory field and saves, THE Settings_Page SHALL persist the value to AppSettings via SettingsService.
4. WHEN the user clears the QDM Watch Directory field and saves, THE Settings_Page SHALL persist a null value for QdmWatchDirectory in AppSettings.

### Requirement 8: Data Files Page — Empty State Message Update

**User Story:** As a user, I want the empty-state message on the Data Files page to mention QDM as a data source option, so that I am aware of the QDM integration when no files are present.

#### Acceptance Criteria

1. WHEN no CSV files are found in any scanned directory, THE Data_Files_Page SHALL display an empty-state message that mentions QuantDataManager as a data source option alongside the existing Dukascopy mention.
