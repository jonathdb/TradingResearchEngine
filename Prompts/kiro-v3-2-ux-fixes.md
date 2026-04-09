# Kiro Implementation Prompt — V3.2 UX Fix Pass

## Context

This is a targeted fix pass on three files in `TradingResearchEngine.Web`. All issues were identified
by code review of the current `main` branch. Do not change domain logic, repositories, application
services, or any files outside the three pages and their direct dependencies. Work through each task
group in order. Each task group targets one file.

---

## Task Group 1 — `Dashboard.razor`

**File:** `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`

### Fix 1 — Remove hardcoded hex colours from strategy strip

**Current code (Zone 1 strategy strip):**
```razor
Style="@($"min-width:180px; cursor:pointer; border-left:3px solid {(lastRun is not null ? "#4F98A3" : "#555")};")"
```

**Replace with:**
```razor
Style="min-width:180px; cursor:pointer;"
Class="@($"strategy-strip-card {(lastRun is not null ? "strip-active" : "strip-untested")}")"
```

Add to `wwwroot/app.css` (create if absent):
```css
.strategy-strip-card { border-left: 3px solid var(--mud-palette-text-disabled); }
.strip-active        { border-left-color: var(--mud-palette-primary) !important; }
.strip-untested      { border-left-color: var(--mud-palette-text-disabled) !important; }
```

---

### Fix 2 — Add `_strategyIdByType` lookup dictionary

In `@code`, add a new field:
```csharp
private Dictionary<string, string> _strategyIdByType = new();
```

In `OnInitializedAsync`, after `_strategies` is loaded, populate it:
```csharp
_strategyIdByType = _strategies
    .GroupBy(s => s.StrategyType)
    .ToDictionary(g => g.Key, g => g.First().StrategyId);
```

This lookup is used by Fixes 3 and 4 below.

---

### Fix 3 — Make Strategy column in Recent Runs table a hub link

**Current code (Recent Runs table `RowTemplate`):**
```razor
<MudTd>@context.ScenarioConfig.StrategyType</MudTd>
```

**Replace with:**
```razor
<MudTd>
    @if (_strategyIdByType.TryGetValue(context.ScenarioConfig.StrategyType, out var sid))
    {
        <MudLink Href="@($"/strategies/{sid}")">@context.ScenarioConfig.StrategyType</MudLink>
    }
    else
    {
        <MudText Typo="Typo.body2">@context.ScenarioConfig.StrategyType</MudText>
    }
</MudTd>
```

---

### Fix 4 — Add a date column to the Recent Runs table

**Current header:**
```razor
<MudTh>Strategy</MudTh>
<MudTh>Sharpe</MudTh>
<MudTh>Max DD</MudTh>
<MudTh>Trades</MudTh>
<MudTh></MudTh>
```

**Replace with:**
```razor
<MudTh>Strategy</MudTh>
<MudTh>Date</MudTh>
<MudTh>Sharpe</MudTh>
<MudTh>Max DD</MudTh>
<MudTh>Trades</MudTh>
<MudTh></MudTh>
```

In `RowTemplate`, add the date cell immediately after the Strategy cell:
```razor
<MudTd>
    <MudText Typo="Typo.body2" Class="text-muted">
        @(TryParseRunDate(context.RunId)?.ToString("yyyy-MM-dd") ?? "—")
    </MudText>
</MudTd>
```

Add the helper method to `@code`:
```csharp
/// <summary>
/// Attempts to extract a date from the RunId prefix (format: yyyyMMdd-HHmmss-*).
/// Falls back to null if the RunId does not match the expected format.
/// </summary>
private static DateTime? TryParseRunDate(string? runId)
{
    if (string.IsNullOrWhiteSpace(runId) || runId.Length < 15) return null;
    if (DateTime.TryParseExact(runId[..15], "yyyyMMdd-HHmmss",
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out var dt))
        return dt;
    return null;
}
```

---

