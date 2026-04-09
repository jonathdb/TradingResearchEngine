<img src="https://r2cdn.perplexity.ai/pplx-full-logo-primary-dark%402x.png" style="height:64px;margin-right:32px"/>

# Thank you. I still feel like the UI is not the most usable. I want you to look at the User Experience and see what can be done to create improvements from the same repo source. I will give you some user goals, ideas and specs that i would like to be important for the design implementation.

1. As a user, i would like the different strategies i create (or default ones) to be the core interactive element of this application. I want to be able to create new strategy, either from scratch, or based on one in the library.
2. When i have created a strategy, i would like for this strategy page to be the "hub"  of all the research and testing for that strategy. When i run a backtest, the strategy should not need to be changed before running, all the variables should be a part of the main strategy.
3. If i would like to try different variables for the same strategy, then that can be created as a version of that strategy, that may be accessed from the same strategy page (could be differentiated through dropdown, tabs, whatever would be best)
4. All the runs for that strategy would be accessible from this page. The runs made when backtesting could be shown, while the runs made when doing walk-forward, monte carlo, etc. Should not be included in these lists, they should only be shown as a part of the statistics those tests create.
5. When i want to do reasearch with this backtesting run, i would do this from this "hub" I still would like to be able to have separate menu items on the side, but i should also be able to do evberything from this hub.
6. When i want to do prop firm testing, i need to be able to choose from these already tested strategies, with version. Not need to create something from scratch.
7. I want to make sure that the application puts these strategies first as the user experience. The main Dashboard should reflect these strategies, and maybe create some way of seeing/filtering on these from the dashboard page? Could be an idea if not to complex to implement.

I now have a complete picture of the whole application. Here is the full Kiro-ready UX restructuring prompt.

***

## Kiro Implementation Prompt — V3.2 Strategy-First UX Restructure

This is a UX architecture overhaul. The goal is to make the **Strategy** the central, persistent object of the entire application — the hub from which all backtesting, research, and prop firm evaluation flows. Do not change domain logic, repositories, or application services. All changes are to `.razor` files, layout, and routing.

***

## The Core Mental Model to Implement

The application currently treats runs, studies, and evaluations as independent objects that happen to relate to a strategy. The new model is:

```
Strategy (the persistent identity)
  └── Version (parameter variants of the same strategy)
        ├── Parameters (market, execution, rules — live on the version)
        ├── Backtest Runs (standard runs only — user-initiated, one-click)
        │     └── Research Studies (Monte Carlo, Walk-Forward, Sweep, etc.)
        │           └── (studies are child records of a run, not surfaced as independent runs)
        └── Prop Firm Evaluations (always scoped to a strategy + version + run)
```

Every page in the app should respect this hierarchy. Nothing should require the user to re-enter strategy context they've already established.

***

## Task Group 1 — Strategy Library: From Table to Card Gallery

**File:** `Components/Pages/Strategies/StrategyLibrary.razor`

### Current problem

The library presents strategies as a flat list or table . It doesn't communicate status, health, or progress. Users can't act on a strategy from this view.

### Required changes

**1. Replace the list/table layout with a card grid.** Each card (`MudCard`) represents one strategy and must show:

- Strategy name (`Typo.subtitle1`, bold)
- Strategy type as a small `MudChip` (e.g. "MeanReversion", "Momentum")
- Market + Timeframe: one line, muted text (e.g. "ES · 15m")
- Version count: "3 versions" — link to the strategy hub
- **Last run summary** (most recent `BacktestResult` for any version of this strategy):
    - Sharpe: coloured value (green ≥ 1.5, amber ≥ 0.8, red < 0.8)
    - Max DD: plain value
    - Date of last run: muted, relative format ("3 days ago" if possible, otherwise `yyyy-MM-dd`)
- **Status badge** (top-right corner of card): one of:
    - `Active` (has runs in last 30 days) — `Color.Success`
    - `Stale` (has runs, but >30 days ago) — `Color.Warning`
    - `Untested` (no runs at all) — `Color.Default`
- **Card footer actions** (`MudCardActions`):
    - `[Open Hub]` → `/strategies/{strategyId}` (primary action)
    - `[Run]` → triggers a one-click run from library (see Task Group 2)
    - `[New Version]` → `/strategies/{strategyId}/version/new`

**2. Add a filter/search bar above the card grid:**

