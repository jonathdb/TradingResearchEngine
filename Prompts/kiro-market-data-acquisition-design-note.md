# Kiro Design Note — Market Data Acquisition Workflow Adjacent to Data Files

## Purpose

This note proposes a concrete implementation plan for adding a **Market Data Acquisition** workflow to TradingResearchEngine. The goal is to let users generate approved CSV files from external market-data providers such as Dukascopy, while keeping analytics and backtesting isolated from data-heavy download work. This aligns with the current architecture, which already separates managed data files, background progress reporting, and validated file selection in the Strategy Builder.[cite:2][cite:4][cite:5]

The intended model is: **external source download -> normalization -> approved CSV artifact -> Data File validation -> analytics consumption**. This preserves the current rule that Strategy Builder Step 2 should surface only validated files, while restoring a first-class path for obtaining those files from market-data sources rather than only manual file import.[cite:2][cite:4][cite:5]

## Why this belongs beside Data Files

The current specs define Data Files as a managed inventory of CSV artifacts with metadata detection, validation status, and repository-backed persistence.[cite:2][cite:4] They do not define a first-class workflow for downloading market data from external providers, which is why raw market-data input is no longer visible as a distinct UX concept.[cite:2]

Adding Market Data as an adjacent workflow avoids overloading Data Files with two separate jobs: acquisition and inventory. In this design, **Market Data** becomes the factory for generating approved CSVs, while **Data Files** remains the library for previewing, validating, and selecting those files for research and backtesting.[cite:2][cite:4]

## Product goals

The proposed workflow should satisfy five product goals:

- Let users fetch historical candles from a supported external provider, starting with Dukascopy.[cite:4]
- Keep download and normalization work separate from backtests and studies, which already use explicit progress, status, and cancellation patterns.[cite:2][cite:4][cite:5]
- Preserve the existing invariant that analytics only consume validated Data Files.[cite:2][cite:5]
- Establish a provider-agnostic architecture so additional sources can be added later without redesigning the model.[cite:4]
- Produce reproducible, provider-normalized CSV artifacts rather than reintroducing a fragile direct raw-data path.[cite:2][cite:4]

## Recommended user workflow

The recommended end-to-end user flow is:

1. Open **Market Data**.
2. Choose a source, initially **Dukascopy**.
3. Choose symbol, timeframe, and date range.
4. Start an import job that runs in the background with progress and cancellation.
5. Normalize the downloaded data into the engine's approved CSV schema.
6. Save the output into the managed data directory.
7. Create a `DataFileRecord` for the generated CSV.
8. Run the existing validation pipeline.
9. If valid, surface the file automatically in **Data Files** and in Strategy Builder Step 2.[cite:2][cite:4][cite:5]

This keeps the Builder unchanged from a research-discipline perspective: users still pick only validated files, but now the product gives them a guided way to create those files from a trusted source.[cite:2][cite:5]

## UX/UI recommendation

The preferred UI model is to add a new top-level or near-top-level screen called **Market Data**. Data Files already owns preview, validation, and deletion behavior, so pushing acquisition into that page would make it too crowded and blur the distinction between obtaining data and managing approved files.[cite:2][cite:4]

### Suggested navigation

- Dashboard
- Strategy Library
- Research Explorer
- **Market Data**
- Data Files
- Settings

### Suggested Market Data screen