### Fix 5 — Robustness flags: link to strategy hub, add run link as secondary action

**Current code (Zone 3 flags column):**
```razor
<MudLink Href="@($"/backtests/{run.Id}")" Typo="Typo.body2">@run.ScenarioConfig.ScenarioId</MudLink>
```

**Replace with:**
```razor
<MudStack Row="true" AlignItems="AlignItems.Center" Spacing="1" Class="mb-1">
    @if (_strategyIdByType.TryGetValue(run.ScenarioConfig.StrategyType, out var flagSid))
    {
        <MudLink Href="@($"/strategies/{flagSid}")" Typo="Typo.body2">
            @run.ScenarioConfig.StrategyType
        </MudLink>
    }
    else
    {
        <MudText Typo="Typo.body2">@run.ScenarioConfig.StrategyType</MudText>
    }
    <MudIconButton Icon="@Icons.Material.Filled.OpenInNew"
                   Size="MudBlazor.Size.Small"
                   Class="text-muted"
                   Href="@($"/backtests/{run.Id}")"
                   title="View run result" />
</MudStack>
```

---

### Fix 6 — Replace Zone 4 outline buttons with nav cards

**Current Zone 4 code:**
```razor
<MudGrid Class="mt-4">
    <MudItem xs="12" sm="4">
        <MudButton Variant="Variant.Outlined" FullWidth="true" Href="/research/explorer"
                   StartIcon="@Icons.Material.Filled.Science">Research Explorer</MudButton>
    </MudItem>
    <MudItem xs="12" sm="4">
        <MudButton Variant="Variant.Outlined" FullWidth="true" Href="/propfirm/evaluate"
                   StartIcon="@Icons.Material.Filled.AccountBalance">Prop Firm Lab</MudButton>
    </MudItem>
    <MudItem xs="12" sm="4">
        <MudButton Variant="Variant.Outlined" FullWidth="true" Href="/data"
                   StartIcon="@Icons.Material.Filled.Storage">Data Files</MudButton>
    </MudItem>
</MudGrid>
```

**Replace with:**
```razor
<MudGrid Class="mt-4">
    <MudItem xs="12" sm="4">
        <MudPaper Class="pa-3" Elevation="1" Style="cursor:pointer"
                  @onclick="@(() => Nav.NavigateTo("/research/explorer"))">
            <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                <MudIcon Icon="@Icons.Material.Filled.Science" Class="text-muted" />
                <MudStack Spacing="0">
                    <MudText Typo="Typo.subtitle2">Research Explorer</MudText>
                    <MudText Typo="Typo.caption" Class="text-muted">Browse all studies</MudText>
                </MudStack>
            </MudStack>
        </MudPaper>
    </MudItem>
    <MudItem xs="12" sm="4">
        <MudPaper Class="pa-3" Elevation="1" Style="cursor:pointer"
                  @onclick="@(() => Nav.NavigateTo("/propfirm/evaluate"))">
            <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                <MudIcon Icon="@Icons.Material.Filled.AccountBalance" Class="text-muted" />
                <MudStack Spacing="0">
                    <MudText Typo="Typo.subtitle2">Prop Firm Lab</MudText>
                    <MudText Typo="Typo.caption" Class="text-muted">Evaluate against firm rules</MudText>
                </MudStack>
            </MudStack>
        </MudPaper>
    </MudItem>
    <MudItem xs="12" sm="4">
        <MudPaper Class="pa-3" Elevation="1" Style="cursor:pointer"
                  @onclick="@(() => Nav.NavigateTo("/data"))">
            <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                <MudIcon Icon="@Icons.Material.Filled.Storage" Class="text-muted" />
                <MudStack Spacing="0">
                    <MudText Typo="Typo.subtitle2">Data Files</MudText>
                    <MudText Typo="Typo.caption" Class="text-muted">Manage market data</MudText>
                </MudStack>
            </MudStack>
        </MudPaper>
    </MudItem>
</MudGrid>
```