```razor
<MudStack Row="true" AlignItems="AlignItems.Center" Class="mb-4" Spacing="3">
    <MudTextField @bind-Value="_search" Placeholder="Search strategies..."
                  Adornment="Adornment.Start" AdornmentIcon="@Icons.Material.Filled.Search"
                  Variant="Variant.Outlined" Immediate="true" Class="flex-grow-1" />
    <MudSelect T="string" @bind-Value="_typeFilter" Label="Type" Variant="Variant.Outlined"
               Style="min-width:160px" Clearable="true">
        @foreach (var t in _allTypes)
        { <MudSelectItem Value="@t">@t</MudSelectItem> }
    </MudSelect>
    <MudSelect T="string" @bind-Value="_statusFilter" Label="Status" Variant="Variant.Outlined"
               Style="min-width:140px" Clearable="true">
        <MudSelectItem Value="@("Active")">Active</MudSelectItem>
        <MudSelectItem Value="@("Stale")">Stale</MudSelectItem>
        <MudSelectItem Value="@("Untested")">Untested</MudSelectItem>
    </MudSelect>
    <MudButton Variant="Variant.Filled" Color="Color.Primary"
               StartIcon="@Icons.Material.Filled.Add"
               Href="/strategies/new">New Strategy</MudButton>
</MudStack>
```

Filter the displayed cards reactively based on `_search` (name contains), `_typeFilter` (exact match), and `_statusFilter`.

**3. Add an empty state** when no strategies exist or no strategies match filters:

```razor
<MudStack AlignItems="AlignItems.Center" Class="pa-16">
    <MudIcon Icon="@Icons.Material.Filled.Psychology" Size="Size.Large" Class="text-faint mb-3"/>
    <MudText Typo="Typo.h6">No strategies yet</MudText>
    <MudText Typo="Typo.body2" Class="text-muted mb-4">
        Create your first strategy to start backtesting
    </MudText>
    <MudButton Variant="Variant.Filled" Color="Color.Primary" Href="/strategies/new">
        Create Strategy
    </MudButton>
</MudStack>
```


***

## Task Group 2 — Strategy Hub: The Central Command Page

**File:** `Components/Pages/Strategies/StrategyDetail.razor`

This page becomes the **primary working surface** of the entire application. The user should be able to do everything here: configure parameters, run backtests, launch research studies, evaluate for prop firms, and review all historical results.

### Layout Architecture

Replace the current single-column layout with a **two-zone layout**:

```
┌────────────────────────────────────────────────────────────────┐
│  STRATEGY HEADER BAND                                          │
│  [Name]  [Type chip]  [Market · TF]  [Status badge]           │
│  [Version Selector ▼]  ──────────────  [▶ Run]  [⋮ More]     │
├──────────────────────┬─────────────────────────────────────────┤
│  LEFT PANEL (md=4)   │  RIGHT PANEL (md=8)                     │
│  Version Config      │  Tabbed Content Area                    │
│  · Parameters        │  · Runs  · Research  · Prop Firm        │
│  · Market & Data     │                                         │
│  · Execution         │                                         │
└──────────────────────┴─────────────────────────────────────────┘
```


### Header Band

```razor
<MudPaper Elevation="0" Class="pa-4 mb-0" Style="border-bottom: 1px solid var(--mud-palette-divider)">
    <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="3" Wrap="Wrap.Wrap">
        <MudStack Spacing="0">
            <MudStack Row="true" AlignItems="AlignItems.Center" Spacing="2">
                <MudText Typo="Typo.h5">@_strategy.StrategyName</MudText>
                <MudChip T="string" Size="MudBlazor.Size.Small" Color="Color.Default">
                    @_strategy.StrategyType
                </MudChip>
                <MudChip T="string" Size="MudBlazor.Size.Small" Color="@GetStatusColor()">
                    @GetStatusLabel()
                </MudChip>
            </MudStack>
            <MudText Typo="Typo.body2" Class="text-muted">
                @(_selectedVersion?.Symbol ?? "—") · @(_selectedVersion?.Timeframe ?? "—")
            </MudText>
        </MudStack>
        <MudSpacer />
        <!-- Version selector -->
        <MudSelect T="StrategyVersion" @bind-Value="_selectedVersion"
                   Label="Version" Variant="Variant.Outlined"
                   Style="min-width:200px"
                   ToStringFunc="@(v => v is null ? "" : $"v{v.VersionNumber} — {v.Notes ?? v.CreatedAt.ToString("yyyy-MM-dd")}")">
            @foreach (var v in _versions)
            {
                <MudSelectItem Value="@v">
                    v@(v.VersionNumber) — @(v.Notes ?? v.CreatedAt.ToString("yyyy-MM-dd"))
                </MudSelectItem>
            }
        </MudSelect>
        <!-- Primary action -->
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   StartIcon="@Icons.Material.Filled.PlayArrow"
                   OnClick="RunBacktest"
                   Disabled="@(_selectedVersion is null)">Run</MudButton>
        <!-- Secondary actions menu -->
        <MudMenu Icon="@Icons.Material.Filled.MoreVert" Variant="Variant.Outlined" Size="MudBlazor.Size.Small">
            <MudMenuItem Href="@($"/strategies/{_strategy.StrategyId}/version/new")">
                New Version from This
            </MudMenuItem>
            <MudMenuItem OnClick="EditStrategy">Edit Strategy Name</MudMenuItem>
            <MudMenuItem Href="@($"/strategies/{_strategy.StrategyId}/clone")">
                Clone Strategy
            </MudMenuItem>
        </MudMenu>
    </MudStack>
</MudPaper>
```


