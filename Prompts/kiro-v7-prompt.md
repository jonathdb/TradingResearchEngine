# Kiro Development Prompt — TradingResearchEngine V7

## Context

You are continuing development of **TradingResearchEngine** from where V6 left off. Read all steering documents in `.kiro/steering/` before writing any code. The dependency rule (`Core ← Application ← Infrastructure ← { Cli, Api, Web }`) is non-negotiable.

This V7 sprint closes the remaining open items from the V6 review. Every item below was identified by direct inspection of the current source code. No speculative changes — fix only what is listed.

---

## What V6 Completed (Do Not Re-Implement)

The following are **fully done** in the current codebase. Do not touch them unless a V7 fix requires it:

- `ResearchChecklistService`: 9-item checklist, PropFirm item 8 wired to `IPropFirmEvaluationRepository`, CPCV item 9 wired, HIGH ≥ 8 / MEDIUM ≥ 5 / LOW < 5
- `CpcvStudyHandler`: fully implemented with combinatorics, fold generation, IS/OOS PBO calculation
- `WalkForwardWorkflow`: `Parallel.ForEachAsync` + `SemaphoreSlim` + `ConcurrentBag` + post-sort by `WindowIndex`
- `ParameterSweepWorkflow`: same parallel pattern
- `SqliteIndexRepository.cs`: exists in Infrastructure/Persistence
- `JsonPropFirmEvaluationRepository.cs`: exists in Infrastructure/Persistence
- `StrategyIdentity.RetirementNote`: nullable `string?` field exists on the record
- `WalkForwardCompositeChart`: renders inline on `WalkForward.razor`
- `StudyType.Cpcv` alias: exists as alias for `CombinatorialPurgedCV`

---

## V7 Scope — Seven Fixes

Work through each fix sequentially. Run `dotnet test` after each fix before proceeding.

---

### Fix 1 — Add `GetVersionAsync(string versionId)` to `IStrategyRepository` (Critical)

**Problem:** `IStrategyRepository` has no direct version lookup by ID. Three places do an O(n×m) full scan — listing all strategies, then all versions of each, then matching by `StrategyVersionId`. With 50–100 strategy versions this becomes expensive.

**Step 1a — Interface (`IStrategyRepository.cs` in Application/Strategy/):**

Add one method:
```csharp
/// <summary>Gets a strategy version by its ID directly, or null if not found.</summary>
Task<StrategyVersion?> GetVersionAsync(string strategyVersionId, CancellationToken ct = default);
```

**Step 1b — Implementation (`JsonStrategyRepository.cs` in Infrastructure/Persistence/):**

The version files are stored at `strategies/{strategyId}/versions/{versionId}.json`. But to do a direct lookup by `versionId` alone, the repository needs to search across all strategy version directories.

Implement as a two-level glob — it is still O(n) in the number of strategy IDs but avoids deserialising every version:

```csharp
public async Task<StrategyVersion?> GetVersionAsync(string strategyVersionId, CancellationToken ct = default)
{
    if (!Directory.Exists(_baseDir)) return null;
    foreach (var strategyDir in Directory.GetDirectories(_baseDir))
    {
        var versionsDir = Path.Combine(strategyDir, "versions");
        var versionFile = Path.Combine(versionsDir, $"{strategyVersionId}.json");
        if (File.Exists(versionFile))
        {
            var json = await File.ReadAllTextAsync(versionFile, ct);
            return JsonSerializer.Deserialize<StrategyVersion>(json, JsonOpts);
        }
    }
    return null;
}
```

**Step 1c — Replace all O(n×m) loops:**

Three callers must be updated:

1. **`ResearchChecklistService.cs`** — replace `GetVersionAsync` private method (the one doing `ListAsync → foreach → GetVersionsAsync → FirstOrDefault`) with a single call to `_strategyRepo.GetVersionAsync(strategyVersionId, ct)`.