---

## Task Group 2 — `StrategyLibrary.razor`

**File:** `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor`

### Fix 7 — Remove the `[Run]` card action; replace with `[New Version]`

**Current `MudCardActions`:**
```razor
<MudButton Size="MudBlazor.Size.Small" Variant="Variant.Text" Color="Color.Primary"
           Href="@($"/strategies/{s.StrategyId}")">Open Hub</MudButton>
<MudButton Size="MudBlazor.Size.Small" Variant="Variant.Text" Color="Color.Primary"
           Href="@($"/backtests/new?strategy={s.StrategyType}")">Run</MudButton>
<MudButton Size="MudBlazor.Size.Small" Variant="Variant.Text"
           Href="@($"/strategies/builder?fromStrategyId={s.StrategyId}")">Clone</MudButton>
```

**Replace with:**
```razor
<MudButton Size="MudBlazor.Size.Small" Variant="Variant.Filled" Color="Color.Primary"
           Href="@($"/strategies/{s.StrategyId}")">Open Hub</MudButton>
<MudButton Size="MudBlazor.Size.Small" Variant="Variant.Text"
           Href="@($"/strategies/builder?fromStrategyId={s.StrategyId}&asVersion=true")">
    New Version
</MudButton>
<MudButton Size="MudBlazor.Size.Small" Variant="Variant.Text"
           Href="@($"/strategies/builder?fromStrategyId={s.StrategyId}")">Clone</MudButton>
```

Rationale: runs happen from the hub, not from the library. `Open Hub` is now the primary
(filled) action to reinforce this hierarchy. `New Version` replaces `Run` as the secondary
action, which is more useful from a library view. `Clone` remains.

---

### Fix 8 — Mark "Stale" filter option as pending until date logic is implemented

**Current filter dropdown:**
```razor
<MudSelectItem Value="@("Stale")">Stale</MudSelectItem>
```

**Replace with:**
```razor
<MudSelectItem Value="@("Stale")" Disabled="true">Stale (coming soon)</MudSelectItem>
```

Also update `GetStatus()` in `@code` to add a comment:
```csharp
private static string GetStatus(StrategyIdentity s, BacktestResult? lastRun)
{
    if (lastRun is null) return "Untested";
    // TODO (V3.3): Add Stale detection once BacktestResult.RunDate or CreatedAt
    // is reliably available. Until then, all strategies with any run are "Active".
    return "Active";
}
```

---

### Fix 9 — Add a `_lastRunDate` display to each card

In `@code`, the current `_lastRunMap` uses `StrategyType` as the key. Add a comment marking
this as a known limitation:
```csharp
// NOTE: _lastRunMap is currently keyed on StrategyType, not StrategyId.
// This means two strategies with the same StrategyType will share the same last-run entry.
// TODO (V3.3): Re-key on StrategyId via StrategyVersionId once that FK is reliably populated
// on BacktestResult. For now this is an accepted limitation.
_lastRunMap = allRuns
    .GroupBy(r => r.ScenarioConfig.StrategyType)
    .ToDictionary(g => g.Key, g => g.First());
```

In the card markup, add a last-run date line below the Sharpe/DD metrics. Add it inside the
`@if (lastRun is not null)` block, after the `MudStack` row:
```razor
<MudText Typo="Typo.caption" Class="text-muted mt-2">
    Last run: @(TryParseRunDate(lastRun.RunId)?.ToString("yyyy-MM-dd") ?? lastRun.RunId[..Math.Min(8, lastRun.RunId.Length)])
</MudText>
```

Add the same `TryParseRunDate` helper used in `Dashboard.razor` to `@code` here:
```csharp
private static DateTime? TryParseRunDate(string? runId)
{
    if (string.IsNullOrWhiteSpace(runId) || runId.Length < 15) return null;
    if (DateTime.TryParseExact(runId[..15], "yyyyMMdd-HHmmss",
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out var dt))
        return dt;
    return null;
}
```