### Left Panel — Version Config

The left panel displays and allows editing of the **currently selected version's parameters**. This replaces the current "Parameters" tab entirely — parameters are always visible alongside the results, not hidden in a tab.

```razor
<MudItem xs="12" md="4">
    <MudPaper Class="pa-4" Elevation="1" Style="position: sticky; top: 80px;">
        <MudText Typo="Typo.subtitle2" Class="text-muted mb-3">VERSION PARAMETERS</MudText>

        <!-- Market & Data section -->
        <MudText Typo="Typo.caption" Class="text-faint mb-1">MARKET & DATA</MudText>
        <MudStack Spacing="2" Class="mb-3">
            <MudTextField Label="Symbol" @bind-Value="_selectedVersion.Symbol"
                          Variant="Variant.Outlined" Margin="Margin.Dense" />
            <MudTextField Label="Timeframe" @bind-Value="_selectedVersion.Timeframe"
                          Variant="Variant.Outlined" Margin="Margin.Dense" />
            <!-- Data file picker: existing MudSelect binding to _dataFiles -->
        </MudStack>

        <MudDivider Class="mb-3" />

        <!-- Strategy Parameters section (dynamic, from _selectedVersion.Parameters dict) -->
        <MudText Typo="Typo.caption" Class="text-faint mb-1">PARAMETERS</MudText>
        <MudStack Spacing="2" Class="mb-3">
            @foreach (var param in _selectedVersion.Parameters)
            {
                <!-- Render type-aware fields: MudSwitch for bool, InputType.Number for numeric -->
            }
        </MudStack>

        <MudDivider Class="mb-3" />

        <!-- Execution section -->
        <MudText Typo="Typo.caption" Class="text-faint mb-1">EXECUTION</MudText>
        <MudStack Spacing="2" Class="mb-4">
            <MudTextField Label="Initial Cash" @bind-Value="_selectedVersion.InitialCash"
                          InputType="InputType.Number" Variant="Variant.Outlined" Margin="Margin.Dense" />
            <!-- Realism profile select, slippage, commission -->
        </MudStack>

        <!-- Save button — only visible when params have been changed -->
        @if (_paramsDirty)
        {
            <MudButton Variant="Variant.Outlined" Color="Color.Primary" FullWidth="true"
                       OnClick="SaveVersionParams" Class="mb-2">
                Save Changes
            </MudButton>
            <MudButton Variant="Variant.Text" FullWidth="true" OnClick="DiscardChanges"
                       Class="text-muted">Discard</MudButton>
        }
    </MudPaper>
</MudItem>
```

Track `_paramsDirty` by comparing current field values against the last-saved snapshot on any input change. Only show Save/Discard when changes have been made.

### Right Panel — Tabbed Content

Three tabs: **Runs**, **Research**, **Prop Firm**.

#### Tab 1 — Runs

This tab shows only **standard user-initiated backtest runs** for the selected version. Walk-forward, Monte Carlo, and sweep sub-runs must **not** appear here — they appear inside the Research tab.

