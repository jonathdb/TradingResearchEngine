# Bugfix Design Document

## Technical Context

### Affected Components

The bug spans the Infrastructure layer. Seven service registrations in `ServiceCollectionExtensions` hardcode paths to `%LOCALAPPDATA%\TradingResearchEngine\<subfolder>`, and two data providers (`DukascopyDataProvider`, `DukascopyImportProvider`) use hardcoded `static readonly` AppData cache paths. `CsvDataProvider` propagates a raw `FileNotFoundException` when a file is missing.

### Files to Modify

| File | Change |
|------|--------|
| `src/TradingResearchEngine.Infrastructure/ServiceCollectionExtensions.cs` | Replace all `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` paths with project-relative `./data/` subdirectories |
| `src/TradingResearchEngine.Infrastructure/DataProviders/DataFileService.cs` | Change default `_dataDir` from AppData to `Path.Combine(Directory.GetCurrentDirectory(), "data")` |
| `src/TradingResearchEngine.Infrastructure/DataProviders/DukascopyDataProvider.cs` | Replace `static readonly CacheDir` with an instance field defaulting to `Path.Combine(Directory.GetCurrentDirectory(), "data", "dukascopy-cache")`, accept optional `cacheDir` constructor parameter |
| `src/TradingResearchEngine.Infrastructure/DataProviders/CsvDataProvider.cs` | Wrap `StreamReader` construction in a file-existence check; throw a descriptive `FileNotFoundException` with the resolved path and guidance |
| `src/TradingResearchEngine.Infrastructure/MarketData/DukascopyImportProvider.cs` | Change default `_cacheDir` from AppData to `Path.Combine(Directory.GetCurrentDirectory(), "data", "dukascopy-day-cache")` |
| `src/TradingResearchEngine.Infrastructure/Settings/SettingsService.cs` | No code change needed — `AppSettings.Default` already uses `"data"` and `"exports"` as relative defaults |

### Project-Relative Directory Layout

After the fix, all data lives under `./data/` relative to the working directory:

```
./data/
  dukascopy-cache/          ← DukascopyDataProvider per-day + aggregated cache
  dukascopy-day-cache/      ← DukascopyImportProvider per-day cache
  datafiles/                ← DataFileRecord JSON metadata
  strategies/               ← StrategyIdentity + version JSON
  studies/                  ← StudyRecord JSON
  exports/                  ← Markdown/CSV/JSON report exports
  imports/                  ← MarketDataImport records
  prop-firm-evaluations/    ← PropFirmEvaluationRecord JSON
  settings.json             ← Application settings
  firms/                    ← Already exists at ./data/firms/ (PropFirmPackLoader)
```

## Design

### 1. DataFileService Default Path Change

**Current:** Constructor defaults to `%LOCALAPPDATA%\TradingResearchEngine\Data\`.

**Fix:** Default to `Path.Combine(Directory.GetCurrentDirectory(), "data")`.

```csharp
public DataFileService(string? dataDir = null)
{
    _dataDir = dataDir ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
    if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
}
```

No fallback. If the directory doesn't exist, it gets created. If a specific file isn't there, it simply hasn't been downloaded yet.

### 2. DukascopyDataProvider Configurable Cache

**Current:** `static readonly CacheDir` hardcoded to AppData.

**Fix:** Convert to an instance field. Accept an optional `cacheDir` parameter in the constructor. Default to `./data/dukascopy-cache/`. Update `GetAggregatedCachePath` to use the instance field instead of the static.

```csharp
private readonly string _cacheDir;

public DukascopyDataProvider(
    HttpClient httpClient,
    ILogger<DukascopyDataProvider> logger,
    DukascopyPriceType priceType = DukascopyPriceType.Bid,
    string? cacheDir = null)
{
    _httpClient = httpClient;
    _logger = logger;
    _priceType = priceType;
    _cacheDir = cacheDir ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "dukascopy-cache");
    Directory.CreateDirectory(_cacheDir);
}
```

All references to the old `CacheDir` static field are replaced with `_cacheDir`. The `GetAggregatedCachePath` method becomes an instance method.

### 3. DukascopyImportProvider Default Cache Path

**Current:** Defaults to `%LOCALAPPDATA%\TradingResearchEngine\DukascopyDayCache`.

**Fix:** Default to `Path.Combine(Directory.GetCurrentDirectory(), "data", "dukascopy-day-cache")`.

```csharp
_cacheDir = cacheDir ?? Path.Combine(Directory.GetCurrentDirectory(), "data", "dukascopy-day-cache");
```

### 4. CsvDataProvider Descriptive Error Message

**Current:** `new StreamReader(_filePath)` throws a raw `FileNotFoundException`.

**Fix:** Check file existence before opening. Throw a descriptive `FileNotFoundException` with guidance.

```csharp
private StreamReader OpenFileOrThrow()
{
    if (!File.Exists(_filePath))
    {
        throw new FileNotFoundException(
            $"Data file not found: '{_filePath}'. " +
            "Ensure the file exists in the project data directory (./data/) " +
            "or check the DataProvider.FilePath configuration.",
            _filePath);
    }
    return new StreamReader(_filePath);
}
```

Both `GetBars` and `GetTicks` call `OpenFileOrThrow()` instead of `new StreamReader(_filePath)`.

### 5. ServiceCollectionExtensions Path Consolidation

**Current:** Seven separate `Path.Combine(Environment.GetFolderPath(...), "TradingResearchEngine", ...)` calls.

**Fix:** Introduce a local helper that resolves project-relative paths under `./data/`:

```csharp
static string DataSubDir(string subfolder)
    => Path.Combine(Directory.GetCurrentDirectory(), "data", subfolder);