---

### Fix 10 — Add robustness flag indicator to cards

In `@code`, add a new field:
```csharp
private HashSet<string> _strategiesWithWarnings = new();
```

In `OnInitializedAsync`, after `_lastRunMap` is built, compute warnings per strategy type:
```csharp
foreach (var (type, run) in _lastRunMap)
{
    bool hasWarning =
        (run.SharpeRatio > 3.0m) ||
        (run.TotalTrades < 30) ||
        (run.EquityCurveSmoothness < 0m) ||
        (run.MaxDrawdown > 0.20m);
    if (hasWarning) _strategiesWithWarnings.Add(type);
}
```

In the card markup, inside `MudCardContent`, add a warning chip below the status badge when
applicable. Place it after the `MudChip` for strategy type:
```razor
@if (_strategiesWithWarnings.Contains(s.StrategyType))
{
    <MudChip T="string" Size="MudBlazor.Size.Small" Color="Color.Warning"
             Icon="@Icons.Material.Filled.Warning" Class="mb-2">
        Robustness flags
    </MudChip>
}
```

---

## Task Group 3 — `StrategyDetail.razor`

**File:** `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyDetail.razor`

### Fix 11 — Add breadcrumb above the header band

Immediately before the `<MudPaper>` header band (the first element inside the `else` block),
insert:
```razor
<MudBreadcrumbs Items="@_breadcrumbs" Class="pa-0 mb-2" Separator=">" />
```

In `@code`, add the field and populate it in `OnInitializedAsync` after `_strategy` is loaded:
```csharp
private List<BreadcrumbItem> _breadcrumbs = new();

// In OnInitializedAsync, after _strategy is confirmed not null:
_breadcrumbs = new List<BreadcrumbItem>
{
    new("Dashboard", href: "/"),
    new("My Strategies", href: "/strategies/library"),
    new(_strategy.StrategyName, href: null, disabled: true)
};
```

---

### Fix 12 — Add overflow menu for Edit and Clone to header band

In the header band `MudStack`, add a `MudMenu` after the `[Run]` button:
```razor
<MudMenu Icon="@Icons.Material.Filled.MoreVert"
         Variant="Variant.Outlined"
         Size="MudBlazor.Size.Small"
         AnchorOrigin="Origin.BottomRight"
         TransformOrigin="Origin.TopRight">
    <MudMenuItem Href="@($"/strategies/builder?fromStrategyId={_strategy.StrategyId}&fromVersionId={_selectedVersion?.StrategyVersionId}&asVersion=true")">
        New Version from This
    </MudMenuItem>
    <MudMenuItem OnClick="OpenRenameDialog">Rename Strategy</MudMenuItem>
    <MudMenuItem Href="@($"/strategies/builder?fromStrategyId={_strategy.StrategyId}")">
        Clone as New Strategy
    </MudMenuItem>
</MudMenu>
```

Add a minimal rename dialog at the bottom of the template (alongside the existing run dialog):
```razor
<MudDialog @bind-Visible="_renameDialogOpen">
    <TitleContent>Rename Strategy</TitleContent>
    <DialogContent>
        <MudTextField @bind-Value="_newName" Label="Strategy Name"
                      Variant="Variant.Outlined" AutoFocus="true" />
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => _renameDialogOpen = false)">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="ConfirmRename"
                   Disabled="@string.IsNullOrWhiteSpace(_newName)">Save</MudButton>
    </DialogActions>
</MudDialog>
```

Add to `@code`:
```csharp
private bool _renameDialogOpen;
private string _newName = "";

private void OpenRenameDialog()
{
    _newName = _strategy?.StrategyName ?? "";
    _renameDialogOpen = true;
}

private async Task ConfirmRename()
{
    if (_strategy is null || string.IsNullOrWhiteSpace(_newName)) return;
    // NOTE: Assumes IStrategyRepository exposes a RenameAsync or UpdateAsync method.
    // If not, call SaveAsync with a mutated copy of _strategy.
    // TODO: Verify the correct method signature on IStrategyRepository.
    _strategy = _strategy with { StrategyName = _newName };
    await StrategyRepo.SaveAsync(_strategy);
    _breadcrumbs[^1] = new(_newName, href: null, disabled: true);
    _renameDialogOpen = false;
    StateHasChanged();
}
```