```razor
<MudTabPanel Text="Runs" BadgeData="@_backtestRuns.Count" BadgeColor="Color.Default">
    @if (_backtestRuns.Count == 0)
    {
        <!-- Empty state with [Run Now] CTA -->
    }
    else
    {
        <!-- KPI summary bar for the best run -->
        <MudPaper Outlined="true" Class="pa-3 mb-4">
            <MudStack Row="true" Spacing="6">
                <MudStack Spacing="0">
                    <MudText Typo="Typo.caption" Class="text-muted">Best Sharpe</MudText>
                    <MudText Typo="Typo.h5" Color="Color.Success">
                        @_backtestRuns.Max(r => r.SharpeRatio)?.ToString("F2")
                    </MudText>
                </MudStack>
                <MudStack Spacing="0">
                    <MudText Typo="Typo.caption" Class="text-muted">Latest Sharpe</MudText>
                    <MudText Typo="Typo.h5">@_latestRun?.SharpeRatio?.ToString("F2")</MudText>
                </MudStack>
                <MudStack Spacing="0">
                    <MudText Typo="Typo.caption" Class="text-muted">Total Runs</MudText>
                    <MudText Typo="Typo.h5">@_backtestRuns.Count</MudText>
                </MudStack>
                <MudStack Spacing="0">
                    <MudText Typo="Typo.caption" Class="text-muted">Last Run</MudText>
                    <MudText Typo="Typo.h5">@_latestRun?.RunDate?.ToString("yyyy-MM-dd")</MudText>
                </MudStack>
            </MudStack>
        </MudPaper>

        <!-- Runs table -->
        <MudTable Items="@_backtestRuns" Dense="true" Hover="true"
                  RowClassFunc="@((r, i) => r.Id == _latestRun?.Id ? "mud-selected-row" : "")">
            <HeaderContent>
                <MudTh>Date</MudTh>
                <MudTh>Sharpe</MudTh>
                <MudTh>Max DD</MudTh>
                <MudTh>Win Rate</MudTh>
                <MudTh>Trades</MudTh>
                <MudTh>Status</MudTh>
                <MudTh></MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd>@context.RunDate?.ToString("yyyy-MM-dd HH:mm")</MudTd>
                <MudTd Class="@GetSharpeClass(context.SharpeRatio)">
                    @context.SharpeRatio?.ToString("F2")
                </MudTd>
                <MudTd>@context.MaxDrawdown?.ToString("P1")</MudTd>
                <MudTd>@context.WinRate?.ToString("P1")</MudTd>
                <MudTd>@context.TotalTrades</MudTd>
                <MudTd><MudChip T="string" Size="MudBlazor.Size.Small">@context.Status</MudChip></MudTd>
                <MudTd>
                    <MudIconButton Icon="@Icons.Material.Filled.OpenInNew" Size="MudBlazor.Size.Small"
                                   Href="@($"/backtests/{context.Id}")" />
                </MudTd>
            </RowTemplate>
        </MudTable>
    }
</MudTabPanel>
```

Add a helper:

```csharp
private string GetSharpeClass(double? sharpe) => sharpe switch
{
    >= 1.5 => "mud-success-text",
    >= 0.8 => "mud-warning-text",
    _ => "mud-error-text"
};
```


#### Tab 2 — Research

This tab lists all research **studies** for the selected version, launched from any of its backtest runs. Studies are grouped by type. Sub-runs generated by Walk-Forward or Monte Carlo are shown as aggregate statistics inside the study record — never as individual rows in the runs table.

