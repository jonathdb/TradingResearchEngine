# Bugfix Requirements Document

## Introduction

Running a backtest throws a raw `FileNotFoundException` when the `CsvDataProvider` attempts to read a Dukascopy-downloaded CSV file from `%LOCALAPPDATA%\TradingResearchEngine\Data\`. This path is machine-specific, so data files downloaded on one machine are not available on another. The root cause is that `DataFileService`, `DukascopyDataProvider`, `ServiceCollectionExtensions`, and several other services hardcode their storage paths to `Environment.SpecialFolder.LocalApplicationData`, making the entire data layer non-portable.

The fix changes all default data directories to project-relative locations (e.g., `./data/`) so that data files live in the repo and travel with the source tree. There is no fallback to `%LOCALAPPDATA%` — if files are not present in the project-relative location, they simply don't exist yet. The fix also replaces the raw `FileNotFoundException` with a clear, actionable error message.

## Bug Analysis

### Current Behavior (Defect)

1.1 WHEN a backtest is run and the referenced CSV data file exists only in `%LOCALAPPDATA%\TradingResearchEngine\Data\` on a different machine THEN the system throws a raw `System.IO.FileNotFoundException` with the full absolute path, crashing the backtest run.

1.2 WHEN `DataFileService` is constructed without an explicit `dataDir` parameter THEN the system defaults to `%LOCALAPPDATA%\TradingResearchEngine\Data\`, which is machine-specific and not portable across environments.

1.3 WHEN `DukascopyDataProvider` caches downloaded data THEN the system stores it in a hardcoded `%LOCALAPPDATA%\TradingResearchEngine\DukascopyCache\` path via a `static readonly` field, which cannot be configured or overridden.

1.4 WHEN `ServiceCollectionExtensions.AddTradingResearchEngineInfrastructure` registers services (Strategies, Studies, DataFiles, Exports, Settings, Imports, PropFirmEvaluations) THEN the system hardcodes each storage path to `%LOCALAPPDATA%\TradingResearchEngine\<subfolder>`, making all persisted data machine-specific.

1.5 WHEN a `CsvDataProvider` is given a file path that does not exist THEN the system propagates a raw `FileNotFoundException` from `StreamReader` without any contextual error message indicating what file was expected or how to resolve the issue.

### Expected Behavior (Correct)

2.1 WHEN a backtest is run and the referenced CSV data file does not exist at the project-relative data directory THEN the system SHALL throw a descriptive error message that includes the file name, the resolved path searched, and guidance on how to obtain or place the file.

2.2 WHEN `DataFileService` is constructed without an explicit `dataDir` parameter THEN the system SHALL default to a project-relative directory (e.g., `./data/`) resolved from the current working directory, making data files portable with the project.

2.3 WHEN `DukascopyDataProvider` caches downloaded data THEN the system SHALL use a configurable cache directory that defaults to a project-relative location (e.g., `./data/dukascopy-cache/`), rather than a hardcoded `%LOCALAPPDATA%` path.

2.4 WHEN `ServiceCollectionExtensions.AddTradingResearchEngineInfrastructure` registers services THEN the system SHALL use project-relative default paths under `./data/` for all data storage directories (Strategies, Studies, DataFiles, Exports, Settings, Imports, PropFirmEvaluations), making the entire data layer portable.

2.5 WHEN a `CsvDataProvider` is given a file path that does not exist THEN the system SHALL throw a clear, user-friendly error message that includes the resolved file path and suggests checking the data directory configuration, rather than propagating a raw `FileNotFoundException`.

### Unchanged Behavior (Regression Prevention)

3.1 WHEN `DataFileService` is constructed with an explicit `dataDir` parameter THEN the system SHALL CONTINUE TO use that explicit path as the primary data directory.

3.2 WHEN `DukascopyDataProvider` downloads and caches data from Dukascopy's datafeed THEN the system SHALL CONTINUE TO cache data in per-day CSV files with the same directory structure and file format.

3.3 WHEN `CsvDataProvider` reads a valid CSV file that exists at the configured path THEN the system SHALL CONTINUE TO parse and return bar records identically to the current behavior.

3.4 WHEN `DataProviderFactory` creates a CSV provider with a relative file path THEN the system SHALL CONTINUE TO walk up directories to find the file using the existing `FindFileUpwards` logic.

3.5 WHEN the backtest engine runs with valid data files present THEN the system SHALL CONTINUE TO produce identical `BacktestResult` output — no numeric or behavioral changes to the engine pipeline.