---

### Fix 13 — Fix `async void` on `OnVersionChanged`

**Current code:**
```csharp
private async void OnVersionChanged(StrategyVersion v)
{
    _selectedVersion = v;
    await LoadVersionData();
    StateHasChanged();
}
```

**Replace with:**
```csharp
private async Task OnVersionChanged(StrategyVersion v)
{
    _selectedVersion = v;
    await LoadVersionData();
    StateHasChanged();
}
```

Update the binding on the version selector `MudSelect` to use the async delegate:
```razor
ValueChanged="@(async (StrategyVersion v) => await OnVersionChanged(v))"
```

---

### Fix 14 — Make left panel parameters inline-editable

Replace the read-only `MudSimpleTable` for parameters with editable fields and dirty-state tracking.

**Add fields to `@code`:**
```csharp
private Dictionary<string, string> _editableParams = new();
private bool _paramsDirty;
```

**In `LoadVersionData()`, after setting `_selectedVersion`, initialise the editable copy:**
```csharp
_editableParams = _selectedVersion.Parameters
    .ToDictionary(p => p.Key, p => p.Value);
_paramsDirty = false;
```

**Replace the PARAMETERS section of the left panel** (the `MudSimpleTable` for params):
```razor
<MudText Typo="Typo.caption" Class="text-faint mb-1">PARAMETERS</MudText>
@foreach (var key in _editableParams.Keys.ToList())
{
    @if (IsBoolParam(key))
    {
        <MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-2">
            <MudText Typo="Typo.body2" Class="text-muted flex-grow-1">@key</MudText>
            <MudSwitch Value="@GetBoolParam(key)"
                       ValueChanged="@((bool v) => SetBoolParam(key, v))"
                       Color="Color.Primary" />
        </MudStack>
    }
    else if (IsNumericParam(key))
    {
        <MudTextField Label="@key"
                      Value="@_editableParams[key]"
                      ValueChanged="@((string v) => OnParamChanged(key, v))"
                      InputType="InputType.Number"
                      Variant="Variant.Outlined"
                      Margin="Margin.Dense"
                      Class="mb-2" />
    }
    else
    {
        <MudTextField Label="@key"
                      Value="@_editableParams[key]"
                      ValueChanged="@((string v) => OnParamChanged(key, v))"
                      Variant="Variant.Outlined"
                      Margin="Margin.Dense"
                      Class="mb-2" />
    }
}
```

**After the EXECUTION `MudSimpleTable`**, add the Save/Discard controls:
```razor
@if (_paramsDirty)
{
    <MudDivider Class="my-3" />
    <MudButton Variant="Variant.Outlined" Color="Color.Primary" FullWidth="true"
               Size="MudBlazor.Size.Small" OnClick="SaveVersionParams" Class="mb-2">
        Save Changes
    </MudButton>
    <MudButton Variant="Variant.Text" FullWidth="true"
               Size="MudBlazor.Size.Small" OnClick="DiscardParamChanges"
               Class="text-muted">
        Discard
    </MudButton>
}
```