2. **`StudyDetail.razor`** — in `OnInitializedAsync`, replace:
   ```csharp
   var strategies = await StrategyRepo.ListAsync();
   foreach (var s in strategies)
   {
       var versions = await StrategyRepo.GetVersionsAsync(s.StrategyId);
       if (versions.Any(v => v.StrategyVersionId == _study.StrategyVersionId))
       { _strategyName = s.StrategyName; break; }
   }
   ```
   With:
   ```csharp
   var version = await StrategyRepo.GetVersionAsync(_study.StrategyVersionId);
   if (version is not null)
   {
       var strategy = await StrategyRepo.GetAsync(version.StrategyId);
       _strategyName = strategy?.StrategyName;
   }
   ```

3. **`ResearchExplorer.razor`** — apply the same pattern wherever it resolves `StrategyVersionId` to a strategy name.

**Tests** in `UnitTests/V7/GetVersionAsyncTests.cs`:
- Returns correct version when file exists
- Returns null when `strategyVersionId` does not match any file
- Handles empty base directory gracefully

**Checkpoint:** `dotnet test` passes. No O(n×m) nested loops remain in `ResearchChecklistService`, `StudyDetail`, or `ResearchExplorer`.

---

### Fix 2 — Add `StudyType` Enum Values for Missing Workflow Types (Critical)

**Problem:** `BenchmarkComparisonWorkflow.cs` and `VarianceTestingWorkflow.cs` exist and are fully implemented, but `StudyType` enum has no values for them. `BackgroundStudyService` cannot dispatch them. The research checklist has no item for Benchmark (`BenchmarkComparison`). The `StrategyDetail` research tab cannot link to them by type.

**Step 2a — Add to `StudyType` enum in `StudyRecord.cs`:**

```csharp
/// <summary>V7: Benchmark comparison against a buy-and-hold baseline.</summary>
BenchmarkComparison,
/// <summary>V7: Variance testing — stability across sub-period slices.</summary>
Variance,
/// <summary>V7: Randomised OOS sampling study.</summary>
RandomisedOos,
```

**Step 2b — Wire into `BackgroundStudyService.cs`:**

In the `switch` that dispatches by `StudyType`, add cases for `BenchmarkComparison`, `Variance`, and `RandomisedOos` using their respective workflow classes (`BenchmarkComparisonWorkflow`, `VarianceTestingWorkflow`, `RandomizedOosWorkflow`). Follow the exact same pattern as existing cases (inject the workflow, call `RunAsync`, serialize result to JSON, call `SaveAsync`).

**Step 2c — Wire `BenchmarkExcessSharpe` chip in `StrategyDetail.razor`:**

Currently hardcoded:
```csharp
// Note: BenchmarkComparisonResult is not persisted; approximate from available data
_benchmarkExcessSharpe = _latestRun.SharpeRatio;
```

Replace with:
```csharp
var benchmarkStudies = await StudyRepo.ListByVersionAsync(_selectedVersionId);
var latestBenchmark = benchmarkStudies
    .Where(s => s.Type == StudyType.BenchmarkComparison && s.Status == StudyStatus.Completed && s.ResultJson is not null)
    .OrderByDescending(s => s.CreatedAt)
    .FirstOrDefault();

if (latestBenchmark?.ResultJson is not null)
{
    var benchmarkResult = JsonSerializer.Deserialize<BenchmarkComparisonResult>(latestBenchmark.ResultJson, _jsonOpts);
    _benchmarkExcessSharpe = benchmarkResult?.ExcessSharpe;
}
else
{
    _benchmarkExcessSharpe = null; // show tooltip instead
}
```

Update the chip tooltip: when `_benchmarkExcessSharpe` is null, show `"Run a Benchmark Comparison study to see excess Sharpe vs Buy & Hold"`.

This requires Fix 3 (ResultJson on StudyRecord) to be in place first — implement Fix 3 before wiring this chip.

