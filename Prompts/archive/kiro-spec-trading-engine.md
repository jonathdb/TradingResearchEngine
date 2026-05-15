# TradingResearchEngine — AWS Kiro Implementation Spec

**Repository:** `jonathdb/TradingResearchEngine`
**Branch:** `Development`
**Stack:** .NET 8 / Blazor Server / MudBlazor / Skender.Stock.Indicators / SQLite / Google Gemini AI

This spec is structured as a series of self-contained **Stories** with explicit acceptance criteria, file paths, and implementation guidance so Kiro can execute them independently or as a batch. Stories are ordered by priority (P0 → P3).

---

## CONTEXT & ARCHITECTURE PRIMER

Before implementing any story, understand the following invariants:

- **Layer boundaries**: `Core` has zero dependencies on `Application` or `Web`. `Application` references `Core`. `Web` references both. Never break this.
- **`IStrategy` contract** lives in `TradingResearchEngine.Core` and returns `IReadOnlyList<EngineEvent>` per bar. Strategies must be **stateful but isolable** — each parallel worker needs its own instance.
- **Skender adapter pattern**: Application-layer indicators extend `SkenderIndicatorAdapter<TResult>` (file: `src/TradingResearchEngine.Application/Indicators/SkenderIndicatorAdapter.cs`). This is the only approved way to wrap Skender indicators.
- **`IndicatorRegistry`** in Core (`src/TradingResearchEngine.Core/Indicators/IndicatorRegistry.cs`) holds static metadata. Application-layer adapters are the runtime implementations.
- **Persistence**: All repositories use JSON files. `SqliteIndexRepository<T>` provides O(log n) lookup over JSON. Do not switch to a full ORM.
- **UI framework**: MudBlazor throughout. Use MudBlazor components exclusively — do not introduce Radzen, Syncfusion, or other UI libraries.
- **Testing**: Add or update unit tests in `TradingResearchEngine.UnitTests` for every Core/Application change.

---

## P0 — CRITICAL BUGS

---

### Story P0-1: Move Backtest Execution Off the Blazor SignalR Thread

**Problem:** `StrategyBuilder.razor` awaits `RunUseCase.RunAsync(...)` synchronously on the Blazor circuit, blocking all UI updates for the duration of the backtest.

**Files to modify:**
- `src/TradingResearchEngine.Web/Components/Pages/StrategyBuilder.razor`
- `src/TradingResearchEngine.Application/Engine/BacktestJob.cs` (existing)
- `src/TradingResearchEngine.Application/Engine/JobExecutor.cs` (existing)
- `src/TradingResearchEngine.Web/Components/Pages/Backtests/BacktestHistory.razor` (may need update for job status display)

**Implementation steps:**

1. In `StrategyBuilder.razor`, replace every direct `await RunUseCase.RunAsync(...)` call with:
   ```csharp
   var job = new BacktestJob { Config = config, SubmittedAt = DateTimeOffset.UtcNow };
   var jobId = await JobExecutor.EnqueueAsync(job, _cts.Token);
   Nav.NavigateTo($"/backtests/job/{jobId}");
   ```
2. Create `src/TradingResearchEngine.Web/Components/Pages/Backtests/JobStatusPage.razor` at route `/backtests/job/{JobId}`:
   - Poll `JobExecutor.GetStatusAsync(JobId)` every 2 seconds using a `PeriodicTimer`.
   - Show a `MudProgressLinear` with percentage if available, otherwise indeterminate.
   - Show "Queued", "Running", "Completed", or "Failed" as a `MudChip` with appropriate colors.
   - On completion, auto-navigate to `/backtests/{resultId}` after a 1-second delay.
   - On failure, show the error message in a `MudAlert Severity="Severity.Error"` with an "Edit & Retry" button linking back to the builder.
3. Remove all inline `await RunUseCase.RunAsync(...)` calls from the builder. The builder's responsibility ends at job submission.

**Acceptance criteria:**
- [ ] Clicking "Launch Backtest" in the builder submits a job and immediately navigates to the status page — the builder page is no longer blocked.
- [ ] The status page shows live progress updates during execution.
- [ ] On completion, user is automatically redirected to the backtest result page.
- [ ] On failure, an actionable error message is shown with a retry path.
- [ ] All existing builder tests still pass.

---

### Story P0-2: Add `IStrategyFactory` to Enforce Parallel Isolation

**Problem:** `Parallel.ForEachAsync` in walk-forward and parameter sweep workflows shares strategy state across threads because there is no factory contract to mandate per-worker instantiation.

**Files to create/modify:**
- `src/TradingResearchEngine.Core/Strategies/IStrategyFactory.cs` *(new)*
- All concrete strategy implementations (scan for `IStrategy` implementors)
- `src/TradingResearchEngine.Application/Research/WalkForwardWorkflow.cs`
- `src/TradingResearchEngine.Application/Research/ParameterSweepWorkflow.cs`
- `src/TradingResearchEngine.Application/ServiceCollectionExtensions.cs`

**Implementation steps:**

1. Create the factory interface in Core:
   ```csharp
   // src/TradingResearchEngine.Core/Strategies/IStrategyFactory.cs
   namespace TradingResearchEngine.Core.Strategies;

   /// <summary>
   /// Creates isolated IStrategy instances for use in parallel workflows.
   /// Each call to Create() MUST return a new, independent instance with its own state.
   /// </summary>
   public interface IStrategyFactory
   {
       string StrategyType { get; }
       IStrategy Create(StrategyConfig config);
   }
   ```
2. For every concrete strategy class that implements `IStrategy`, add a corresponding nested or sibling `Factory` class implementing `IStrategyFactory`.
3. Register all factories in `ServiceCollectionExtensions.cs` as `IStrategyFactory` with a keyed service registration using `StrategyType` as the key, or collect them in a `IEnumerable<IStrategyFactory>`.
4. In `WalkForwardWorkflow.cs` and `ParameterSweepWorkflow.cs`, replace any pattern that reuses or casts a single `IStrategy` instance across parallel iterations. Each `Parallel.ForEachAsync` iteration must call `factory.Create(config)` to obtain a fresh instance.
5. Add a unit test that runs the same strategy factory concurrently 20 times and asserts that each instance produces independent results (no shared mutable state).

**Acceptance criteria:**
- [ ] `IStrategyFactory` is defined in Core with no Application or Web references.
- [ ] Every `IStrategy` implementor has a corresponding factory.
- [ ] Parallel sweep and walk-forward workflows use `factory.Create()` per iteration.
- [ ] Concurrency unit test passes without data races.

---

### Story P0-3: Fix Short Direction Fill Logic

**Problem:** `TryFillLimit`, `TryFillStopMarket`, and `TryFillStopLimit` in the execution engine silently drop `Direction.Short` orders (return `null` without filling), making short entries via limit/stop orders impossible despite `AllowReversals` being enabled.