**Add helper methods to `@code`:**
```csharp
private void OnParamChanged(string key, string value)
{
    _editableParams[key] = value;
    _paramsDirty = true;
}

private bool GetBoolParam(string key) =>
    _editableParams.TryGetValue(key, out var v) &&
    (v == "true" || v == "True" || v == "1");

private void SetBoolParam(string key, bool value)
{
    _editableParams[key] = value ? "true" : "false";
    _paramsDirty = true;
}

private static bool IsBoolParam(string key)
{
    var k = key.ToLowerInvariant();
    return k.Contains("exit") && k.Contains("middle") ||
           k.StartsWith("use") || k.StartsWith("enable") || k.StartsWith("allow");
}

private static bool IsNumericParam(string key)
{
    var k = key.ToLowerInvariant();
    return k.Contains("period") || k.Contains("length") || k.Contains("lookback") ||
           k.Contains("bars") || k.Contains("threshold") || k.Contains("multiplier") ||
           k.Contains("stddev") || k.Contains("oversold") || k.Contains("overbought") ||
           k.Contains("cash") || k.Contains("size") || k.Contains("ratio");
}

private async Task SaveVersionParams()
{
    if (_selectedVersion is null) return;
    // Merge _editableParams back into the version and persist.
    // NOTE: StrategyVersion.Parameters is a Dictionary<string,string>.
    // If StrategyVersion is immutable (record type), use a `with` expression:
    var updated = _selectedVersion with { Parameters = new Dictionary<string, string>(_editableParams) };
    await StrategyRepo.SaveVersionAsync(updated);
    _selectedVersion = updated;
    // Refresh the versions list so the selector stays in sync.
    _versions = (await StrategyRepo.GetVersionsAsync(StrategyId)).ToList();
    _paramsDirty = false;
    Snackbar.Add("Parameters saved.", Severity.Success);
    StateHasChanged();
}

private async Task DiscardParamChanges()
{
    if (_selectedVersion is null) return;
    _editableParams = _selectedVersion.Parameters.ToDictionary(p => p.Key, p => p.Value);
    _paramsDirty = false;
    StateHasChanged();
    await Task.CompletedTask;
}
```

**Update `ConfirmRun()` to warn when params are dirty:**

Replace the start of `ConfirmRun()`:
```csharp
private async Task ConfirmRun()
{
    if (_selectedVersion is null) return;

    // If params have been edited but not saved, auto-save before running.
    if (_paramsDirty)
    {
        await SaveVersionParams();
        Snackbar.Add("Parameters saved before run.", Severity.Info);
    }

    _running = true;
    _runDialogOpen = false;
    StateHasChanged();
    // ... rest of existing ConfirmRun logic unchanged ...
```

---

### Fix 15 — Fix Walk-Forward and Sweep launch links to use `resultId`

**Current Research tab launch bar:**
```razor
<MudButton ... Href="@($"/research/walkforward?strategy={_strategy.StrategyType}")">Walk-Forward</MudButton>
<MudButton ... Href="@($"/research/sweep?strategy={_strategy.StrategyType}")">Sweep</MudButton>
```

**Replace with:**
```razor
<MudButton Variant="Variant.Outlined" Size="MudBlazor.Size.Small"
           Href="@($"/research/walkforward?resultId={_latestRun!.Id}")">
    Walk-Forward
</MudButton>
<MudButton Variant="Variant.Outlined" Size="MudBlazor.Size.Small"
           Href="@($"/research/sweep?resultId={_latestRun!.Id}")">
    Sweep
</MudButton>
```

These buttons are already inside the `@if (_latestRun is not null)` guard, so the null-forgiving
operator is safe. Add a TODO comment above both lines:
```razor
@* TODO (V3.3): Verify that WalkForward.razor and Sweep.razor accept [SupplyParameterFromQuery]
   string? ResultId and pre-load the result. If they currently only accept ?strategy=, update
   those pages to accept resultId as well. *@
```

---

### Fix 16 — Add run date to Prop Firm tab run selector

**Current `ToStringFunc`:**
```csharp
ToStringFunc="@(r => r is null ? "" : $"Sharpe {r.SharpeRatio?.ToString("F2") ?? "—"} · {r.TotalTrades} trades")"
```

