# Kiro Design Note — QuantDataManager (QDM) Folder Integration (Option A)

## Purpose

This note describes the minimal changes required to let the engine consume CSV exports from **QuantDataManager (QDM)** without leaving the application to perform any format conversion. The approach is entirely folder-based: QDM exports files into the engine's configured `DataDirectory`, and the existing **Data Files** page discovers, detects, previews, and converts them automatically. No new download logic, no new provider, no changes to the backtesting core.

This is intentionally scoped as the smallest viable QDM integration. It relies on the existing `CsvFormatConverter`, `DataFileService`, and `Data.razor` infrastructure, adding only what is missing: recognition of the QDM export format and a configurable second watch path for QDM's output folder.

---

## Background

QDM exports bar data as CSV files using the MetaTrader 4 bar format:

```
Date,Time,Open,High,Low,Close,Volume
2020.01.02,00:00,1.12100,1.12250,1.12000,1.12190,1234
```

Key characteristics:
- **Separator:** comma
- **Date format:** `yyyy.MM.dd` (dot-separated, not dash-separated)
- **Time format:** `HH:mm` (no seconds)
- **Column order:** `Date, Time, Open, High, Low, Close, Volume` — identical in column structure to the existing `MetaTrader` format in `CsvFormatConverter`, but with a different date separator and no seconds in the time field
- **File naming convention:** `SYMBOL_TIMEFRAME.csv` e.g. `EURUSD_H1.csv`, `GBPUSD_M15.csv`
- **No header variation:** QDM always writes the same header

The existing `MetaTrader` case in `CsvFormatConverter.ConvertMetaTrader()` calls `DateTimeOffset.Parse()` on `"{date} {time}"`. This works for `2020-01-02 00:00:00` (MT5 style) but **fails silently** for QDM's `2020.01.02 00:00` because `DateTimeOffset.Parse` does not recognise dot-separated date components in `InvariantCulture`. Detection also misses QDM files when the auto-detect logic falls through to `SourceFormat.Engine`, causing conversion to be skipped or data to be misread.

---

## What Needs to Change

### 1. Add `QuantDataManager` as a named `SourceFormat`

In `CsvFormatConverter.cs`, add a new enum member:

```csharp
/// <summary>
/// QuantDataManager export: Date,Time,Open,High,Low,Close,Volume
/// Date format is yyyy.MM.dd and Time format is HH:mm (no seconds).
/// </summary>
QuantDataManager,
```

### 2. Extend `DetectFormat` to recognise QDM headers

The current `MetaTrader` detection rule matches QDM structurally, but the converter then fails on the date format. Distinguish the two by peeking at the first data row:

```csharp
// In DetectFormat, update the existing MetaTrader check:
if (lower.Contains("date") && lower.Contains("time") && !lower.Contains("timestamp"))
{
    // Peek at first data row to distinguish QDM (yyyy.MM.dd) from MT5 (yyyy-MM-dd)
    if (lines.Length > 1 && lines[1].Length > 4 && lines[1][4] == '.')
        return SourceFormat.QuantDataManager;
    return SourceFormat.MetaTrader;
}
```

Update the `DetectFormat` signature to accept `string[] lines` instead of `string headerLine` so the peek is possible. All existing callers pass `lines[0]`; update them to pass `lines` directly.

### 3. Add `ConvertQuantDataManager` conversion method

```csharp
// QDM: Date,Time,Open,High,Low,Close,Volume
// Date is yyyy.MM.dd  Time is HH:mm
private static string ConvertQuantDataManager(string line)
{
    var p = line.Split(',');
    if (p.Length < 7) throw new FormatException();
    var datePart = p[0].Trim().Replace('.', '-');   // 2020.01.02 → 2020-01-02
    var timePart = p[1].Trim();                      // 00:00
    var ts = DateTimeOffset.Parse(
        $"{datePart}T{timePart}:00Z",
        CultureInfo.InvariantCulture);
    return $"{ts:O},{p[2].Trim()},{p[3].Trim()},{p[4].Trim()},{p[5].Trim()},{p[6].Trim()}";
}
```

Wire this into `ConvertLine`:

```csharp
SourceFormat.QuantDataManager => ConvertQuantDataManager(line),
```

### 4. Add a `QdmWatchDirectory` setting

In `AppSettings` (in `SettingsService.cs`), add one optional field:

```csharp
public sealed record AppSettings(
    string DataDirectory,
    string ExportDirectory,
    string? QdmWatchDirectory,          // ← new: null means not configured
    ExecutionRealismProfile DefaultRealismProfile,
    decimal DefaultInitialCash,
    decimal DefaultRiskFreeRate,
    string DefaultSizingPolicy)
{
    public static AppSettings Default { get; } = new(
        "data",
        "exports",
        null,                           // QdmWatchDirectory not set by default
        ExecutionRealismProfile.StandardBacktest,
        100_000m,
        0.02m,
        "PercentEquity");
}
```