**Files to modify:**
- `src/TradingResearchEngine.Core/Execution/` — locate the fill methods (`TryFillLimit`, `TryFillStopMarket`, `TryFillStopLimit`).

**Implementation steps:**

For `TryFillLimit`:
```
Direction.Short limit: fill when bar.High >= limitPrice
  (short sell limit is entered above market; fills when price rises to it)
```
For `TryFillStopMarket`:
```
Direction.Short stop: fill when bar.Low <= stopPrice
  (short sell stop is entered below market; triggers when price falls to it)
```
For `TryFillStopLimit`:
```
Direction.Short stop-limit: trigger when bar.Low <= stopPrice, then fill at limitPrice if bar.High >= limitPrice
```

1. Add `Direction.Short` branches to each fill method following the same pattern as the existing `Direction.Long` branches.
2. Apply bid/ask-aware pricing for short fills (sell at bid, not mid).
3. Add unit tests for each fill type with `Direction.Short` covering: fill conditions met, fill conditions not met, and boundary (price exactly at trigger level).

**Acceptance criteria:**
- [ ] Short limit orders fill when price rises to the limit level.
- [ ] Short stop-market orders trigger when price falls to the stop level.
- [ ] Short stop-limit orders trigger and fill correctly.
- [ ] All three have dedicated unit tests that pass.
- [ ] No regression on existing long-side fill tests.

---

## P0 — ENGINE CORRECTNESS

---

### Story P0-4: Fix Sortino Ratio Downside Deviation Formula

**Problem:** `MetricsCalculator.ComputeSortinoRatio` filters to only realized losing periods before computing the standard deviation, which produces incorrect results and can return `null` when there are no down bars.

**File:** `src/TradingResearchEngine.Core/Metrics/MetricsCalculator.cs`

**Correct formula:**
```
downsideDev = sqrt( mean( min(r - threshold, 0)^2  for all r ) )
```
This uses ALL returns but treats upside deviations as zero.

**Implementation:**
```csharp
public static decimal? ComputeSortinoRatio(
    IReadOnlyList<EquityCurvePoint> curve, decimal annualRiskFreeRate, int barsPerYear)
{
    if (curve.Count < 2) return null;
    var returns = GetPeriodReturns(curve);
    if (returns.Count == 0) return null;

    decimal periodRiskFree = annualRiskFreeRate / barsPerYear;
    decimal meanReturn = returns.Average();

    // Downside deviation: uses all returns, zeros out upside
    decimal sumSquared = returns.Sum(r =>
    {
        decimal diff = Math.Min(r - periodRiskFree, 0m);
        return diff * diff;
    });
    decimal downsideDev = (decimal)Math.Sqrt((double)(sumSquared / returns.Count));
    if (downsideDev == 0m) return null;

    return (meanReturn - periodRiskFree) / downsideDev * (decimal)Math.Sqrt(barsPerYear);
}
```

**Acceptance criteria:**
- [ ] Formula matches the standard Sortino definition (downside deviation over ALL periods).
- [ ] Method no longer returns `null` for strategies with no losing bars — it returns `null` only when `downsideDev == 0`.
- [ ] Unit test: verify correct value for a known synthetic return series with a mix of winning and losing periods.
- [ ] Unit test: strategy with all positive returns returns non-null (not null as before).

---

### Story P0-5: Thread `BarsPerYear` into Calmar Ratio Annualization

**Problem:** `ComputeCalmarRatio` hardcodes `252` for annualization, making it wrong for intraday timeframes.

**File:** `src/TradingResearchEngine.Core/Metrics/MetricsCalculator.cs`

**Change:** Add `int barsPerYear` parameter to `ComputeCalmarRatio` and `ComputeReturnOnMaxDrawdown`. Replace the `252m / (decimal)days` approximation with `(decimal)barsPerYear / (decimal)(curve.Count - 1)` (return-per-bar × bars-per-year), which is consistent with how Sharpe is annualized.

```csharp
public static decimal? ComputeCalmarRatio(
    IReadOnlyList<EquityCurvePoint> curve, decimal startEquity, decimal endEquity, int barsPerYear)
{
    if (curve.Count < 2 || startEquity == 0m) return null;
    decimal maxDd = ComputeMaxDrawdown(curve);
    if (maxDd == 0m) return null;

    int n = curve.Count - 1;
    if (n <= 0) return null;

    var returns = GetPeriodReturns(curve);
    if (returns.Count == 0) return null;
    decimal meanReturn = returns.Average();
    decimal annualizedReturn = meanReturn * barsPerYear;
    return annualizedReturn / maxDd;
}
```

Update all call sites to pass `config.EffectiveBarsPerYear` (or `BarsPerYearDefaults` lookup).

**Acceptance criteria:**
- [ ] `ComputeCalmarRatio` signature includes `int barsPerYear`.
- [ ] All call sites pass the correct `barsPerYear` value from `ScenarioConfig`.
- [ ] Unit test: M1 config (barsPerYear=131040) produces a different (correct) Calmar than a D1 config (barsPerYear=252) for the same equity curve.

---

### Story P0-6: Fix `ComputeHistoricalVaR` Small-Sample Boundary

**Problem:** With fewer than ~20 samples, the VaR index calculation always returns the worst single return regardless of confidence level, producing misleading results.

**File:** `src/TradingResearchEngine.Core/Metrics/MetricsCalculator.cs`

**Change:**
```csharp
public static decimal? ComputeHistoricalVaR(IReadOnlyList<EquityCurvePoint> curve, decimal confidence)
{
    if (curve.Count < 2) return null;
    var returns = GetPeriodReturns(curve).OrderBy(r => r).ToList();
    if (returns.Count < 30) return null; // insufficient sample for meaningful VaR
    int idx = (int)Math.Floor((1 - confidence) * returns.Count);
    return -returns[Math.Max(0, idx)];
}
```
Apply the same minimum-sample guard to `ComputeHistoricalCVaR`.

**Acceptance criteria:**
- [ ] Both VaR and CVaR return `null` when sample count < 30.
- [ ] Unit test: assert null is returned for 15-bar equity curve at 95% confidence.
- [ ] Unit test: assert non-null and correct value for 100-bar curve.

---

## P1 — ARCHITECTURE & FEEDBACK LOOPS

---

### Story P1-1: Add `Reset()` and `Initialize()` Lifecycle to `IStrategy`

**Problem:** Walk-forward must destroy and reconstruct strategy objects between windows because there are no lifecycle hooks.

**Files:**
- `src/TradingResearchEngine.Core/Strategies/IStrategy.cs` *(modify)*
- All concrete `IStrategy` implementations *(add Reset/Initialize)*
- `src/TradingResearchEngine.Application/Research/WalkForwardWorkflow.cs` *(use Reset)*