```razor
<MudTabPanel Text="Research" BadgeData="@_studies.Count" BadgeColor="Color.Default">
    @if (_studies.Count == 0)
    {
        <MudStack AlignItems="AlignItems.Center" Class="pa-12">
            <MudIcon Icon="@Icons.Material.Filled.Science" Size="Size.Large" Class="text-faint mb-3"/>
            <MudText Typo="Typo.h6">No studies yet</MudText>
            <MudText Typo="Typo.body2" Class="text-muted mb-4">
                Open a backtest run, then launch a study from there
            </MudText>
            @if (_latestRun is not null)
            {
                <MudButton Variant="Variant.Outlined" Color="Color.Primary"
                           Href="@($"/backtests/{_latestRun.Id}")">
                    Open Latest Run →
                </MudButton>
            }
        </MudStack>
    }
    else
    {
        <!-- Launch bar for quick study access -->
        <MudPaper Outlined="true" Class="pa-3 mb-4">
            <MudStack Row="true" Spacing="2" AlignItems="AlignItems.Center" Wrap="Wrap.Wrap">
                <MudText Typo="Typo.caption" Class="text-muted mr-2">LAUNCH STUDY</MudText>
                @if (_latestRun is not null)
                {
                    <MudButton Variant="Variant.Outlined" Size="MudBlazor.Size.Small"
                               Href="@($"/research/montecarlo?resultId={_latestRun.Id}")">
                        Monte Carlo
                    </MudButton>
                    <MudButton Variant="Variant.Outlined" Size="MudBlazor.Size.Small"
                               Href="@($"/research/walkforward?resultId={_latestRun.Id}")">
                        Walk-Forward
                    </MudButton>
                    <MudButton Variant="Variant.Outlined" Size="MudBlazor.Size.Small"
                               Href="@($"/research/sweep?resultId={_latestRun.Id}")">
                        Parameter Sweep
                    </MudButton>
                    <MudButton Variant="Variant.Outlined" Size="MudBlazor.Size.Small"
                               Href="@($"/research/perturbation?resultId={_latestRun.Id}")">
                        Perturbation
                    </MudButton>
                }
                else
                {
                    <MudText Typo="Typo.body2" Class="text-muted">
                        Run a backtest first to launch studies
                    </MudText>
                }
            </MudStack>
        </MudPaper>

        <!-- Studies table -->
        <MudTable Items="@_studies" Dense="true" Hover="true">
            <HeaderContent>
                <MudTh>Type</MudTh>
                <MudTh>Status</MudTh>
                <MudTh>Source Run</MudTh>
                <MudTh>Created</MudTh>
                <MudTh>Summary</MudTh>
                <MudTh></MudTh>
            </HeaderContent>
            <RowTemplate>
                <MudTd><MudChip T="string" Size="MudBlazor.Size.Small">@context.StudyType</MudChip></MudTd>
                <MudTd><MudChip T="string" Size="MudBlazor.Size.Small" Color="@GetStudyStatusColor(context.Status)">@context.Status</MudChip></MudTd>
                <MudTd>
                    <MudLink Href="@($"/backtests/{context.SourceRunId}")">
                        @context.SourceRunId?.ToString()[..8]
                    </MudLink>
                </MudTd>
                <MudTd>@context.CreatedAt.ToString("yyyy-MM-dd")</MudTd>
                <MudTd Class="text-muted">@context.SummaryLine</MudTd>
                <MudTd>
                    <MudIconButton Icon="@Icons.Material.Filled.OpenInNew" Size="MudBlazor.Size.Small"
                                   Href="@($"/research/study/{context.StudyId}")" />
                </MudTd>
            </RowTemplate>
        </MudTable>
    }
</MudTabPanel>
```


#### Tab 3 — Prop Firm

This tab allows the user to evaluate **any existing run of the current strategy version** against a prop firm rule pack, without leaving the strategy hub. This replaces the need to navigate to `/propfirm/evaluate` from scratch.

```razor
<MudTabPanel Text="Prop Firm">
    <MudGrid>
        <MudItem xs="12" md="5">
            <MudPaper Class="pa-4" Elevation="1">
                <MudText Typo="Typo.subtitle2" Class="text-muted mb-3">EVALUATE A RUN</MudText>

                <!-- Run selector: only shows backtest runs for this version -->
                <MudSelect T="BacktestResult" @bind-Value="_evalRun" Label="Select Run"
                           Variant="Variant.Outlined" Class="mb-3"
                           ToStringFunc="@(r => r is null ? "" : $"{r.RunDate:yyyy-MM-dd} — Sharpe {r.SharpeRatio:F2}")">
                    @foreach (var run in _backtestRuns.Where(r => r.Status == "Completed"))
                    {
                        <MudSelectItem Value="@run">
                            @run.RunDate?.ToString("yyyy-MM-dd") — Sharpe @run.SharpeRatio?.ToString("F2")
                        </MudSelectItem>
                    }
                </MudSelect>

                <!-- Firm selector -->
                <MudSelect T="PropFirmRulePack" @bind-Value="_evalPack" Label="Firm & Challenge"
                           Variant="Variant.Outlined" Class="mb-4"
                           ToStringFunc="@(p => p is null ? "" : $"{p.FirmName} — {p.ChallengeName}")">
                    @foreach (var pack in _firmPacks)
                    {
                        <MudSelectItem Value="@pack">
                            @pack.FirmName — @pack.ChallengeName ($@pack.AccountSizeUsd.ToString("N0"))
                        </MudSelectItem>
                    }
                </MudSelect>

                <MudButton Variant="Variant.Filled" Color="Color.Primary" FullWidth="true"
                           OnClick="EvaluateForFirm"
                           Disabled="@(_evalRun is null || _evalPack is null)">
                    Evaluate
                </MudButton>

                <MudButton Variant="Variant.Text" FullWidth="true" Class="mt-2 text-muted"
                           Href="/propfirm/rulepack/new" Target="_blank">
                    + Add custom rule pack
                </MudButton>
            </MudPaper>
        </MudItem>

        <MudItem xs="12" md="7">
            <!-- Inline evaluation results — reuse the same rendering logic as PropFirmEvaluation.razor -->
            @if (_evalPhaseResults is not null)
            {
                <!-- Copy the phase result rendering from PropFirmEvaluation.razor here as a shared component -->
                <!-- TODO: Extract to a shared <EvaluationResults> component in Components/Shared/ -->
            }
        </MudItem>
    </MudGrid>

    <!-- Evaluation history for this strategy version -->
    @if (_pastEvaluations.Count > 0)
    {
        <MudDivider Class="my-4" />
        <MudText Typo="Typo.subtitle2" Class="text-muted mb-3">EVALUATION HISTORY</MudText>
        <!-- Table of past evaluations: Firm, Challenge, Run date, Pass/Fail -->
    }
</MudTabPanel>
```