```text
┌──────────────────────────────────────────────────────────────┐
│ Market Data                                 [+ New Import]  │
├──────────────────────────────────────────────────────────────┤
│ Source: [Dukascopy ▼]                                     │
│ Symbol: [EURUSD    ▼]  Timeframe: [H1 ▼]                  │
│ Range:  [2020-01-01] to [2024-12-31]                      │
│ Output: Approved CSV                                       │
│                                   [Start Download]         │
├──────────────────────────────────────────────────────────────┤
│ Recent Imports                                             │
│ ┌────────────────────────────────────────────────────────┐ │
│ │ ✅ EURUSD H1 2020-2024 · Dukascopy · Approved         │ │
│ │    43,800 bars · Saved to datafiles/eurusd_h1.csv     │ │
│ │    [View File] [Open in Data Files] [Re-import]       │ │
│ └────────────────────────────────────────────────────────┘ │
│ ┌────────────────────────────────────────────────────────┐ │
│ │ 🔄 GBPUSD M15 2022-2024 · Dukascopy · 37%             │ │
│ │    Downloading month 9 of 24                          │ │
│ │    [View Progress] [Cancel]                           │ │
│ └────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### Suggested Data Files linkages

The Data Files page should add a shortcut such as **[Import from Market Source]**. The Strategy Builder Step 2 page should add a small helper link: **"Can't find your data? Import market data."** This preserves the validated-file workflow while making the acquisition path discoverable.[cite:2][cite:5]

## Architecture recommendation

The new workflow should follow the same separation principles already used throughout the application: orchestration in Application, provider/network/file IO in Infrastructure, and no unnecessary Core changes.[cite:4] Core does not need to know where data came from; it only needs the resulting approved CSV and the scenario configuration that references it.[cite:4]

### Proposed layer ownership

| Layer | Responsibility |
|---|---|
| Core | No change; consumes normalized approved CSV indirectly through existing engine inputs [cite:4] |
| Application | Import workflow orchestration, import records, provider abstraction, normalization contract, validation handoff [cite:4] |
| Infrastructure | Dukascopy adapter, HTTP/download logic, raw-file staging, CSV writing, import persistence [cite:4] |
| Web | Market Data screen, import forms, progress UI, history list, deep links into Data Files [cite:4][cite:5] |

## Proposed domain and service additions

### New Application records

```csharp
public enum MarketDataImportStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record MarketDataImportRecord(
    string ImportId,
    string Source,
    string Symbol,
    string Timeframe,
    DateTimeOffset RequestedStart,
    DateTimeOffset RequestedEnd,
    MarketDataImportStatus Status,
    string? OutputFilePath = null,
    string? OutputFileId = null,
    int? DownloadedChunkCount = null,
    int? TotalChunkCount = null,
    string? ErrorDetail = null,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset? CompletedAt = null);
```

This record is intentionally separate from `DataFileRecord`. `DataFileRecord` describes a managed file in the library; `MarketDataImportRecord` describes the acquisition job that may or may not successfully produce such a file.[cite:4]

### New Application interfaces

```csharp
public interface IMarketDataProvider
{
    string SourceName { get; }

    Task<IReadOnlyList<MarketSymbolDescriptor>> ListSymbolsAsync(
        CancellationToken ct = default);

    Task DownloadAsync(
        MarketDataImportRequest request,
        IProgressReporter? progress = null,
        CancellationToken ct = default);
}

public interface IMarketDataImportRepository
{
    Task<MarketDataImportRecord?> GetAsync(string importId, CancellationToken ct = default);
    Task<IReadOnlyList<MarketDataImportRecord>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(MarketDataImportRecord record, CancellationToken ct = default);
    Task DeleteAsync(string importId, CancellationToken ct = default);
}

public interface IApprovedCsvWriter
{
    Task<ApprovedCsvWriteResult> WriteAsync(
        NormalizedMarketData data,
        ApprovedCsvMetadata metadata,
        CancellationToken ct = default);
}
```

### New Application workflow service

```csharp
public sealed class MarketDataImportService
{
    public event Action<ImportProgressUpdate>? OnProgress;
    public event Action<ImportCompletionUpdate>? OnCompleted;

    public Task<string> StartImportAsync(
        MarketDataImportRequest request,
        CancellationToken ct = default);