**Tests** in `UnitTests/V7/StudyTypeEnumTests.cs`:
- `StudyType.BenchmarkComparison` has a distinct integer value
- `StudyType.Variance` has a distinct integer value
- No two enum values share the same integer (check all values are unique)

---

### Fix 3 — Add `ResultJson` to `StudyRecord` and Persist Study Results (Critical)

**Problem:** Study results are computed but never persisted. Once you navigate away from a research page, the results are gone. `StudyDetail.razor` shows only metadata. The `BenchmarkExcessSharpe` chip cannot read real data.

**Step 3a — Add `ResultJson` to `StudyRecord.cs`:**

```csharp
public sealed record StudyRecord(
    string StudyId,
    string StrategyVersionId,
    StudyType Type,
    StudyStatus Status,
    DateTimeOffset CreatedAt,
    string? SourceRunId = null,
    string? ErrorSummary = null,
    bool IsPartial = false,
    int CompletedCount = 0,
    int TotalCount = 0,
    /// <summary>V7: Serialized JSON of the workflow result. Null until the study completes.</summary>
    string? ResultJson = null) : IHasId
```

**Step 3b — Add `SaveResultAsync` to `IStudyRepository.cs`:**

```csharp
/// <summary>Saves the result JSON for a completed study.</summary>
Task SaveResultAsync(string studyId, string resultJson, CancellationToken ct = default);
```

**Step 3c — Implement in `JsonStudyRepository.cs`:**

```csharp
public async Task SaveResultAsync(string studyId, string resultJson, CancellationToken ct = default)
{
    var study = await GetAsync(studyId, ct);
    if (study is null) return;
    await SaveAsync(study with { ResultJson = resultJson }, ct);
}
```

**Step 3d — Serialize and save results in `BackgroundStudyService.cs`:**

After each workflow's `RunAsync` returns successfully, before marking the study `Completed`, serialize the result to JSON and call `SaveResultAsync`:

```csharp
var resultJson = JsonSerializer.Serialize(result, _jsonOpts);
await _studyRepo.SaveResultAsync(studyRecord.StudyId, resultJson, ct);
await _studyRepo.SaveAsync(studyRecord with { Status = StudyStatus.Completed }, ct);
```

Do this for **all** workflow types: MonteCarlo, WalkForward, AnchoredWalkForward, ParameterSweep, Sensitivity, Realism, RegimeSegmentation, BenchmarkComparison, Variance, RandomisedOos, Cpcv.

Use a shared `JsonSerializerOptions` instance (with `WriteIndented = false` to keep file sizes down).

**Step 3e — Render results in `StudyDetail.razor`:**

After loading `_study`, if `_study.ResultJson` is not null and `_study.Status == StudyStatus.Completed`, deserialize and render the appropriate chart component based on `_study.Type`:

```razor
@if (_study.ResultJson is not null && _study.Status == StudyStatus.Completed)
{
    <MudPaper Class="pa-4 mt-4" Elevation="1">
        <MudText Typo="Typo.subtitle1" Class="mb-3">Results</MudText>
        @switch (_study.Type)
        {
            case StudyType.MonteCarlo:
                <MonteCarloFanChart Result="_monteCarloResult" />
                break;
            case StudyType.WalkForward:
            case StudyType.AnchoredWalkForward:
                <WalkForwardCompositeChart Summary="_wfSummary" />
                break;
            case StudyType.ParameterSweep:
                <ParameterSweepHeatmap SweepResult="_sweepResult" />
                break;
            case StudyType.BenchmarkComparison:
                @* Render BenchmarkComparisonResult metrics cards *@
                break;
            case StudyType.Cpcv:
                @* Render CpcvResult: MedianOosSharpe, ProbabilityOfOverfitting, PerformanceDegradation *@
                break;
        }
    </MudPaper>
}
```

In the `@code` block, deserialize `_study.ResultJson` to the appropriate result type in `OnInitializedAsync` based on `_study.Type`. Store each as a nullable field (e.g., `MonteCarloResult? _monteCarloResult`).