**Changes:**
```csharp
public interface IStrategy
{
    string StrategyType { get; }
    
    /// <summary>Called once before the first bar of a new execution window.</summary>
    void Initialize(StrategyConfig config);

    /// <summary>
    /// Resets all indicator state. Called between walk-forward windows to
    /// reuse the same instance without reconstruction overhead.
    /// </summary>
    void Reset();

    IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt);
}
```

- Each concrete strategy's `Reset()` must call `Reset()` on all its indicator instances (SMA, EMA, etc.) and clear any internal state (position tracking, open order refs, etc.).
- `Initialize(config)` sets parameters from config — move parameter extraction from constructors to `Initialize` where practical.
- Walk-forward workflow calls `strategy.Reset()` before each out-of-sample window instead of creating a new instance.

**Acceptance criteria:**
- [ ] `IStrategy` has `Initialize` and `Reset` methods.
- [ ] All concrete strategies implement both.
- [ ] Walk-forward workflow uses `Reset()` between windows.
- [ ] Unit test: run a strategy for 50 bars, call `Reset()`, run again for 50 bars — results are identical to a fresh instance.

---

### Story P1-2: Refactor `StudyDetail.razor` to Use a Pluggable Renderer Pattern

**Problem:** The 7-case `switch` in `StudyDetail.razor` violates OCP and requires modification for every new study type.

**Files:**
- `src/TradingResearchEngine.Web/Components/Pages/Research/StudyDetail.razor` *(modify)*
- `src/TradingResearchEngine.Web/Components/Studies/` *(new directory)*
  - `IStudyResultRenderer.cs` *(new)*
  - `MonteCarloResultRenderer.razor` *(new)*
  - `WalkForwardResultRenderer.razor` *(new)*
  - `SweepResultRenderer.razor` *(new)*
  - `RealismResultRenderer.razor` *(new)*
  - `BenchmarkResultRenderer.razor` *(new)*
  - `CpcvResultRenderer.razor` *(new — includes histogram chart, see Story P2-1)*
  - `VarianceResultRenderer.razor` *(new)*
  - `StudyRendererRegistry.cs` *(new)*

**Implementation:**
```csharp
// IStudyResultRenderer.cs — Blazor component interface contract
// Each renderer receives: StudyRecord Study, string ResultJson
// and renders its own content.

// StudyRendererRegistry.cs
public static class StudyRendererRegistry
{
    private static readonly Dictionary<StudyType, Type> _map = new()
    {
        [StudyType.MonteCarlo] = typeof(MonteCarloResultRenderer),
        [StudyType.WalkForward] = typeof(WalkForwardResultRenderer),
        // ... etc
    };

    public static Type? GetRenderer(StudyType type)
        => _map.GetValueOrDefault(type);
}
```

In `StudyDetail.razor`, replace the switch block with:
```razor
@{
    var rendererType = StudyRendererRegistry.GetRenderer(_study.Type);
}
@if (rendererType is not null)
{
    <DynamicComponent Type="rendererType"
        Parameters="@(new Dictionary<string, object> { ["Study"] = _study, ["ResultJson"] = _study.ResultJson! })" />
}
```

**Acceptance criteria:**
- [ ] `StudyDetail.razor` contains no `switch` on `StudyType` for result rendering.
- [ ] Each study type has its own renderer component.
- [ ] Adding a new study type requires only: (1) a new renderer component and (2) a registry entry — no changes to `StudyDetail.razor`.
- [ ] All existing study result displays are visually identical to before.

---

### Story P1-3: Paginate Dashboard "Recent Runs" — No Full Load

**Problem:** `Dashboard.razor` loads all backtest results into memory on every page visit.

**Files:**
- `src/TradingResearchEngine.Core/Persistence/IRepository.cs` (or wherever `IRepository<T>` is defined)
- `src/TradingResearchEngine.Infrastructure/Persistence/SqliteIndexRepository.cs` *(add paged method)*
- `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor` *(modify)*

**Changes:**

1. Add to the repository interface:
   ```csharp
   Task<IReadOnlyList<T>> ListRecentAsync(int count, CancellationToken ct = default);
   ```
2. Implement in `SqliteIndexRepository<T>`: use `ORDER BY RunId DESC LIMIT @count` via the SQLite index.
3. In `Dashboard.razor`, replace:
   ```csharp
   _runs = (await ResultRepo.ListAsync(_cts.Token)).OrderByDescending(r => r.RunId).ToList();
   _recentRuns = _runs.Take(10).ToList();
   ```
   With:
   ```csharp
   _recentRuns = (await ResultRepo.ListRecentAsync(10, _cts.Token)).ToList();
   _latestRun = _recentRuns.FirstOrDefault();
   ```
4. For the robustness warnings loop, load only completed runs via a second `ListRecentAsync(20)` call — do not hold all runs in `_runs`.

**Acceptance criteria:**
- [ ] Dashboard page load issues at most 2 repository queries, neither loading all results.
- [ ] `ListRecentAsync` is implemented with a DB-level LIMIT.
- [ ] Robustness flags still show correctly with the paginated approach.

---

### Story P1-4: Wire Live Study Progress to UI via SignalR / Blazor Events

**Problem:** `BackgroundStudyService` fires progress events but no UI page subscribes to them, so there is no live feedback during long-running studies.

**Files:**
- `src/TradingResearchEngine.Web/Components/Pages/Research/StudyDetail.razor` *(modify)*
- `src/TradingResearchEngine.Web/Components/Pages/Backtests/JobStatusPage.razor` *(created in P0-1)*
- `src/TradingResearchEngine.Application/Research/BackgroundStudyService.cs` *(verify events are accessible)*

**Implementation:**

1. In `StudyDetail.razor`, during `OnInitializedAsync`, if the study status is `Running`, subscribe to `BackgroundStudyService.OnStudyProgress`:
   ```csharp
   _studyService.OnStudyProgress += HandleProgress;
   _studyService.OnStudyCompleted += HandleCompleted;
   ```
2. `HandleProgress(studyId, completed, total)`: if `studyId == StudyId`, update `_progressCompleted` and `_progressTotal`, call `await InvokeAsync(StateHasChanged)`.
3. `HandleCompleted(studyId)`: reload the study record and re-render results.
4. Show a `MudProgressLinear` with `Value="@(_progressCompleted * 100.0 / _progressTotal)"` when status is Running.
5. Implement `IDisposable` on the page and unsubscribe from events in `Dispose()`.
6. Apply the same pattern to `JobStatusPage.razor` for backtest job progress.

**Acceptance criteria:**
- [ ] A running Monte Carlo study shows a live progress bar (X of N simulations).
- [ ] A running parameter sweep shows progress across all parameter combinations.
- [ ] Progress bar disappears and results render automatically when study completes.
- [ ] No memory leaks — event handlers are unsubscribed on page disposal.

---