### 5. Extend `DataFileService.ListFiles()` to include the QDM watch directory

When `QdmWatchDirectory` is configured and the directory exists, `ListFiles()` merges results, deduplicating by file name:

```csharp
// After scanning DataDirectory, if QdmWatchDirectory is set:
if (!string.IsNullOrWhiteSpace(_settings.QdmWatchDirectory)
    && Directory.Exists(_settings.QdmWatchDirectory))
{
    var qdmFiles = Directory.GetFiles(_settings.QdmWatchDirectory, "*.csv");
    foreach (var f in qdmFiles)
    {
        if (!results.Any(r => r.FileName == Path.GetFileName(f)))
            results.Add(InspectFile(f));
    }
}
```

No file copying occurs at list time. Files in `QdmWatchDirectory` are listed in place. The **Convert** button writes the converted output into `DataDirectory` (not back into the QDM folder), so QDM's own exports are never modified.

### 6. Expose `QdmWatchDirectory` in the Settings page

In `Settings.razor`, add a text field alongside the existing `DataDirectory` input:

```
┌──────────────────────────────────────────────────────┐
│ Data Directory                                       │
│ [data/____________________________________] [Browse] │
│                                                      │
│ QDM Watch Directory  (optional)                      │
│ [C:\QDM\data\__________________________] [Browse]   │
│  When set, CSV files exported by QuantDataManager    │
│  appear automatically in Data Files.                 │
└──────────────────────────────────────────────────────┘
```

The field is optional. If left blank, behaviour is identical to today.

---

## What Does NOT Change

- `CsvDataProvider.cs` — no changes; reads engine-format CSVs only
- `DataProviderFactory.cs` — no new provider type needed for Option A
- `MarketData.razor` — no new download tab in this scope
- Strategy Builder — validated files surface automatically after conversion, as today
- The backtesting core — zero changes

---

## End-to-End Workflow After This Change

1. Open QDM, select symbols + timeframe + date range, set output folder to `QdmWatchDirectory`
2. QDM exports e.g. `EURUSD_H1.csv`, `GBPUSD_M15.csv` into that folder
3. Open **Data Files** — the files appear immediately with `Format: QuantDataManager`
4. Click **Convert** — engine writes `EURUSD_H1_engine.csv` into `DataDirectory`
5. Click **Validate** — file passes and becomes available in Strategy Builder Step 2
6. No browser tab left open, no manual column remapping, no copy-paste

Alternatively, run `qdm-bulk-download.ps1` first to export many symbols in one shot before opening the engine.

---

## Acceptance Criteria

1. A QDM CSV (header `Date,Time,Open,High,Low,Close,Volume`, date `yyyy.MM.dd`) is detected as `QuantDataManager` format, not `MetaTrader` or `Engine`.
2. Clicking **Convert** on a QDM file produces a valid engine-format CSV with `Timestamp` in ISO 8601 UTC and correct OHLCV columns.
3. The converted file passes the existing `ValidateSchema` check without modifications to the validator.
4. When `QdmWatchDirectory` is configured, files in that directory appear in the **Data Files** table.
5. The **Convert** action writes output to `DataDirectory`, not to `QdmWatchDirectory`.
6. When `QdmWatchDirectory` is empty or not set, behaviour is identical to today.
7. The `QdmWatchDirectory` field is visible and editable in Settings and persists to `appsettings.json`.

---

## Suggested Task Breakdown

1. Update `CsvFormatConverter`: add `QuantDataManager` enum member, update `DetectFormat` to accept lines array and peek first data row, add `ConvertQuantDataManager`, wire into `ConvertLine`.
2. Add unit tests: `DetectFormat_QdmHeader_ReturnsQuantDataManager`, `ConvertQuantDataManager_KnownRow_ProducesCorrectTimestamp`, `DetectFormat_Mt5Header_StillReturnsMetaTrader`.
3. Update `AppSettings` with nullable `QdmWatchDirectory`; ensure JSON round-trip (old settings files without the field deserialise without error using constructor default).
4. Update `DataFileService.ListFiles()` to merge QDM watch directory when configured.
5. Update `Settings.razor` to expose the `QdmWatchDirectory` field.
6. Add integration test: `DataFileService_QdmWatchDirectory_ListsQdmFiles`.
7. Update `Data.razor` empty-state message to mention QDM as a data source option.

---

## Risks and Mitigations

| Risk | Why it matters | Mitigation |
|---|---|---|
| QDM date format changes between versions | `yyyy.MM.dd` is the current observed format | Peek logic is isolated in `DetectFormat`; easy to extend |
| QDM writing a file while engine is listing | Partial reads during active export | `ListFiles` reads metadata only; no data corruption risk |
| Large QDM archive scanned on every page load | May slow the Data Files page | Limit scan to `*.csv`; add warning after 500 files |
| Output naming collision | Two files with same base name from different locations | QDM naming includes timeframe suffix (`EURUSD_H1.csv`); output should preserve full base name |