```

Replace each registration:

| Service | Old Path | New Path |
|---------|----------|----------|
| `IStrategyRepository` | `%LOCALAPPDATA%/.../Strategies` | `./data/strategies` |
| `IStudyRepository` | `%LOCALAPPDATA%/.../Studies` | `./data/studies` |
| `SettingsService` | `%LOCALAPPDATA%/.../settings.json` | `./data/settings.json` |
| `IDataFileRepository` | `%LOCALAPPDATA%/.../DataFiles` | `./data/datafiles` |
| `IReportExporter` | `%LOCALAPPDATA%/.../Exports` | `./data/exports` |
| `IPropFirmEvaluationRepository` | `%LOCALAPPDATA%/.../PropFirmEvaluations` | `./data/prop-firm-evaluations` |
| `IMarketDataImportRepository` | `%LOCALAPPDATA%/.../Imports` | `./data/imports` |

The `DukascopyDataProvider` factory registration also passes the cache directory:

```csharp
private DukascopyDataProvider CreateDukascopyProvider(Dictionary<string, object> options)
{
    var client = _httpClientFactory?.CreateClient("Dukascopy") ?? new HttpClient();
    return new DukascopyDataProvider(client, _loggerFactory.CreateLogger<DukascopyDataProvider>());
}
```

The constructor default handles the path, so `DataProviderFactory` needs no change.

### 6. DukascopyImportProvider Registration

The `ServiceCollectionExtensions` registration for `DukascopyImportProvider` passes the new cache directory:

```csharp
var cacheDir = Path.Combine(Directory.GetCurrentDirectory(), "data", "dukascopy-day-cache");
return new MarketData.DukascopyImportProvider(httpFactory.CreateClient(), logger, cacheDir);
```

Or rely on the constructor default (which is the same path).

## Correctness Properties

### Fix Checking

These verify the bug is fixed for inputs that previously triggered the defect.

**Property F1: DataFileService defaults to project-relative path**
- Requirement: 2.2
- GIVEN `DataFileService` is constructed without an explicit `dataDir`
- THEN `DataDirectory` ends with the platform path separator followed by `data`
- AND does not contain `AppData` or `LocalApplicationData`

**Property F2: CsvDataProvider throws descriptive error on missing file**
- Requirement: 2.1, 2.5
- GIVEN a `CsvDataProvider` with a non-existent file path
- WHEN `GetBars` is called
- THEN a `FileNotFoundException` is thrown
- AND the message contains the file path
- AND the message contains guidance text ("data directory" or "configuration")

**Property F3: DukascopyDataProvider uses project-relative cache**
- Requirement: 2.3
- GIVEN a `DukascopyDataProvider` constructed without an explicit `cacheDir`
- THEN the cache directory path ends with `data/dukascopy-cache` (or platform equivalent)
- AND does not contain `AppData` or `LocalApplicationData`

### Preservation Checking

These verify existing behavior is unchanged for non-buggy inputs.

**Property P1: DataFileService explicit dataDir is respected**
- Requirement: 3.1
- GIVEN `DataFileService` is constructed with an explicit `dataDir` of `/custom/path`
- THEN `DataDirectory` equals `/custom/path`

**Property P2: CsvDataProvider reads valid files identically**
- Requirement: 3.3
- GIVEN a valid CSV file with known bar data
- WHEN `CsvDataProvider.GetBars` is called
- THEN the returned bars match the expected values exactly

**Property P3: DukascopyHelpers cache format unchanged**
- Requirement: 3.2
- GIVEN a list of `BarRecord` values
- WHEN `DukascopyHelpers.SaveToCsv` writes them and `LoadFromCsv` reads them back
- THEN the round-tripped bars match the originals

## Bug Condition (Formal)

```pascal
FUNCTION isBugCondition(X)
  INPUT: X of type DataPathResolution
  OUTPUT: boolean
  
  // Returns true when the system attempts to resolve a data path
  // using the default (no explicit override) and the file does not
  // exist at the hardcoded %LOCALAPPDATA% location
  RETURN X.explicitPathOverride = NULL
     AND X.fileExistsAtAppData = FALSE
END FUNCTION
```

```pascal
// Property: Fix Checking — Project-Relative Default Paths
FOR ALL X WHERE isBugCondition(X) DO
  result ← resolveDataPath'(X)
  ASSERT result.resolvedPath STARTS_WITH currentWorkingDirectory + "/data/"
     AND NOT contains(result.resolvedPath, "AppData")
END FOR
```

```pascal
// Property: Fix Checking — Descriptive Error on Missing File
FOR ALL X WHERE isBugCondition(X) AND NOT fileExists(X.resolvedPath) DO
  error ← readFile'(X.resolvedPath)
  ASSERT error IS FileNotFoundException
     AND contains(error.message, X.resolvedPath)
     AND contains(error.message, "data directory")
END FOR
```

```pascal
// Property: Preservation Checking — Explicit Paths Unchanged
FOR ALL X WHERE NOT isBugCondition(X) DO
  ASSERT resolveDataPath(X) = resolveDataPath'(X)
END FOR
```