## P2 — ROBUSTNESS & RESEARCH DEPTH

---

### Story P2-1: CPCV Result Visualization (Histogram + Percentile Table)

**Problem:** `CpcvResultRenderer` (from P1-2) currently only shows 3 KPI cards. CPCV deserves a distribution chart of OOS path Sharpe ratios.

**Files:**
- `src/TradingResearchEngine.Web/Components/Studies/CpcvResultRenderer.razor` *(new, from P1-2)*
- `src/TradingResearchEngine.Web/Components/Charts/CpcvDistributionChart.razor` *(new)*

**Implementation:**

1. In `CpcvDistributionChart.razor`, use Plotly.Blazor to render a histogram of `CpcvResult.PathSharpeRatios` (IReadOnlyList<decimal>).
   - X-axis: OOS Sharpe ratio values.
   - Y-axis: frequency count.
   - Add a vertical dashed line at the median and at 0 (zero-line marker).
   - Color bars red where Sharpe < 0, green where ≥ 1, yellow otherwise.
2. Below the histogram, add a `MudTable` showing: P10, P25, P50 (Median), P75, P90 Sharpe percentile values computed from `PathSharpeRatios`.
3. If `CpcvResult` does not currently carry `PathSharpeRatios`, add the field:
   ```csharp
   public IReadOnlyList<decimal> PathSharpeRatios { get; init; } = Array.Empty<decimal>();
   ```
   And populate it in the CPCV workflow.

**Acceptance criteria:**
- [ ] CPCV result page shows a Plotly histogram of all OOS path Sharpe ratios.
- [ ] Percentile table shows P10–P90 values.
- [ ] `CpcvResult` carries `PathSharpeRatios`.
- [ ] Chart renders correctly for a result with 10+ paths.

---

### Story P2-2: Parameter Sweep Heatmap Metric Selector

**Problem:** `ParameterSweepHeatmap` hardcodes `MetricName="Sharpe"`. Users need to switch between Sharpe, MaxDD, WinRate, ProfitFactor.

**Files:**
- `src/TradingResearchEngine.Web/Components/Charts/ParameterSweepHeatmap.razor` *(modify)*
- `src/TradingResearchEngine.Web/Components/Pages/Research/StudyDetail.razor` / `SweepResultRenderer.razor` *(modify to pass context)*

**Changes:**

1. Add a `SelectedMetric` parameter and a dropdown above the heatmap:
   ```razor
   <MudSelect T="string" @bind-Value="_selectedMetric" Label="Metric" Dense="true" Class="mb-2" Style="max-width:200px">
       <MudSelectItem Value="@("Sharpe")">Sharpe Ratio</MudSelectItem>
       <MudSelectItem Value="@("MaxDrawdown")">Max Drawdown</MudSelectItem>
       <MudSelectItem Value="@("WinRate")">Win Rate</MudSelectItem>
       <MudSelectItem Value="@("ProfitFactor")">Profit Factor</MudSelectItem>
       <MudSelectItem Value="@("TotalTrades")">Trade Count</MudSelectItem>
   </MudSelect>
   ```
2. Update the heatmap `z` values data series to use the selected metric extracted from `SweepResult.Cells`.
3. Each `SweepCell` in `SweepResult` must carry the metric values. If it only carries `SharpeRatio` today, add: `MaxDrawdown`, `WinRate`, `ProfitFactor`, `TotalTrades` to the cell record and populate them in the sweep workflow.
4. The color scale direction should flip for `MaxDrawdown` (lower = better = green).

**Acceptance criteria:**
- [ ] Heatmap has a metric selector dropdown.
- [ ] Selecting "Max Drawdown" re-renders the heatmap with DD values, inverted color scale.
- [ ] All 5 metrics render correctly.
- [ ] No page reload required — metric switch is fully client-side reactive.

---

### Story P2-3: Add 1-Bar Entry Delay Perturbation to Sensitivity Analysis

**Problem:** The Realism sensitivity workflow perturbs slippage and commissions but not entry fill delay, which is the most common source of real-world strategy degradation.

**Files:**
- `src/TradingResearchEngine.Application/Research/SensitivityWorkflow.cs` (or equivalent)
- `src/TradingResearchEngine.Core/Config/ExecutionConfig.cs` — verify `FillDelayBars` exists or add it
- `src/TradingResearchEngine.Application/Research/Results/` — update sensitivity result model if needed

**Changes:**

1. If `ExecutionConfig` does not have `FillDelayBars`, add: `public int FillDelayBars { get; init; } = 0;`
2. In the execution engine's bar processing, when `FillDelayBars > 0`, defer order submission by N bars (hold in a delay queue, emit to pending-order queue only after N bars pass).
3. In `SensitivityWorkflow`, add a new perturbation dimension: `FillDelayBars` values `[0, 1, 2]` as a standard sensitivity axis.
4. Label these variants in results as "Delay 0 bars", "Delay 1 bar", "Delay 2 bars".
5. Expose `FillDelayBars` as a configurable parameter in `AdvancedOverridesPanel.razor`.

**Acceptance criteria:**
- [ ] Sensitivity study includes 3 fill-delay variants (0, 1, 2 bars).
- [ ] A strategy that relies on bar-open fills shows measurable degradation at 1-bar delay.
- [ ] `FillDelayBars` is configurable in the Advanced Overrides panel.
- [ ] Unit test: fill delay of 1 bar results in orders executing 1 bar later than without delay.

---

### Story P2-4: Surface Checklist Score on Dashboard Strategy Cards

**Problem:** The 9-item `ResearchChecklistService` confidence score exists but is not visible on the Dashboard strategy strip.

**Files:**
- `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor` *(modify)*
- `src/TradingResearchEngine.Application/Research/ResearchChecklistService.cs` *(inject)*

**Changes:**

1. In `Dashboard.razor`, inject `ResearchChecklistService`.
2. During `OnInitializedAsync`, for each strategy's latest run, call `checklistService.EvaluateAsync(latestRun)` to get a `ChecklistResult` with `ConfidenceLevel` and `PassedCount`.
3. Display on each strategy strip card below the Sharpe:
   ```razor
   <MudText Typo="Typo.caption" Class="@GetChecklistClass(checklistResult)">
       ✓ @checklistResult.PassedCount/9 checks
   </MudText>
   ```
   Where `GetChecklistClass` returns green/yellow/red CSS class based on passed count (≥7 = green, 5-6 = yellow, <5 = red).
4. Add a `MudTooltip` on the checklist badge showing the list of failed checks by name.

**Acceptance criteria:**
- [ ] Each strategy card on the dashboard shows "X/9 checks" badge.
- [ ] Badge color reflects confidence level.
- [ ] Tooltip shows which specific checks failed.
- [ ] Strategies with no runs show "—" for the checklist badge.

---

## P2 — STRATEGY CREATION

---