In `@code`, inject `PropFirmEvaluator Evaluator` and `IFirmPackLoader` (or use the same `LoadBuiltInPacks()` pattern from `PropFirmEvaluation.razor`) . Load packs in `OnInitializedAsync`.

Add an `EvaluateForFirm()` method mirroring the `Evaluate()` method from `PropFirmEvaluation.razor` , storing results in `_evalPhaseResults` and `_evalAllPassed`.

***

## Task Group 3 — One-Click Run From the Hub

**File:** `Components/Pages/Strategies/StrategyDetail.razor`

When the user clicks the `[Run]` button in the header band, a backtest should launch using the currently selected version's parameters — with no separate form page required. Implement as an inline confirmation dialog, not a navigation away from the hub.

```razor
<!-- Inline run dialog triggered by the Run button -->
<MudDialog @bind-IsVisible="_runDialogOpen" Options="_runDialogOptions">
    <TitleContent>Run Backtest</TitleContent>
    <DialogContent>
        <MudStack Spacing="2">
            <MudText Typo="Typo.body1">
                Run <strong>@_strategy.StrategyName</strong>
                (v@(_selectedVersion?.VersionNumber)) on
                <strong>@_selectedVersion?.Symbol · @_selectedVersion?.Timeframe</strong>?
            </MudText>
            <MudText Typo="Typo.body2" Class="text-muted">
                Uses the parameters currently shown in the version config panel.
            </MudText>
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => _runDialogOpen = false)">Cancel</MudButton>
        <MudButton Variant="Variant.Filled" Color="Color.Primary"
                   OnClick="ConfirmRun">Run Now</MudButton>
    </DialogActions>
</MudDialog>
```

The `ConfirmRun()` method should:

1. Call the existing backtest execution service with the selected version's parameters
2. Close the dialog
3. Show a `Snackbar` with "Backtest started…"
4. Reload the Runs tab after completion (or use a polling/SignalR pattern if already implemented)

If the backtest service requires a full `ScenarioConfig` object, build it from `_selectedVersion` fields in `ConfirmRun()`.

***

## Task Group 4 — Strategy Creation: New and From Existing

**File:** `Components/Pages/Strategies/StrategyBuilder.razor` (new entry point: `/strategies/new`)

Add support for two creation modes via a query parameter:

```razor
@page "/strategies/new"
[SupplyParameterFromQuery] public string? FromStrategyId { get; set; }
[SupplyParameterFromQuery] public string? FromVersionId { get; set; }
```

In `OnInitializedAsync`, if `FromStrategyId` is provided:

1. Load the source strategy and its specified version (or latest version if `FromVersionId` is null)
2. Pre-fill all builder fields from that version's parameters
3. Set `_strategy.StrategyName` to `"{source name} (copy)"` — editable by the user
4. Show a contextual banner at the top of Step 1:

```razor
<MudAlert Severity="Severity.Info" Dense="true" Class="mb-3">
    Creating a copy of <strong>@_sourceStrategy.StrategyName</strong> v@(_sourceVersion.VersionNumber).
    You can modify any parameters before saving.
</MudAlert>
```


In `StrategyLibrary.razor`, the `[Clone]` card action should navigate to:

```
/strategies/new?fromStrategyId={strategyId}
```

And add a `[New Version from This]` button in the hub header menu navigating to:

```
/strategies/new?fromStrategyId={strategyId}&fromVersionId={versionId}&asVersion=true
```

When `asVersion=true`, the builder should save the result as a new `StrategyVersion` on the existing strategy rather than creating a new `Strategy` entity. Adjust the final `[Save]` action accordingly.

***

## Task Group 5 — Dashboard: Strategy-First View

**File:** `Components/Pages/Dashboard.razor`

The Dashboard is the first screen the user sees. It should be oriented entirely around strategies — their health, recent activity, and what needs attention.

### Replace the current layout with this structure:

**Zone 1 — Strategy Status Strip (top, full width)**

A horizontally scrollable row of strategy mini-cards. Each card is compact (approx 180px wide) and shows:

- Strategy name (truncated at 20 chars)
- Latest Sharpe (coloured)
- Status badge (Active / Stale / Untested)
- Click → navigates to that strategy hub

```razor
<MudText Typo="Typo.subtitle2" Class="text-muted mb-2">YOUR STRATEGIES</MudText>
<div style="display:flex; gap:12px; overflow-x:auto; padding-bottom:8px; margin-bottom:24px;">
    @foreach (var s in _strategies)
    {
        <MudPaper @onclick="@(() => NavigationManager.NavigateTo($"/strategies/{s.StrategyId}"))"
                  Class="pa-3 cursor-pointer"
                  Style="min-width:180px; border-left:3px solid @GetStatusColor(s);"
                  Elevation="1">
            <MudText Typo="Typo.subtitle2" Style="white-space:nowrap;overflow:hidden;text-overflow:ellipsis">
                @s.StrategyName
            </MudText>
            <MudText Typo="Typo.body2" Class="@GetSharpeClass(_lastRunMap.GetValueOrDefault(s.StrategyId))">
                Sharpe: @(_lastRunMap.GetValueOrDefault(s.StrategyId)?.SharpeRatio?.ToString("F2") ?? "—")
            </MudText>
            <MudText Typo="Typo.caption" Class="text-muted">@GetStatusLabel(s)</MudText>
        </MudPaper>
    }
    <!-- Add new strategy card -->
    <MudPaper Class="pa-3 d-flex align-center justify-center cursor-pointer"
              Style="min-width:180px; border:1px dashed var(--mud-palette-divider);"
              Elevation="0"
              @onclick="@(() => NavigationManager.NavigateTo("/strategies/new"))">
        <MudStack AlignItems="AlignItems.Center" Spacing="1">
            <MudIcon Icon="@Icons.Material.Filled.Add" Class="text-muted"/>
            <MudText Typo="Typo.caption" Class="text-muted">New Strategy</MudText>
        </MudStack>
    </MudPaper>
</div>
```

Build `_lastRunMap` as `Dictionary<string, BacktestResult>` keyed on `StrategyId` (or `StrategyType` if `StrategyId` is not yet on `BacktestResult`) in `OnInitializedAsync`.

**Zone 2 — Four KPI Tiles (below strategy strip)**

Replace current tiles with:

- **Strategies** — total count, clicking navigates to Library
- **Last Sharpe** — most recent backtest Sharpe across all strategies
- **Active Studies** — count of in-progress studies
- **Flags** — count of strategies with robustness warnings (links to the flags section below)

**Zone 3 — Two-column body**

Left column (`md=8`): **Recent Runs** — compact table, last 10 runs across all strategies, columns: Strategy (linked), Version, Sharpe, Date, `[Open]` button.

Right column (`md=4`): **Robustness Flags** — the warning watchlist from the current implementation, redesigned as a compact list (not `MudAlert` stack). Each row: strategy name (linked to hub), warning summary chips.

**Zone 4 — Quick Actions row (below body)**

Three action cards, equal width, no duplication of content above:

- `Research Explorer` → `/research/explorer`
- `Prop Firm Lab` → `/propfirm/evaluate`
- `Data Files` → `/data`

These are navigation shortcuts to the tool surfaces, not primary strategy actions.

***