**Tests** in `UnitTests/V7/StudyResultPersistenceTests.cs`:
- `SaveResultAsync` updates the `ResultJson` field and persists to disk
- Round-trip: save then load → `ResultJson` equals original serialized string
- `ResultJson` is null for a study in `Running` state

---

### Fix 4 — Research Page Query Parameter Wiring (High)

**Problem:** Five research pages (`WalkForward`, `Sweep`, `Perturbation`, `Benchmark`, `Variance`) ignore URL parameters passed from `StrategyDetail`. Users cannot launch research for a saved strategy — they must re-enter the entire config from scratch every time.

**Step 4a — Create `StrategyVersionPicker.razor`** in `TradingResearchEngine.Web/Components/Builder/`:

```razor
@inject IStrategyRepository StrategyRepo

<MudStack Spacing="2">
    <MudSelect T="StrategyIdentity?" Label="Strategy" @bind-Value="_selectedStrategy"
               Variant="Variant.Outlined" ToStringFunc="@(s => s?.StrategyName ?? "Select a strategy...")">
        @foreach (var s in _strategies)
        {
            <MudSelectItem T="StrategyIdentity?" Value="@s">@s.StrategyName</MudSelectItem>
        }
    </MudSelect>

    @if (_versions.Count > 0)
    {
        <MudSelect T="StrategyVersion?" Label="Version" @bind-Value="_selectedVersion"
                   Variant="Variant.Outlined" ToStringFunc="@(v => v is null ? "Select version..." : $"v{v.VersionNumber} — {v.CreatedAt:yyyy-MM-dd}")">
            @foreach (var v in _versions)
            {
                <MudSelectItem T="StrategyVersion?" Value="@v">v@(v.VersionNumber) — @v.CreatedAt.ToString("yyyy-MM-dd")</MudSelectItem>
            }
        </MudSelect>
    }
</MudStack>

@code {
    [Parameter] public EventCallback<(StrategyIdentity Strategy, StrategyVersion Version)> OnSelectionChanged { get; set; }
    [Parameter] public string? PreselectedStrategyId { get; set; }
    [Parameter] public string? PreselectedVersionId { get; set; }

    private List<StrategyIdentity> _strategies = new();
    private List<StrategyVersion> _versions = new();
    private StrategyIdentity? _selectedStrategy;
    private StrategyVersion? _selectedVersion;

    protected override async Task OnInitializedAsync()
    {
        _strategies = (await StrategyRepo.ListAsync()).Where(s => s.Stage != DevelopmentStage.Retired).ToList();

        if (PreselectedStrategyId is not null)
        {
            _selectedStrategy = _strategies.FirstOrDefault(s => s.StrategyId == PreselectedStrategyId);
            if (_selectedStrategy is not null)
                await LoadVersions(_selectedStrategy.StrategyId);
        }

        if (PreselectedVersionId is not null && _versions.Count > 0)
        {
            _selectedVersion = _versions.FirstOrDefault(v => v.StrategyVersionId == PreselectedVersionId);
            if (_selectedVersion is not null)
                await NotifySelection();
        }
    }

    private async Task OnStrategyChanged(StrategyIdentity? strategy)
    {
        _selectedStrategy = strategy;
        _selectedVersion = null;
        _versions = new();
        if (strategy is not null)
            await LoadVersions(strategy.StrategyId);
    }

    private async Task LoadVersions(string strategyId)
    {
        _versions = (await StrategyRepo.GetVersionsAsync(strategyId)).ToList();
        if (_versions.Count == 1)
        {
            _selectedVersion = _versions[0];
            await NotifySelection();
        }
    }

    private async Task NotifySelection()
    {
        if (_selectedStrategy is not null && _selectedVersion is not null)
            await OnSelectionChanged.InvokeAsync((_selectedStrategy, _selectedVersion));
    }
}
```