### Story P2-5: AI Strategy Builder — Streaming Response with Token-by-Token Display

**Problem:** `IAIStrategyAssistant` returns a complete response in one shot with no streaming feedback. Users see a blank loading state for 5–15 seconds.

**Files:**
- `src/TradingResearchEngine.Application/AI/IAIStrategyAssistant.cs` *(modify)*
- `src/TradingResearchEngine.Infrastructure/AI/GeminiStrategyAssistant.cs` *(modify)*
- `src/TradingResearchEngine.Web/Components/Pages/StrategyBuilder.razor` (or the AI sub-page) *(modify)*

**Changes:**

1. Add a streaming method to the interface:
   ```csharp
   public interface IAIStrategyAssistant
   {
       Task<AIStrategyDraft> GenerateAsync(string prompt, CancellationToken ct = default);
       
       /// <summary>Streams the raw text response token by token.</summary>
       IAsyncEnumerable<string> StreamGenerateAsync(string prompt, CancellationToken ct = default);
       
       Task<AIStrategyDraft> RefineAsync(AIStrategyDraft current, string feedback, CancellationToken ct = default);
       IAsyncEnumerable<string> StreamRefineAsync(AIStrategyDraft current, string feedback, CancellationToken ct = default);
   }
   ```
2. Implement `StreamGenerateAsync` in `GeminiStrategyAssistant` using `Mscc.GenerativeAI`'s streaming API (`GenerateContentStreamAsync` or equivalent).
3. In the AI builder UI page:
   - Maintain a `_streamBuffer` string.
   - Consume the `IAsyncEnumerable<string>` in a background task.
   - On each token chunk: append to buffer, call `await InvokeAsync(StateHasChanged)`.
   - Show the buffer in a `MudPaper` with a monospace font and a blinking cursor CSS animation.
   - Once the stream completes, parse the full buffer as `AIStrategyDraft` JSON and populate the builder form fields.
4. Show a "Stop generation" button during streaming that cancels the `CancellationToken`.

**Acceptance criteria:**
- [ ] AI response text appears word by word with a typing animation.
- [ ] "Stop generation" button cancels the stream mid-flight.
- [ ] After stream completion, form fields are auto-populated from the parsed draft.
- [ ] No regression on non-streaming `GenerateAsync` path.

---

### Story P2-6: AI Strategy Builder — Iterative Refinement Loop

**Problem:** Users cannot refine an AI-generated strategy without starting from scratch.

**Files:**
- `src/TradingResearchEngine.Application/AI/IAIStrategyAssistant.cs` *(method added in P2-5)*
- `src/TradingResearchEngine.Application/AI/AIStrategyDraft.cs` *(ensure it is serializable as context)*
- `src/TradingResearchEngine.Web/Components/Pages/StrategyBuilder.razor` or AI sub-component *(modify)*

**Changes:**

1. After a draft is generated and displayed, show a "Refine this strategy" expandable section:
   ```razor
   <MudExpansionPanel Text="Refine with AI feedback">
       <MudTextField @bind-Value="_refinementPrompt" 
                     Label="What would you like to change?"
                     Placeholder="e.g. Make the stop loss tighter, use RSI instead of SMA, add a volume filter..."
                     Lines="3" />
       <MudButton OnClick="RefineAsync" Disabled="_isGenerating">Apply Refinement</MudButton>
   </MudExpansionPanel>
   ```
2. `RefineAsync()` calls `StreamRefineAsync(currentDraft, _refinementPrompt)` passing the current full draft as context.
3. Show a refinement history panel listing: original prompt → refinement 1 → refinement 2, etc. Allow the user to revert to any previous version by clicking it.
4. Store refinement history in `AIStrategyDraft.RefinementHistory` (new property: `IReadOnlyList<string> RefinementHistory`).

**Acceptance criteria:**
- [ ] A "Refine" text box appears after draft generation.
- [ ] Submitting a refinement calls the AI with the current draft as context.
- [ ] Refinement history shows all previous prompts.
- [ ] User can revert to a previous draft version.
- [ ] Refinement uses streaming (from P2-5).

---

### Story P2-7: Add Result-Aware Dynamic Interpretations to Study Detail

**Problem:** `GetInterpretation()` in `StudyDetail.razor` returns the same static text regardless of the actual results.

**Files:**
- `src/TradingResearchEngine.Application/Research/StudyInterpretationService.cs` *(new)*
- `src/TradingResearchEngine.Web/Components/Pages/Research/StudyDetail.razor` *(or renderer components)*

**Implementation:**

Create `StudyInterpretationService` with a method per study type:
```csharp
public string InterpretMonteCarlo(MonteCarloResult result)
{
    var sb = new StringBuilder();
    sb.Append($"Median end equity is ${result.P50EndEquity:F0}. ");
    if (result.RuinProbability > 0.05m)
        sb.Append($"⚠ Ruin probability of {result.RuinProbability:P1} is elevated — consider tightening risk parameters. ");
    else
        sb.Append($"Ruin probability is low at {result.RuinProbability:P1}. ");
    if (result.P10EndEquity < result.StartEquity * 0.8m) // example: StartEquity needs to be on result
        sb.Append("The P10 (worst-case) scenario shows a significant loss — strategy may be fragile. ");
    return sb.ToString();
}
```

Add similar methods for `WalkForward`, `CPCV`, `ParameterSweep`, `Realism`, `BenchmarkComparison`.

Key interpretation triggers (implement all):
- **Monte Carlo**: Ruin prob > 5% → warn. P10 equity < 80% of start → warn. P90/P10 spread > 3× → warn high variance.
- **Walk-Forward**: OOS Sharpe < 50% of IS Sharpe → warn degradation. Param drift score high → warn instability.
- **CPCV**: P(overfit) > 50% → strong warn. Median OOS Sharpe < 0 → critical warn.
- **ParameterSweep**: Fewer than 20% of parameter cells with positive Sharpe → warn fragile peak.
- **Realism**: Worst Sharpe < 0 → warn. Std dev Sharpe > 0.5 → warn sensitivity.
- **BenchmarkComparison**: Negative alpha → warn. Beta > 1.5 → warn high market exposure.

**Acceptance criteria:**
- [ ] Each study type has a result-aware interpretation (not static text).
- [ ] Interpretations include specific values from actual results.
- [ ] Warning phrases appear when quantitative thresholds are breached.
- [ ] `StudyInterpretationService` is unit-testable and injected — not inline in the Razor component.

---

## P3 — UX IMPROVEMENTS

---

### Story P3-1: Builder Step Persistence — Resume Mid-Wizard on Reload

**Problem:** Refreshing the browser during the 5-step wizard resets to Step 1 even though the draft was saved.