    public void CancelImport(string importId);
    public IReadOnlyList<ActiveImport> GetActiveImports();
}
```

This service should mirror the job-control behavior already specified for long-running studies, but it should remain a distinct concept from `BackgroundStudyService` to avoid mixing research jobs and acquisition jobs under one misleading abstraction.[cite:4][cite:5]

## Canonical approved CSV contract

Before implementation, the approved CSV contract should be made explicit. The current Data File validation rules describe structural checks, but they do not fully define a provider-independent canonical schema suitable for multi-provider imports.[cite:2][cite:4]

### Recommended approved CSV schema

```text
Timestamp,Open,High,Low,Close,Volume
2020-01-01T00:00:00Z,1.1210,1.1225,1.1201,1.1219,1234
2020-01-01T01:00:00Z,1.1219,1.1230,1.1210,1.1222,1188
```

### Contract decisions

- Timestamps must be stored in UTC using ISO 8601 with `Z` suffix.
- Rows must be strictly ascending by timestamp.
- Header names must be fixed and provider-independent.
- `Volume` should be optional at the engine level but present when available; if unavailable, it may be written as `0` or left out only if validation rules explicitly support that choice.
- Bars must represent a declared basis: `Bid`, `Ask`, or `Mid`.
- The chosen basis must be recorded in import metadata even if it is not embedded in the CSV itself.[cite:2][cite:4]

The canonical schema is the single most important prerequisite for multi-provider support. Without it, adding a second source later will create inconsistent analytics even when files look structurally valid.[cite:2][cite:4]

## Critical specification decisions to lock before coding

### Timezone and session policy

A fixed policy is required for how bars are interpreted. The research blueprint already highlights how realism and market-structure assumptions materially affect results, especially for intraday strategies.[cite:1] The import workflow should therefore define whether approved bars are UTC-native only, whether daily bars follow UTC or New York close semantics, and whether provider-specific session boundaries are normalized or preserved.[cite:1][cite:2]

### Candle basis policy

For FX data in particular, Dukascopy and future sources may expose bid, ask, or mid data. If the engine does not define which basis counts as approved for backtesting, results across providers will not be comparable.[cite:1] The recommendation is to make candle basis an explicit import setting with a clear default, and to persist that metadata alongside the resulting file.[cite:1][cite:4]

### Range semantics

The UI should use **explicit date range** as the primary input, with quick presets like 1Y, 3Y, 5Y, and 10Y layered on top for convenience. Date range is less ambiguous than "length" and maps better to validation, reproducibility, sealed test set logic, and file metadata already present in `DataFileRecord`.[cite:4][cite:2]

### Provider-native versus locally aggregated timeframes

This must be answered before implementation. Downloading provider-native H1, H4, or D1 candles is simpler and lighter. Downloading a base timeframe and aggregating locally is more flexible but greatly increases storage, CPU, and QA complexity.[cite:1] The recommended V1 approach is to request provider-native timeframes only, unless a specific research need justifies aggregation.[cite:1]

## Proposed workflow stages

### Phase 1 — Dukascopy-only acquisition MVP

Scope:
- Single provider: Dukascopy
- Symbols from a curated supported list
- Timeframes limited to those the provider exposes clearly
- Date-range input
- Background download with cancellation
- Normalization into approved CSV
- Automatic `DataFileRecord` creation and validation
- Deep link into Data Files after completion

This phase restores the missing market-data acquisition capability while keeping scope controlled and preserving the validated-file architecture.[cite:2][cite:4][cite:5]

### Phase 2 — Import history and quality diagnostics

Scope:
- Import history page enhancements
- Duplicate detection by `(source, symbol, timeframe, range, basis)`
- Richer data-quality checks: duplicate timestamps, suspicious gaps, flatline sequences, extreme outliers
- Re-import and overwrite options
- Comparison between old and newly imported datasets

This phase improves trust and operational safety, especially when provider behavior changes over time.[cite:2][cite:4]

### Phase 3 — Multi-provider expansion

Scope:
- Additional providers behind `IMarketDataProvider`
- Provider capability metadata: symbols, timeframes, volume availability, candle basis options
- UI capability filtering based on selected provider
- Cross-provider audit fields persisted on import records

This phase should only start once the canonical CSV contract and import metadata model have proven stable with Dukascopy.[cite:4]

## Error handling and long-running job behavior

The current specs already define strong patterns for failure handling, progress reporting, cancellation, and status surfaces for long-running work.[cite:2][cite:4][cite:5] The market-data workflow should adopt the same behavior class:

- Configuration errors: invalid symbol, invalid range, unsupported timeframe; shown inline, no completed file generated.[cite:2][cite:4]
- Runtime errors: network failure, provider response error, parse failure, normalization failure; persisted on `MarketDataImportRecord` with `Status = Failed` and error detail.[cite:2][cite:4]
- Cancellation: import is stopped cleanly, temporary artifacts removed or marked stale, job stored as `Cancelled`.[cite:4][cite:5]
- Progress: determinate when chunked by month/day, indeterminate when provider does not expose predictable chunk counts.[cite:2][cite:4]

### Progress examples

- `Downloading EURUSD H1 from Dukascopy — month 7 of 60 (11%)`
- `Normalizing candles — 540,000 rows processed`
- `Validating approved CSV — checking continuity and OHLC integrity`

## Persistence and storage model

The app already persists Data Files as JSON-backed metadata records in Infrastructure.[cite:4][cite:5] The acquisition workflow should add a parallel import-history repository while continuing to treat the approved CSV as the primary artifact used by research workflows.[cite:4]

### Recommended storage rules

- Persist the normalized approved CSV as the canonical artifact.
- Persist `MarketDataImportRecord` as import history.
- Persist `DataFileRecord` after successful normalization and validation.
- Keep raw provider downloads only if needed for debugging or resumable import; do not make raw downloads the primary research artifact.[cite:4]

This avoids cluttering the research model with provider-specific file formats and ensures the Data Files inventory remains the single source of truth for backtests.[cite:2][cite:4]

## Testing plan

The current project already emphasizes unit and integration testing for data-file persistence, validation, migration, long-running execution, and workflow-level guarantees.[cite:4][cite:5] The market-data workflow should extend that discipline with dedicated coverage.

### Unit tests

- `ApprovedCsvWriter_WritesCanonicalHeaders`
- `MarketDataNormalizer_ConvertsProviderRows_ToUtcAscendingBars`
- `MarketDataImportRequest_InvalidRange_FailsValidation`
- `MarketDataImportRecord_RoundTrip_Json`
- `DukascopyProvider_UnsupportedTimeframe_ThrowsExpected`
- `DataFileValidation_ImportedCsv_PassesForKnownGoodFixture`

### Integration tests

- `MarketDataImport_DukascopyKnownFixture_CreatesValidDataFile`
- `MarketDataImport_Cancelled_LeavesNoApprovedFile`
- `MarketDataImport_FailedNormalization_PersistsFailureRecord`
- `MarketDataImport_Completed_AppearsInDataFilesList`
- `StrategyBuilder_ImportedApprovedFile_AppearsInValidatedPicker`

### End-to-end workflow tests

- `FullFlow_ImportMarketData_Validate_CreateStrategy_RunBacktest`
- `FullFlow_ReimportSameRange_ShowsDuplicateOrReplacePrompt`

These tests matter because the feature crosses acquisition, normalization, persistence, validation, and UX discoverability in one flow.[cite:4][cite:5]

## Main risks and mitigations

| Risk | Why it matters | Mitigation |
|---|---|---|
| Canonical schema not defined early | Multi-provider support becomes inconsistent | Freeze approved CSV contract before coding [cite:2][cite:4] |
| Timezone/session ambiguity | Intraday and daily analytics may diverge silently | Fix UTC/session policy explicitly in requirements [cite:1][cite:2] |
| Provider fragility | External URLs, formats, and limits may change | Isolate in provider adapter and persist failures clearly [cite:4] |
| Scope creep | Multi-provider ambitions can delay restoration of basic capability | Ship Dukascopy-first behind provider abstraction [cite:4] |
| Hidden data-quality issues | Structurally valid files may still be analytically poor | Add post-import quality diagnostics in Phase 2 [cite:2] |
| Overloaded Data Files UX | Acquisition and library management become muddled | Keep Market Data as adjacent workflow, not a replacement [cite:2][cite:4] |

## Requirements amendments recommended for Kiro

The following additions would make the workflow concrete in the product spec:

### New requirement — Market Data Acquisition

**User Story:** As a user, a user wants to fetch historical market data from a supported provider and turn it into an approved CSV file so that backtests and studies can use validated local data without doing external downloads inline.

**Acceptance Criteria:**
1. A Market Data screen lets the user choose a provider, symbol, timeframe, and date range.[cite:4]
2. Starting an import launches a background job with progress and cancellation support.[cite:2][cite:4]
3. A completed import produces a normalized approved CSV in the managed data directory.[cite:4]
4. The app creates a `DataFileRecord` for the output and runs the existing validation pipeline.[cite:2][cite:4][cite:5]
5. Successfully validated imported files appear automatically in Data Files and in Strategy Builder Step 2.[cite:2][cite:5]
6. Failed and cancelled imports are visible in import history with error details or cancellation status.[cite:4]

### New requirement — Approved CSV Contract

**User Story:** As a developer, a developer wants a canonical approved CSV schema so that all providers feed the same research workflow consistently.

**Acceptance Criteria:**
1. The approved CSV schema defines required headers, timestamp format, sort order, timezone, and candle basis metadata.[cite:2][cite:4]
2. All provider adapters normalize to this schema before file registration.[cite:4]
3. Validation rules apply uniformly to manually added files and imported files.[cite:2][cite:5]

## Suggested task breakdown

1. Add Application types: `MarketDataImportRecord`, `MarketDataImportStatus`, request/result records, and repository interface.
2. Add Infrastructure persistence: `JsonMarketDataImportRepository`.
3. Add `IMarketDataProvider` and implement `DukascopyMarketDataProvider`.
4. Add normalization and `IApprovedCsvWriter`.
5. Add `MarketDataImportService` with progress, events, and cancellation.
6. Add Web Market Data screen and recent-import list.
7. Add Data Files shortcut: **Import from Market Source**.
8. Add Builder Step 2 helper link for missing data.
9. Add tests for canonicalization, import lifecycle, and Data Files integration.[cite:4][cite:5]

## Recommendation

This feature should proceed, but only with a strict boundary: **external acquisition produces approved CSVs; analytics never consume provider output directly**. That design fits the current architecture, solves the missing raw-market-data workflow in a reproducible way, and keeps resource-intensive downloading separate from backtests and studies.[cite:2][cite:4][cite:5]

The four questions that should be resolved before implementation starts are: the canonical CSV contract, timezone/session policy, candle basis policy, and whether V1 uses provider-native timeframes only. Those choices will determine whether the Dukascopy-first workflow remains clean and extensible or becomes a one-off path that is hard to generalize later.[cite:1][cite:2][cite:4]