**Step 4b — Wire query parameters in all five pages:**

For each page, add query parameter support and pre-populate `ScenarioConfigEditor` from the selected version's `BaseScenarioConfig`. The pattern is identical across all five pages.

**`WalkForward.razor`:**
```razor
@page "/research/walkforward"
```
Add to `@code`:
```csharp
[SupplyParameterFromQuery] public string? StrategyId { get; set; }
[SupplyParameterFromQuery] public string? VersionId { get; set; }
```

Add `<StrategyVersionPicker PreselectedStrategyId="@StrategyId" PreselectedVersionId="@VersionId" OnSelectionChanged="OnVersionSelected" />` above `<ScenarioConfigEditor>`.

In `OnVersionSelected`, call `_configEditor!.PopulateFromConfig(selection.Version.BaseScenarioConfig)` (add `PopulateFromConfig(ScenarioConfig config)` public method to `ScenarioConfigEditor` — see Step 4c).

Apply the same pattern to: **`Sweep.razor`**, **`Perturbation.razor`**, **`Benchmark.razor`**, **`Variance.razor`**.

For Perturbation, Benchmark, and Variance — they also accept `?result={runId}` to pre-populate from a saved `BacktestResult`. Add:
```csharp
[SupplyParameterFromQuery] public string? ResultId { get; set; }
```
And inject `IBacktestResultRepository`. In `OnInitializedAsync`, if `ResultId` is provided, load the result and call `_configEditor!.PopulateFromConfig(result.ScenarioConfig)`.

**Step 4c — Add `PopulateFromConfig` to `ScenarioConfigEditor.razor`:**

Add a public method that accepts a `ScenarioConfig` and populates all bound fields (strategy type, symbol, timeframe, commission, slippage, initial cash, date range, parameters). This is the reverse of `BuildConfig()`.

```csharp
public void PopulateFromConfig(ScenarioConfig config)
{
    // Populate all @bind-Value fields from config
    // Call StateHasChanged() at the end
}
```

**Step 4d — Add Benchmark and Variance launch buttons to `StrategyDetail.razor`:**

In the Research tab's launch button row, add two new buttons alongside the existing ones:
```razor
<MudButton Variant="Variant.Outlined" Color="Color.Secondary"
           Href="@($"/research/benchmark?strategyId={_strategy.StrategyId}&versionId={_selectedVersionId}")">
    Benchmark
</MudButton>
<MudButton Variant="Variant.Outlined" Color="Color.Secondary"
           Href="@($"/research/variance?strategyId={_strategy.StrategyId}&versionId={_selectedVersionId}")">
    Variance
</MudButton>
```

Also update `GetStudyLaunchUrl()` (if it exists) to map `StudyType.BenchmarkComparison` and `StudyType.Variance` to their respective routes.

**Tests** in `UnitTests/V7/StrategyVersionPickerTests.cs`:
- No xUnit tests for Blazor components — verify manually via UI
- Unit test `ScenarioConfigEditor.PopulateFromConfig` round-trip: `BuildConfig()` after `PopulateFromConfig(config)` returns an equivalent config

---

### Fix 5 — Edit Execution Window: Full Intraday Timeframe List (Medium)

**Problem:** The Edit Execution Window dialog in `StrategyDetail.razor` (and potentially `Step2DataExecutionWindow.razor`) hardcodes only `Daily, H4, H1, M15`. Your primary timeframes are `1m, 5m, 15m, 30m, 1H, 2H, 4H`.

**Step 5a — Verify `TimeframeOptions.cs` exists** in the Web layer. If it does not, create `TradingResearchEngine.Web/Options/TimeframeOptions.cs`:

```csharp
namespace TradingResearchEngine.Web.Options;

/// <summary>Available data timeframes for the strategy builder and execution window editor.</summary>
public static class TimeframeOptions
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "1m", "5m", "15m", "30m", "1H", "2H", "4H", "Daily"
    };
}
```