**Files:**
- `src/TradingResearchEngine.Application/Configuration/ConfigDraft.cs` *(modify)*
- `src/TradingResearchEngine.Web/Components/Builder/BuilderViewModel.cs` *(modify)*
- `src/TradingResearchEngine.Web/Components/Pages/StrategyBuilder.razor` *(modify)*

**Changes:**

1. Add to `ConfigDraft`:
   ```csharp
   public int CurrentStep { get; init; } = 1;
   public int MaxVisitedStep { get; init; } = 1;
   ```
2. In `BuilderViewModel`, whenever `CurrentStep` changes, call a debounced save to persist the draft with the new step.
3. In `BuilderViewModel.FromDraft(draft)`, restore `CurrentStep` and `MaxVisitedStep`.
4. On `StrategyBuilder.razor` initialization, load the existing draft via `DraftId` URL parameter and call `BuilderViewModel.FromDraft(draft)` — the wizard will open at the correct step.

**Acceptance criteria:**
- [ ] Refreshing mid-wizard restores the correct step.
- [ ] `MaxVisitedStep` prevents skipping forward to unvisited steps after resume.
- [ ] Draft auto-saves step changes (debounced 500ms).

---

### Story P3-2: Robustness Flag Tooltips with Plain-English Explanations

**Problem:** Warning chips like "K-Ratio < 0" are opaque to users not familiar with quant metrics.

**Files:**
- `src/TradingResearchEngine.Application/Research/IRobustnessAdvisoryService.cs` (or wherever warning labels are defined)
- `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor` *(modify)*
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyDetail.razor` *(modify, if it also shows flags)*

**Changes:**

1. Create a `RobustnessWarningCatalog` static class:
   ```csharp
   public static class RobustnessWarningCatalog
   {
       public static readonly Dictionary<string, string> Explanations = new()
       {
           ["High Sharpe"] = "A Sharpe ratio above 3 on historical data is statistically rare. This may indicate overfitting — the strategy may have been tuned to look good on this specific dataset.",
           ["Low Trades"] = "Fewer than 30 trades gives insufficient statistical power. Sharpe and win rate estimates are unreliable with a small sample.",
           ["K-Ratio < 0"] = "A negative K-Ratio means the equity curve is declining on a linear basis — the strategy is losing money consistently, not just in a drawdown.",
           ["High Max DD"] = "The maximum drawdown exceeds 30%. This level of peak-to-trough decline would be psychologically and practically difficult to sustain in live trading.",
           // Add entries for all warnings emitted by IRobustnessAdvisoryService
       };
   }
   ```
2. Wrap each `MudChip` warning in a `MudTooltip`:
   ```razor
   <MudTooltip Text="@RobustnessWarningCatalog.Explanations.GetValueOrDefault(w, w)">
       <MudChip T="string" Size="MudBlazor.Size.Small" Color="Color.Warning">@w</MudChip>
   </MudTooltip>
   ```

**Acceptance criteria:**
- [ ] Hovering any warning chip shows a tooltip with a plain-English explanation.
- [ ] All warning types emitted by `IRobustnessAdvisoryService` have a catalog entry.
- [ ] Fallback: if a warning has no catalog entry, show the raw label as tooltip (no null reference).

---

### Story P3-3: Recent Runs Table — Sortable Columns and Strategy Filter

**Problem:** The Dashboard "Recent Runs" table has no sorting or filtering controls.

**Files:**
- `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor` *(modify)*

**Changes:**

1. Add `SortLabel` to each `MudTh` in the recent runs table:
   ```razor
   <MudTh><MudTableSortLabel SortBy="@(new Func<BacktestResult, object>(r => r.SharpeRatio ?? -999m))">Sharpe</MudTableSortLabel></MudTh>
   <MudTh><MudTableSortLabel SortBy="@(new Func<BacktestResult, object>(r => r.MaxDrawdown))">Max DD</MudTableSortLabel></MudTh>
   <MudTh><MudTableSortLabel SortBy="@(new Func<BacktestResult, object>(r => r.TotalTrades))">Trades</MudTableSortLabel></MudTh>
   ```
2. Add a strategy filter chip group above the table:
   ```razor
   <MudChipSet @bind-SelectedValues="_selectedStrategyFilters" MultiSelection="true" Class="mb-2">
       @foreach (var strategy in _strategies)
       {
           <MudChip T="string" Value="@strategy.StrategyType">@strategy.StrategyName</MudChip>
       }
   </MudChipSet>
   ```
3. Filter `_recentRuns` by `_selectedStrategyFilters` when any are selected.
4. Add "Show failed runs" toggle `MudSwitch` that includes runs with `BacktestStatus.Failed`.

**Acceptance criteria:**
- [ ] Sharpe, MaxDD, and Trades columns are sortable (ascending/descending on click).
- [ ] Strategy chips filter the displayed runs reactively.
- [ ] "Show failed runs" toggle works correctly.
- [ ] All changes are client-side reactive (no additional API calls).

---

### Story P3-4: First-Run Empty State for Strategy Library

**Problem:** An empty strategy library shows a blank list with no guidance.

**Files:**
- `src/TradingResearchEngine.Web/Components/Pages/Strategies/StrategyLibrary.razor` *(modify)*

**Changes:**

Replace the empty list case with a structured empty state:
```razor
@if (_strategies.Count == 0)
{
    <MudPaper Class="pa-8 text-center" Elevation="0" Style="border: 2px dashed var(--mud-palette-divider); border-radius: 12px;">
        <MudIcon Icon="@Icons.Material.Filled.Science" Style="font-size:4rem; opacity:0.3;" Class="mb-4" />
        <MudText Typo="Typo.h5" Class="mb-2">No strategies yet</MudText>
        <MudText Typo="Typo.body1" Class="text-muted mb-4" Style="max-width:460px; margin:0 auto;">
            A strategy moves through stages: <strong>Hypothesis → Exploring → Optimizing → Validating → FinalTest</strong>.
            Start by creating your first strategy from a template or let the AI generate one for you.
        </MudText>
        <MudStack Row="true" Justify="Justify.Center" Spacing="2">
            <MudButton Variant="Variant.Filled" Color="Color.Primary" 
                       StartIcon="@Icons.Material.Filled.Add"
                       Href="/strategies/builder">Start from Template</MudButton>
            <MudButton Variant="Variant.Outlined" Color="Color.Secondary"
                       StartIcon="@Icons.Material.Filled.AutoAwesome"
                       Href="/strategies/builder?mode=ai">Use AI Builder</MudButton>
        </MudStack>
    </MudPaper>
}
```

**Acceptance criteria:**
- [ ] Empty library shows a well-structured empty state with research lifecycle explanation.
- [ ] Both "Start from Template" and "Use AI Builder" CTAs navigate correctly.
- [ ] Empty state does not appear when strategies exist.

---

## P3 — EXPANDED INDICATOR LIBRARY (Skender Full Access)

---

### Story P3-5: Universal Skender Indicator Bridge — Expose All 150+ Indicators

**Problem:** Only 8–10 of Skender.Stock.Indicators' 150+ indicators are wrapped. Adding each one manually requires a new `SkenderIndicatorAdapter` subclass, a descriptor in `IndicatorRegistry`, and UI wiring.

**Goal:** Create a **generic bridge** that allows any Skender indicator to be used without hand-writing a new wrapper class.

**Files to create:**
- `src/TradingResearchEngine.Application/Indicators/SkenderBridgeIndicator.cs` *(new)*
- `src/TradingResearchEngine.Application/Indicators/SkenderIndicatorCatalog.cs` *(new)*
- `src/TradingResearchEngine.Core/Indicators/IndicatorRegistry.cs` *(extend with catalog entries)*
- `src/TradingResearchEngine.Web/Components/Builder/IndicatorPickerPanel.razor` *(new)*

---

#### Step A — `SkenderIndicatorCatalog` (the metadata layer)

Define a catalog entry that describes any Skender indicator generically:

```csharp
// src/TradingResearchEngine.Application/Indicators/SkenderIndicatorCatalog.cs

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Describes a Skender indicator and how to invoke it without a hand-written wrapper.
/// The factory delegate receives parameter values keyed by name and returns a
/// Func<IEnumerable<Quote>, IEnumerable<object>> (the Skender batch method).
/// </summary>
public sealed record SkenderCatalogEntry(
    string Key,                         // e.g. "MACD", "ADX", "WilliamsR"
    string DisplayName,
    string Description,
    string Category,                    // "Momentum", "Trend", "Volatility", "Volume", "Oscillator"
    IReadOnlyList<SkenderParamDef> Parameters,
    string PrimaryOutputField,          // The field name on the result object used as the "main" signal value
    IReadOnlyList<string> AllOutputFields,  // All extractable field names (e.g. ["Macd", "Signal", "Histogram"])
    int WarmupMultiplier = 2            // Queue capacity = max(params) * WarmupMultiplier
);