## Task Group 6 — PropFirmEvaluation: Strategy-Aware Entry

**File:** `Components/Pages/PropFirm/PropFirmEvaluation.razor`

The standalone `/propfirm/evaluate` page should still exist for direct navigation, but must now be strategy-aware when reached from a strategy hub.

Add query parameter support:

```razor
[SupplyParameterFromQuery] public string? StrategyId { get; set; }
[SupplyParameterFromQuery] public string? VersionId { get; set; }
[SupplyParameterFromQuery] public string? ResultId { get; set; }
```

In `OnInitializedAsync`:

- If `ResultId` is provided, load that result and pre-select it in the `ResultPicker`
- If `StrategyId` is provided but no `ResultId`, filter the `ResultPicker` to only show runs for that strategy, and show a contextual banner:

```razor
<MudAlert Severity="Severity.Info" Dense="true" Class="mb-3">
    Evaluating <strong>@_strategyName</strong>. Showing runs for this strategy only.
    <MudLink Href="/propfirm/evaluate">Show all runs</MudLink>
</MudAlert>
```


Replace the `ResultPicker` component's data source with a filtered set when `StrategyId` is present. If `ResultPicker` doesn't support filtering natively, pass a filtered `IEnumerable<BacktestResult>` as a parameter to it.

***

## Task Group 7 — Navigation Update

**File:** `Components/Layout/NavMenu.razor`

With the strategy hub as the core, the nav should guide users there first. Update to:

```razor
<MudText Typo="Typo.overline" Class="px-4 pt-3 pb-1 text-faint">STRATEGIES</MudText>
<MudNavLink Href="/" Match="NavLinkMatch.All"
            Icon="@Icons.Material.Filled.Dashboard">Dashboard</MudNavLink>
<MudNavLink Href="/strategies/library"
            Icon="@Icons.Material.Filled.Psychology">My Strategies</MudNavLink>

<MudText Typo="Typo.overline" Class="px-4 pt-3 pb-1 text-faint">TOOLS</MudText>
<MudNavLink Href="/research/explorer"
            Icon="@Icons.Material.Filled.Science">Research Explorer</MudNavLink>
<MudNavLink Href="/propfirm/evaluate"
            Icon="@Icons.Material.Filled.AccountBalance">Prop Firm Lab</MudNavLink>

<MudText Typo="Typo.overline" Class="px-4 pt-3 pb-1 text-faint">SETTINGS</MudText>
<MudNavLink Href="/data"
            Icon="@Icons.Material.Filled.Storage">Data Files</MudNavLink>
<MudNavLink Href="/settings"
            Icon="@Icons.Material.Filled.Settings">Settings</MudNavLink>
```

The label change from "Strategy Library" to **"My Strategies"** reinforces ownership and aligns with the strategy-first framing .

***

## Acceptance Criteria

When complete, verify the following user journeys work end-to-end without any navigating away from context:

- [ ] **New user journey**: Dashboard → "New Strategy" card in strip → Builder → Save → redirects to Strategy Hub
- [ ] **Run journey**: Strategy Hub → parameters visible in left panel → `[Run]` button → inline dialog → confirm → snackbar → Runs tab updates
- [ ] **Clone journey**: Strategy Library → card `[Clone]` → Builder pre-filled with source params → save as new strategy → Hub
- [ ] **New version journey**: Strategy Hub header menu → "New Version from This" → Builder pre-filled → save as version → Hub version selector shows new version
- [ ] **Research journey**: Strategy Hub → Research tab → `[Monte Carlo]` button → Monte Carlo page opens with `resultId` pre-filled — no re-selection needed
- [ ] **Prop firm journey**: Strategy Hub → Prop Firm tab → select run from dropdown (pre-filtered to this strategy's runs) → select firm → Evaluate → results appear inline — no navigation away
- [ ] **Dashboard**: Strategy strip shows all strategies with Sharpe and status; clicking any card navigates to that strategy hub
- [ ] **Sub-runs not in Runs tab**: After running a Monte Carlo study, the simulation sub-runs do NOT appear in the Strategy Hub Runs tab — only the parent standard backtest run appears
- [ ] **Version selector**: Switching versions in the hub header updates the left panel parameters, the Runs tab, the Research tab, and the Prop Firm tab — all scoped to the newly selected version
- [ ] **Dirty params warning**: Editing left panel parameters and clicking `[Run]` without saving first shows either a warning or auto-saves before launching