**Replace with:**
```csharp
ToStringFunc="@(r => r is null ? "" : $"{TryParseRunDate(r.RunId)?.ToString("yyyy-MM-dd") ?? r.RunId[..Math.Min(8,r.RunId.Length)]} — Sharpe {r.SharpeRatio?.ToString("F2") ?? "—"}")"
```

Also add corresponding display inside the `MudSelectItem` so the dropdown itself is readable:
```razor
@foreach (var run in _versionRuns.Where(r => r.Status == BacktestStatus.Completed))
{
    <MudSelectItem Value="@run">
        @(TryParseRunDate(run.RunId)?.ToString("yyyy-MM-dd") ?? run.RunId[..Math.Min(8, run.RunId.Length)])
        — Sharpe @(run.SharpeRatio?.ToString("F2") ?? "—")
        · @run.TotalTrades trades
    </MudSelectItem>
}
```

Add the `TryParseRunDate` helper (same implementation as in Dashboard and Library):
```csharp
private static DateTime? TryParseRunDate(string? runId)
{
    if (string.IsNullOrWhiteSpace(runId) || runId.Length < 15) return null;
    if (DateTime.TryParseExact(runId[..15], "yyyyMMdd-HHmmss",
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out var dt))
        return dt;
    return null;
}
```

> **Note for V3.3:** `TryParseRunDate` is now duplicated in three files. Extract to a static
> helper class `RunIdHelper` in the Web project's `Helpers/` folder and replace all three
> usages with `RunIdHelper.TryParseRunDate(runId)`.

---

### Fix 17 — Mark `LoadBuiltInPacks` duplication for extraction

Add the following comment directly above `LoadBuiltInPacks()` in `StrategyDetail.razor`:

```csharp
// TODO (V3.3): This method is duplicated in PropFirmEvaluation.razor.
// Extract to a shared IPropFirmPackLoader service registered in Program.cs,
// inject it here and in PropFirmEvaluation.razor, and remove both static copies.
private static List<PropFirmRulePack> LoadBuiltInPacks()
```

---

## Acceptance Criteria

Verify the following after all fixes are applied:

### Dashboard
- [ ] Strategy strip left-border colour adapts via CSS variables — no hardcoded hex values
- [ ] Recent Runs table has a Date column showing the run date parsed from RunId
- [ ] Strategy column in Recent Runs is a clickable link to the strategy hub
- [ ] Strategies with no matching hub link still render plain text (no broken links)
- [ ] Robustness flag rows link to the strategy hub; secondary icon links to the run result
- [ ] Zone 4 shows three nav cards with icon + title + description (no outline buttons)

### Strategy Library
- [ ] Card actions are: `[Open Hub]` (filled primary), `[New Version]` (text), `[Clone]` (text)
- [ ] No card action navigates to `/backtests/new`
- [ ] "Stale" filter option is disabled with "(coming soon)" label
- [ ] Each card with runs shows the last run date below the Sharpe/DD metrics
- [ ] Strategies with robustness flags show a warning chip on their card
- [ ] `_lastRunMap` code comment acknowledges the StrategyType key limitation

### Strategy Detail
- [ ] Breadcrumb shows Dashboard › My Strategies › {StrategyName} above the header band
- [ ] Header band includes an overflow `⋮` menu with: New Version, Rename, Clone
- [ ] Rename dialog opens, allows editing name, saves via StrategyRepo, updates breadcrumb
- [ ] Left panel parameters render as editable fields (MudSwitch for bools, number input for numerics, text for others)
- [ ] Save Changes / Discard buttons appear only when a parameter has been changed
- [ ] Clicking Run while params are dirty auto-saves before running and shows an info snackbar
- [ ] Discarding restores fields to last-saved values
- [ ] Walk-Forward and Sweep buttons in the Research tab pass `?resultId=` not `?strategy=`
- [ ] Prop Firm run selector shows date prefix in both the trigger display and dropdown items
- [ ] `OnVersionChanged` is `async Task`, not `async void`
- [ ] `LoadBuiltInPacks` has a TODO comment marking it for extraction