public sealed record SkenderParamDef(
    string Name,
    Type ClrType,           // int or decimal
    object DefaultValue,
    object Min,
    object Max,
    string Description
);
```

Populate the catalog with all practically useful Skender indicators. The minimum required set (beyond existing wrappers):

```
Category: Momentum
  - MACD (FastPeriod, SlowPeriod, SignalPeriod) → Macd, Signal, Histogram
  - Stochastic (LookbackPeriod, SignalPeriod, SmoothPeriod) → K, D
  - Williams %R (LookbackPeriod) → WilliamsR
  - ROC / Rate of Change (LookbackPeriod) → Roc
  - CCI (LookbackPeriod) → Cci
  - Ultimate Oscillator (ShortPeriod, MidPeriod, LongPeriod) → UltOsc

Category: Trend
  - ADX (LookbackPeriod) → Adx, Dip, Dim
  - Parabolic SAR (AccelerationStep, MaxAcceleration) → Sar
  - Supertrend (LookbackPeriod, Multiplier) → Supertrend, UpperBand, LowerBand
  - TEMA (LookbackPeriod) → Tema
  - WMA / Weighted MA (LookbackPeriod) → Wma
  - ALMA (LookbackPeriod, Offset, Sigma) → Alma
  - Hull MA (LookbackPeriod) → Hma
  - VWMA (LookbackPeriod) → Vwma

Category: Volatility
  - Keltner Channel (EmaPeriod, AtrPeriod, Multiplier) → UpperBand, Basis, LowerBand
  - Chandelier Exit (LookbackPeriod, Multiplier) → ChandelierExit
  - Chaikin Volatility (EmaPeriod, RocPeriod) → ChaikinVol
  - Standard Deviation (LookbackPeriod) → StdDev
  - Historical Volatility (LookbackPeriod) → Hv

Category: Volume
  - OBV → Obv
  - VWAP → Vwap (session-based — note: Skender computes from all available bars)
  - Chaikin Money Flow (LookbackPeriod) → Cmf
  - Money Flow Index (LookbackPeriod) → Mfi
  - Force Index (LookbackPeriod) → ForceIndex

Category: Oscillator / Other
  - Aroon (LookbackPeriod) → AroonUp, AroonDown, Oscillator
  - Ichimoku Cloud (TenkanPeriod, KijunPeriod, SenkouBPeriod) → TenkanSen, KijunSen, SenkouSpanA, SenkouSpanB, ChikouSpan
  - Pivot Points (PeriodSize) → R1, R2, R3, S1, S2, S3, PP
  - Elder Ray Index (EmaPeriod) → BullPower, BearPower
```

---

#### Step B — `SkenderBridgeIndicator` (the generic runtime adapter)

```csharp
// src/TradingResearchEngine.Application/Indicators/SkenderBridgeIndicator.cs

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Generic runtime bridge that wraps any Skender indicator described in
/// <see cref="SkenderIndicatorCatalog"/> without requiring a hand-written subclass.
///
/// Usage:
///   var indicator = new SkenderBridgeIndicator("MACD", new Dictionary&lt;string, object&gt;
///   {
///       ["FastPeriod"] = 12, ["SlowPeriod"] = 26, ["SignalPeriod"] = 9
///   }, outputField: "Macd");
///
/// The <paramref name="outputField"/> selects which property of the Skender result
/// object is extracted as the decimal signal value.
/// </summary>
public sealed class SkenderBridgeIndicator : IIndicatorSeries<decimal>
{
    private readonly SkenderCatalogEntry _entry;
    private readonly Dictionary<string, object> _params;
    private readonly string _outputField;
    private readonly Queue<Quote> _quotes;
    private readonly List<decimal> _results = new();
    private readonly int _capacity;

    public SkenderBridgeIndicator(
        string indicatorKey,
        Dictionary<string, object> parameters,
        string? outputField = null)
    {
        _entry = SkenderIndicatorCatalog.Get(indicatorKey);
        _params = parameters;
        _outputField = outputField ?? _entry.PrimaryOutputField;

        // capacity: use the largest int parameter value × warmup multiplier
        int maxPeriod = _entry.Parameters
            .Where(p => p.ClrType == typeof(int))
            .Select(p => parameters.TryGetValue(p.Name, out var v) ? (int)v : (int)p.DefaultValue)
            .DefaultIfEmpty(20)
            .Max();
        _capacity = Math.Max(maxPeriod * _entry.WarmupMultiplier, 60);
        _quotes = new Queue<Quote>(_capacity);
    }

    public IReadOnlyList<decimal> Results => _results;
    public bool IsWarm => _results.Count >= _capacity / _entry.WarmupMultiplier;