**Step 5b — Replace hardcoded `MudSelectItem` lists** in the Edit Execution Window dialog inside `StrategyDetail.razor` and in `Step2DataExecutionWindow.razor` with:

```razor
@foreach (var tf in TradingResearchEngine.Web.Options.TimeframeOptions.All)
{
    <MudSelectItem T="string" Value="@tf">@tf</MudSelectItem>
}
```

**Step 5c — Auto-populate `BarsPerYear`** when timeframe changes. In `Step2DataExecutionWindow.razor`, on timeframe selection change, set `BarsPerYear` from `BarsPerYearDefaults`:

```csharp
private void OnTimeframeChanged(string tf)
{
    _selectedTimeframe = tf;
    _barsPerYear = tf switch
    {
        "1m"    => BarsPerYearDefaults.M1,
        "5m"    => BarsPerYearDefaults.M5,
        "15m"   => BarsPerYearDefaults.M15,
        "30m"   => BarsPerYearDefaults.M30,
        "1H"    => BarsPerYearDefaults.H1,
        "2H"    => BarsPerYearDefaults.H2,
        "4H"    => BarsPerYearDefaults.H4,
        "Daily" => BarsPerYearDefaults.D1,
        _       => BarsPerYearDefaults.D1
    };
}
```

**Tests** in `UnitTests/V7/TimeframeOptionsTests.cs`:
- `TimeframeOptions.All` contains exactly 8 entries
- All 8 values map to a non-zero `BarsPerYearDefaults` constant
- `"1m"` maps to `BarsPerYearDefaults.M1` (362880)

---

### Fix 6 — Strategy Library: "Show Retired" Toggle (Medium)

**Problem:** `StrategyIdentity.RetirementNote` exists on the model, `DevelopmentStage.Retired` exists in the enum, `StrategyDetail.razor` can set stage to Retired — but `StrategyLibrary.razor` (or `StrategyOverview.razor`) does not hide retired strategies by default or offer a toggle to show them.

Find the strategy list/library page (likely `StrategyLibrary.razor` or `StrategyOverview.razor`).

**Add to the page:**

1. A `MudSwitch` or `MudToggleIconButton` labelled **"Show Retired"** in the page toolbar, bound to `bool _showRetired = false`.

2. Filter the displayed strategy list:
   ```csharp
   var displayed = _strategies.Where(s => _showRetired || s.Stage != DevelopmentStage.Retired).ToList();
   ```

3. For cards/rows where `s.Stage == DevelopmentStage.Retired`, apply:
   - `style="opacity: 0.5"`
   - A `<MudChip Color="Color.Dark" Size="Size.Small">RETIRED</MudChip>` badge
   - Grey border instead of the standard card border

4. Add a counter in the toolbar: `@if (!_showRetired && _retiredCount > 0) { <MudText Typo="Typo.caption">@_retiredCount retired hidden</MudText> }`

5. On the retirement confirmation dialog (already triggered from `StrategyDetail`), ensure the dialog:
   - Shows a `MudTextField` for `RetirementNote` (optional)
   - Calls `StrategyRepo.SaveAsync(strategy with { Stage = DevelopmentStage.Retired, RetirementNote = _retirementNote })`

No new tests required — this is pure UI.

---

### Fix 7 — Research Explorer: Row Click Navigation (Low)

**Problem:** `ResearchExplorer.razor` shows the study history table but rows have no click action. Users cannot navigate from the explorer to `StudyDetail`.

In `ResearchExplorer.razor`, on the `MudTable`:

1. Add `OnRowClick="@(row => Nav.NavigateTo($"/research/study/{row.Item.StudyId}"))"` to the `MudTable` element.
2. Add `Hover="true"` and `style="cursor:pointer"` to the table to make rows visually clickable.
3. Add an explicit action column with a `MudIconButton` (open icon) pointing to `/research/study/{studyId}` — matches the pattern used in other tables in the codebase.