    public void Add(BarRecord bar)
    {
        var quote = new Quote
        {
            Date = bar.Timestamp.UtcDateTime,
            Open = bar.Open, High = bar.High, Low = bar.Low,
            Close = bar.Close, Volume = bar.Volume
        };
        if (_quotes.Count >= _capacity) _quotes.Dequeue();
        _quotes.Enqueue(quote);

        var computed = InvokeSkender(_quotes.ToList());
        if (computed.HasValue) _results.Add(computed.Value);
    }

    public void Reset()
    {
        _quotes.Clear();
        _results.Clear();
    }

    /// <summary>
    /// Invokes the Skender indicator via reflection on the windowed quotes.
    /// Returns the extracted output field value from the last result, or null if
    /// the result list is empty or the field value is null.
    /// </summary>
    private decimal? InvokeSkender(IReadOnlyList<Quote> quotes)
    {
        // Resolve the Skender extension method by name from SkenderIndicatorCatalog
        // Use SkenderCatalogEntry.InvokerFactory (a pre-compiled delegate, see below)
        return _entry.InvokerFactory(_params, quotes, _outputField);
    }
}
```

**Important implementation note for `InvokerFactory`:** Do **not** use runtime reflection on every bar — this would be O(n) reflection per bar, devastating performance. Instead, each `SkenderCatalogEntry` must carry a **pre-compiled delegate** created once at startup:

```csharp
// In SkenderIndicatorCatalog, each entry's InvokerFactory is a:
// Func<Dictionary<string, object> params, IReadOnlyList<Quote> quotes, string field, decimal?>

// Example for MACD:
entry.InvokerFactory = (p, quotes, field) =>
{
    int fast = (int)p.GetValueOrDefault("FastPeriod", 12);
    int slow = (int)p.GetValueOrDefault("SlowPeriod", 26);
    int signal = (int)p.GetValueOrDefault("SignalPeriod", 9);
    var results = quotes.GetMacd(fast, slow, signal).ToList();
    if (results.Count == 0) return null;
    var last = results[^1];
    return field switch
    {
        "Macd" => (decimal?)last.Macd,
        "Signal" => (decimal?)last.Signal,
        "Histogram" => (decimal?)last.Histogram,
        _ => null
    };
};
```

Each catalog entry has its own strongly-typed lambda — reflection is only used during catalog **construction** (at startup, once), not during bar processing.

---

#### Step C — Extend `IndicatorRegistry` in Core

Add all catalog entries to `IndicatorRegistry.All` as `IndicatorDescriptor` objects (for discovery by the UI and the AI builder). The descriptor maps to the catalog key:

```csharp
new IndicatorDescriptor(
    "MACD",
    "Moving Average Convergence Divergence — momentum indicator using two EMAs and a signal line.",
    new[]
    {
        new IndicatorParameterDescriptor("FastPeriod", "int", 2, 100, 12),
        new IndicatorParameterDescriptor("SlowPeriod", "int", 2, 200, 26),
        new IndicatorParameterDescriptor("SignalPeriod", "int", 2, 50, 9),
    },
    outputTypes: new[] { "Macd", "Signal", "Histogram" },
    primaryOutput: "Macd",
    category: "Momentum"),
```

Add `Category`, `OutputTypes`, and `PrimaryOutput` fields to `IndicatorDescriptor` if not present.

---

#### Step D — `IndicatorPickerPanel.razor` (UI for the strategy builder)

Create a browseable indicator picker for the Visual Rule Composer and the AI builder context:

```
src/TradingResearchEngine.Web/Components/Builder/IndicatorPickerPanel.razor
```

Features:
- Category filter chips: All / Momentum / Trend / Volatility / Volume / Oscillator
- Full-text search box filtering indicator names and descriptions
- Card grid (2 or 3 columns) showing: indicator name, category chip, description, output fields
- Each card has an "Add to strategy" button that fires a callback `EventCallback<IndicatorDescriptor> OnIndicatorSelected`
- Show current parameter defaults and allow editing before adding
- Show estimated warmup period based on parameter values
- Indicator cards display a `MudTooltip` with extended description on hover

---

#### Step E — Wire Indicators into Strategy Parameter Schema

Update `IStrategySchemaProvider` and the `ParameterGroupEditor` so that any parameter with `SensitivityHint.IndicatorPeriod` can be linked to a `SkenderBridgeIndicator`. This enables:
- The parameter sweep to automatically sweep indicator periods
- The AI builder to reference indicator names when generating strategy JSON

---

**Acceptance criteria for P3-5:**
- [ ] `SkenderBridgeIndicator` instantiates and produces correct values for at least: MACD, ADX, Stochastic, Williams %R, OBV, CCI, Supertrend, Keltner Channel.
- [ ] No reflection occurs during bar processing — all invocations go through pre-compiled delegates.
- [ ] `IndicatorRegistry.All` lists all 40+ catalog indicators with correct descriptors.
- [ ] `IndicatorPickerPanel` renders with category filter, search, and add-to-strategy flow.
- [ ] Performance test: `SkenderBridgeIndicator` (MACD) processes 100,000 bars in < 500ms (same order as hand-written `MacdIndicator`).
- [ ] Unit tests: each major catalog category has at least one indicator tested for correct output values against a known reference series.

---

## IMPLEMENTATION ORDER (Recommended for Kiro)

Execute stories in this sequence to minimize merge conflicts:

```
P0-4 → P0-5 → P0-6   (metrics fixes — no deps)
P0-3                   (fill logic — engine only)
P0-2                   (factory interface — needed before P0-1)
P0-1                   (async job dispatch — needs factory)
P1-1                   (IStrategy lifecycle)
P1-3                   (repository pagination)
P1-2                   (renderer refactor — independent)
P1-4                   (progress wiring — needs BackgroundStudyService)
P2-1                   (CPCV viz — needs P1-2)
P2-2                   (sweep metric selector)
P2-3                   (fill delay perturbation)
P2-4                   (checklist on dashboard)
P2-5 → P2-6            (AI streaming + refinement — sequential)
P2-7                   (dynamic interpretations)
P3-5                   (Skender bridge — large, independent)
P3-1 → P3-2 → P3-3 → P3-4  (UX polish — independent)
```

---

## TESTING REQUIREMENTS

For every story, the following test coverage is mandatory:

| Layer | Requirement |
|---|---|
| Core metrics changes | Pure unit tests with synthetic data in `TradingResearchEngine.UnitTests` |
| Engine fill logic | Unit tests per fill type and direction combination |
| Parallel safety | Concurrency stress test (20+ parallel instances) |
| `SkenderBridgeIndicator` | Output correctness test against hand-computed reference values |
| Repository changes | Integration test with a temp SQLite file |
| Blazor component changes | bUnit tests for component rendering and event handling |

---

## OUT OF SCOPE

The following are explicitly out of scope for this implementation batch:
- Multi-user / authentication
- Cloud deployment / Docker changes
- Database schema migrations (persist JSON, SQLite index only)
- Adding new strategy template types (engine strategies)
- Real-money broker integration