No new tests required.

---

## Architectural Rules — Mandatory

1. **Dependency rule**: `Core ← Application ← Infrastructure ← { Cli, Api, Web }`. No upward references.
2. **Records and immutability**: `StudyRecord with { ResultJson = resultJson }` — always use `with` expressions, never mutate records.
3. **Async discipline**: no `.Result` or `.Wait()`. All async methods accept `CancellationToken ct = default`.
4. **JSON options**: use a shared static `JsonSerializerOptions` instance per class. Never construct a new one per call.
5. **No domain logic in Infrastructure**: `JsonStudyRepository.SaveResultAsync` only reads/writes — no business logic.
6. **XML doc comments**: all new `public` types and members carry `/// <summary>` comments.
7. **Test project boundaries**: `UnitTests` references Core and Application only.

---

## Steering Document Locations

All steering documents are in `.kiro/steering/`:

| File | Contents |
|---|---|
| `product.md` | Version scopes |
| `tech.md` | .NET version, NuGet packages, async/logging standards |
| `domain-boundaries.md` | Dependency rule, ownership |
| `testing-standards.md` | xUnit/FsCheck/Moq rules, naming conventions |
| `strategy-registry.md` | `[StrategyName]` attribute, `StrategyRegistry` |

---

## Definition of Done

V7 is complete when:

- [ ] `IStrategyRepository.GetVersionAsync(string versionId)` exists in interface and implementation
- [ ] No O(n×m) nested loops remain in `ResearchChecklistService`, `StudyDetail`, or `ResearchExplorer`
- [ ] `StudyType.BenchmarkComparison` and `StudyType.Variance` exist in the enum and are dispatched by `BackgroundStudyService`
- [ ] `StudyRecord.ResultJson` property exists (nullable `string?`)
- [ ] `IStudyRepository.SaveResultAsync` exists and is called after every workflow completes
- [ ] `StudyDetail.razor` renders result components when `ResultJson` is not null
- [ ] `BenchmarkExcessSharpe` chip reads from `BenchmarkComparisonResult.ExcessSharpe` (not from `_latestRun.SharpeRatio`)
- [ ] `StrategyVersionPicker.razor` component exists and pre-populates on `PreselectedStrategyId` / `PreselectedVersionId`
- [ ] `ScenarioConfigEditor.PopulateFromConfig(ScenarioConfig)` method exists
- [ ] `WalkForward`, `Sweep`, `Perturbation`, `Benchmark`, `Variance` pages read `[SupplyParameterFromQuery]` params and pre-populate their editors
- [ ] Benchmark and Variance launch buttons present in `StrategyDetail` Research tab
- [ ] Edit Execution Window timeframe dropdown includes all 8 values (`1m` to `Daily`) driven from `TimeframeOptions.All`
- [ ] `BarsPerYear` auto-populates on timeframe selection change
- [ ] Strategy Library hides retired strategies by default with "Show Retired" toggle
- [ ] Research Explorer rows are clickable and navigate to `/research/study/{studyId}`
- [ ] All new V7 unit tests pass
- [ ] All existing V1–V6 tests still pass
- [ ] `product.md` V7 scope section added at the bottom

---

## Implementation Order

Work in this exact order — each fix unblocks the next:

1. **Fix 1** (`GetVersionAsync`) — used by Fix 3 and Fix 4
2. **Fix 2** (`StudyType` enum + `BackgroundStudyService` dispatch) — required before Fix 3 can persist Benchmark results
3. **Fix 3** (`ResultJson` persistence + `StudyDetail` rendering) — required before Fix 2's chip wiring works
4. **Fix 4** (`StrategyVersionPicker` + query params + launch buttons) — standalone after Fix 1
5. **Fix 5** (timeframe list) — standalone, no dependencies
6. **Fix 6** (Retired toggle) — standalone, no dependencies
7. **Fix 7** (Explorer row click) — standalone, no dependencies
