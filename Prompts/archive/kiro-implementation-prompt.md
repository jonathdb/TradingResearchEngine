# Kiro Implementation Prompt: TradingResearchEngine — Development Branch

## Context

You are implementing a batch of improvements to **TradingResearchEngine**, a .NET 8 / Blazor Server / MudBlazor event-driven backtesting engine for quantitative strategy research.

- **Repository**: https://github.com/jonathdb/TradingResearchEngine
- **Target branch**: `Development`
- **Stack**: .NET 8, Blazor Server, MudBlazor, SQLite (via `SqliteIndexRepository<T>`), Google Gemini (via `IAIStrategyAssistant`), Skender.Stock.Indicators NuGet package

All implementation must follow the existing layered architecture:

```
Core/           ← Pure domain. Zero references to Application or Web.
Application/    ← Use-cases, workflows, services, DI wiring.
Web/            ← Blazor Server pages and components.
```

---

## Key File Locations (read these before starting each item)

| Component | Path |
|---|---|
| Backtest engine | `src/TradingResearchEngine.Core/Engine/BacktestEngine.cs` |
| Engine interface | `src/TradingResearchEngine.Core/Engine/IBacktestEngine.cs` |
| Strategy interface | `src/TradingResearchEngine.Core/Strategy/IStrategy.cs` |
| Metrics calculator | `src/TradingResearchEngine.Core/Metrics/MetricsCalculator.cs` |
| Fill logic | `src/TradingResearchEngine.Core/Engine/BacktestEngine.cs` (inner methods `TryFillLimit`, `TryFillStopMarket`, `TryFillStopLimit`) |
| Strategy Builder page | `src/TradingResearchEngine.Web/Pages/StrategyBuilder.razor` |
| Study Detail page | `src/TradingResearchEngine.Web/Pages/StudyDetail.razor` |
| Dashboard page | `src/TradingResearchEngine.Web/Pages/Dashboard.razor` |
| Strategy Library page | `src/TradingResearchEngine.Web/Pages/StrategyLibrary.razor` |
| AI assistant interface | `src/TradingResearchEngine.Application/AI/IAIStrategyAssistant.cs` |
| Background study service | `src/TradingResearchEngine.Application/Engine/BackgroundStudyService.cs` |
| Research workflows | `src/TradingResearchEngine.Application/Research/` |
| Indicator registry | `src/TradingResearchEngine.Application/Indicators/` |
| DI wiring | `src/TradingResearchEngine.Application/ServiceCollectionExtensions.cs` |
| Repository interface | `src/TradingResearchEngine.Application/` (find `IRepository<T>`) |

---

## Implementation Order

Work through the items **in priority order**. Complete each item fully (including tests where specified) before moving to the next.

---

## P0 — Critical Bugs

### Item 1: Async Backtest Job Dispatch

**Problem**: `StrategyBuilder.razor` calls `await RunUseCase.RunAsync(...)` directly on the Blazor SignalR circuit thread, blocking the UI for the entire duration of the backtest.

**Implementation**:

1. In `TradingResearchEngine.Application/Engine/`, create `JobExecutor.cs`:
   ```csharp
   public class JobExecutor
   {
       // Enqueue a BacktestJobRequest → returns a Guid jobId immediately
       // Runs the backtest on a background Task (not a thread pool thread; use Task.Run with a cancellation token)
       // Exposes GetStatus(jobId) → BacktestJobStatus { Pending, Running, Completed, Failed }
       // Exposes GetError(jobId) → string?
       // Exposes GetResultId(jobId) → Guid? (the RunId once complete)
       // Cleans up jobs older than 24 hours to avoid memory growth
   }
   ```

2. Register `JobExecutor` as a **singleton** in `ServiceCollectionExtensions.cs`.

3. In `StrategyBuilder.razor`, replace all inline `await RunUseCase.RunAsync(...)` calls with:
   ```csharp
   var jobId = JobExecutor.Enqueue(config);
   NavManager.NavigateTo($"/job-status/{jobId}");
   ```

4. Create `src/TradingResearchEngine.Web/Pages/JobStatus.razor` with route `/job-status/{JobId:guid}`:
   - Polls `JobExecutor.GetStatus(JobId)` every 2 seconds using `PeriodicTimer`
   - Shows a `MudProgressLinear` indeterminate bar while `Running`
   - Auto-redirects to `/runs/{resultId}` after 1 second when `Completed`
   - Shows a `MudAlert` with `Severity.Error` and the error message when `Failed`, plus an "Edit & Retry" button linking back to `StrategyBuilder`
   - Disposes the `PeriodicTimer` and `CancellationTokenSource` in `IAsyncDisposable.DisposeAsync`

**Acceptance**: Zero `await RunUseCase.RunAsync` calls remain in `StrategyBuilder.razor`.

---

### Item 2: IStrategyFactory for Parallel Isolation

**Problem**: `BacktestEngine` accepts `IStrategy` directly. Parallel walk-forward and parameter-sweep workflows share the same strategy instance, risking data races on indicator state.

**Implementation**:

1. In `src/TradingResearchEngine.Core/Strategy/`, create:
   ```csharp
   public interface IStrategyFactory
   {
       IStrategy Create(StrategyConfig config);
   }
   ```
   `StrategyConfig` is whatever config type is already used to construct strategies. If none exists, define a minimal `record StrategyConfig(...)` that captures the parameters needed.

2. Update `BacktestEngine` constructor to accept `IStrategyFactory factory` instead of `IStrategy strategy`. Call `factory.Create(config)` inside `RunAsync` before the event loop starts.

3. In `Application/Research/`, update `WalkForwardWorkflow` and `ParameterSweepWorkflow`: each parallel iteration must call `factory.Create(iterationConfig)` — never reuse a single `IStrategy` across iterations.

4. Update all `BacktestEngineFactory`/builder patterns in the Application layer to supply an `IStrategyFactory`.

5. Register all existing concrete strategy types with an `IStrategyFactory` implementation in `ServiceCollectionExtensions.cs`.

**Acceptance**: Running 20 concurrent parameter-sweep iterations against the same factory produces independent, deterministic results.

---

### Item 3: Stop-Limit Triggered State Persistence

**Problem**: In `TryFillStopLimit`, when a stop-limit order triggers on bar N but the limit is not reached, `ExecutionOutcome.Unfilled` is returned and `ProcessPendingOrders` re-queues the **original** order (without `StopTriggered = true`). On bar N+1 the stop check runs again from scratch.

**Implementation**:

1. Add a `TriggeredOrder` property to `ExecutionResult`:
   ```csharp
   public record ExecutionResult(
       ExecutionOutcome Outcome,
       FillEvent? Fill,
       string? RejectionReason = null,
       OrderEvent? TriggeredOrder = null);  // ← new
   ```

2. In `TryFillStopLimit`, when triggered but not filled, return:
   ```csharp
   return new ExecutionResult(ExecutionOutcome.Unfilled, null,
       TriggeredOrder: order with { StopTriggered = true });
   ```

3. In `ProcessPendingOrders`, when re-queuing an unfilled order, use:
   ```csharp
   remaining.Add(result?.TriggeredOrder ?? order);
   ```

**Acceptance**: A stop-limit order that triggers on bar N but misses the limit on that bar correctly persists `StopTriggered = true` and fills on the next bar where the limit is reached.

---

### Item 4: Synthetic Bar Timeframe Fix in CreateFillAtPrice

**Problem**: `CreateFillAtPrice` hardcodes `"1D"` as the timeframe in the synthetic `BarEvent`. If a commission or slippage model is timeframe-aware this produces incorrect results on intraday strategies.

**Implementation**:

In `CreateFillAtPrice`, replace the hardcoded `"1D"` with the timeframe from the current market event:

```csharp
private ExecutionResult? CreateFillAtPrice(OrderEvent order, decimal fillPrice, DateTimeOffset timestamp)
{
    string timeframe = state.LastMarketEvent is BarEvent lastBar
        ? lastBar.Timeframe
        : "1D";   // fallback only for tick data
    var syntheticBar = new BarEvent(order.Symbol, timeframe, fillPrice, fillPrice, fillPrice, fillPrice, 0m, timestamp);
    ...
}
```

Pass `state` or `timeframe` to `CreateFillAtPrice` as a parameter (whichever is cleaner given the current method signature).

---

### Item 5: PendingOrders Allocation Optimization

**Problem**: `ProcessPendingOrders` allocates `new List<OrderEvent>()` on every bar that has pending orders, creating GC pressure on M1 backtests (~2.6M bars over 5 years).

**Implementation**:

Add a pre-allocated swap buffer to `RunState`:

```csharp
// In RunState:
private readonly List<OrderEvent> _pendingSwap = new();

public void SwapToRemaining()
{
    // Swap _pendingSwap and PendingOrders references, clear the swap buffer
    var temp = PendingOrders;  // old pending (fully processed)
    // ... swap pattern
}
```

Rewrite `ProcessPendingOrders` to populate `_pendingSwap` as the "remaining" list, then swap at the end:
- No `new List<OrderEvent>()` allocation inside the method
- `_pendingSwap.Clear()` at the start of each call to reuse the buffer

---

## P0/P1 — Quant Correctness

### Item 6: Historical VaR Small-Sample Guard

**Problem**: `ComputeHistoricalVaR` (and CVaR if present) will index into a very small returns list when trades are few, producing a statistically meaningless result with no signal to the user.

**Implementation**:

In `MetricsCalculator.cs`, add at the top of both `ComputeHistoricalVaR` and `ComputeHistoricalCVaR`:

```csharp
private const int MinSampleForPercentile = 30;

public static decimal? ComputeHistoricalVaR(IReadOnlyList<decimal> returns, decimal confidence)
{
    if (returns.Count < MinSampleForPercentile) return null;
    ...
}
```

If these methods do not yet exist, add them with this guard from the start.

---

### Item 7: IProgress\<T\> Surface on IBacktestEngine

**Problem**: `IBacktestEngine.RunAsync` has no progress reporting surface. Callers cannot subscribe to live updates without reaching into the concrete `BacktestEngine`.

**Implementation**:

1. Update `IBacktestEngine`:
   ```csharp
   Task<BacktestResult> RunAsync(
       ScenarioConfig config,
       IProgress<ProgressUpdate>? progress = null,
       CancellationToken ct = default);
   ```

2. In `BacktestEngine.RunAsync`, emit progress every N bars (N = `Math.Max(1, totalBars / 100)` for ~100 updates):
   ```csharp
   if (barsProcessed % progressInterval == 0)
       progress?.Report(new ProgressUpdate(barsProcessed, totalBars, ...));
   ```

3. Inject `ILoggerFactory` instead of `NullLoggerFactory.Instance` for `Portfolio` and `DataHandler` construction inside `RunAsync`. Thread it through the `BacktestEngine` constructor.

---

## P1 — Architecture

### Item 8: IStrategy Lifecycle Hooks

**Problem**: `IStrategy` has no `Initialize` or `Reset` methods. Walk-forward must reconstruct the entire engine per window.

**Implementation**:

1. Add to `IStrategy`:
   ```csharp
   void Initialize(ScenarioConfig config);
   void Reset();
   ```

2. All concrete strategy implementations must implement both methods:
   - `Initialize`: apply config parameters to the strategy instance
   - `Reset`: clear all indicator buffers, position tracking, and internal state to initial values

3. `BacktestEngine.RunAsync` calls `strategy.Initialize(config)` before the event loop.

4. `WalkForwardWorkflow` calls `strategy.Reset()` before each out-of-sample window (rather than reconstructing).

---

### Item 9: Consolidate Strategies/Strategy Namespaces

**Problem**: `src/TradingResearchEngine.Application/Strategies/` and `src/TradingResearchEngine.Application/Strategy/` both exist, creating naming ambiguity.

**Implementation**:

1. Move all files from `Application/Strategy/` into `Application/Strategies/`.
2. Update all namespaces and `using` directives across the solution.
3. Delete the empty `Application/Strategy/` folder.
4. Verify the solution builds with zero errors after the rename.

---

### Item 10: InjectLoggerFactory into BacktestEngine

**Problem**: `BacktestEngine.RunAsync` constructs `Portfolio` and `DataHandler` with `NullLoggerFactory.Instance`, silently swallowing all log output from these components.

**Implementation**:

1. Add `ILoggerFactory loggerFactory` to `BacktestEngine` constructor parameters.
2. Pass `loggerFactory` to `Portfolio` and `DataHandler` inside `RunAsync`.
3. Update DI registration in `ServiceCollectionExtensions.cs` to inject `ILoggerFactory`.

---

## P1 — UI/UX

### Item 11: Live Study Progress Display

**Problem**: `ProgressUpdate` exists in Core and `BackgroundStudyService` exists in Application, but the Study Detail page does not subscribe to live progress events — long-running studies show no feedback.

**Implementation**:

1. In `BackgroundStudyService`, add a progress event mechanism (e.g., `event Action<Guid studyId, ProgressUpdate update> OnProgress`).

2. In `StudyDetail.razor`:
   - Subscribe to `BackgroundStudyService.OnProgress` in `OnInitializedAsync`
   - When the matching `studyId` fires, call `StateHasChanged()` to update a `MudProgressLinear` bar
   - Show "X of N simulations complete" label next to the bar
   - Hide the bar and render results when the study completes
   - Unsubscribe in `IAsyncDisposable.DisposeAsync` to prevent memory leaks

3. Wire this for: Monte Carlo, Parameter Sweep, Walk-Forward, and CPCV study types.

---

### Item 12: Builder Step Persistence on Refresh

**Problem**: Refreshing the browser mid-wizard always resets `CurrentStep` to 1, losing the user's position.

**Implementation**:

1. Add `CurrentStep` and `MaxVisitedStep` properties to `ConfigDraft` (the draft persistence model).

2. In `StrategyBuilder.razor`:
   - Auto-save `CurrentStep` to the draft (debounced at 500ms using `System.Threading.Timer` or `Task.Delay`)
   - On load, call `BuilderViewModel.FromDraft(draft)` and set `_vm.CurrentStep = draft.CurrentStep`
   - Enforce `_vm.CurrentStep <= _vm.MaxVisitedStep` to prevent skipping unvisited steps

---

### Item 13: Robustness Flag Tooltips

**Problem**: Robustness warning chips display a short label with no explanation. New users cannot interpret warnings like "K-Ratio < 0" or "High Sharpe".

**Implementation**:

1. Create `RobustnessWarningCatalog.cs` in the Application layer:
   ```csharp
   public static class RobustnessWarningCatalog
   {
       private static readonly Dictionary<string, string> _explanations = new()
       {
           ["High Sharpe"] = "A Sharpe ratio above 3 on historical data often indicates overfitting. Validate with walk-forward or Monte Carlo.",
           ["Low Trades"] = "Fewer than 30 trades makes statistical metrics unreliable. Consider a longer test period.",
           ["K-Ratio < 0"] = "The equity curve is declining or inconsistent. A positive, increasing K-Ratio indicates a healthy strategy.",
           // ... add all existing warning types
       };

       public static string GetExplanation(string warningKey) =>
           _explanations.TryGetValue(warningKey, out var text) ? text : warningKey;
   }
   ```

2. In `Dashboard.razor` and `StudyDetail.razor`, wrap each warning chip in `MudTooltip`:
   ```razor
   <MudTooltip Text="@RobustnessWarningCatalog.GetExplanation(warning)">
       <MudChip Color="Color.Warning">@warning</MudChip>
   </MudTooltip>
   ```

---

### Item 14: Dashboard Checklist Score Badge

**Problem**: The `ResearchChecklistService` 9-item checklist result is not visible on strategy cards in the Dashboard.

**Implementation**:

1. After `Repository.ListRecentAsync(...)`, call `ChecklistService.Evaluate(run)` for each run.

2. On each strategy card, add a checklist badge:
   ```razor
   <MudChip Color="@GetChecklistColor(score)" Size="Size.Small">
       @score/9 checks
   </MudChip>
   ```
   Where `GetChecklistColor` returns `Color.Success` for ≥7, `Color.Warning` for 5–6, `Color.Error` for <5.

3. Wrap with `MudTooltip` listing the names of failed checks (one per line).

4. When no runs exist for a strategy, display "—".

---

### Item 15: Dashboard Recent Runs Sorting and Filtering

**Problem**: The Dashboard recent-runs table is capped at 10 with no sort or filter controls.

**Implementation**:

1. Ensure the runs table uses `MudTable` with `SortLabel` attributes on Sharpe, Max Drawdown, and Trade Count column headers.

2. Add strategy filter `MudChip` buttons above the table that reactively filter displayed rows by strategy type.

3. Add a "Show failed runs" `MudSwitch` that toggles inclusion of failed-status runs.

4. All sorting and filtering operates on the in-memory result set — no additional repository queries.

---

### Item 16: Strategy Library Empty State

**Problem**: When no strategies exist, `StrategyLibrary.razor` shows an empty list with no guidance.

**Implementation**:

In `StrategyLibrary.razor`, when the strategy list is empty, render:

```razor
<MudStack AlignItems="AlignItems.Center" Class="mt-16 pa-8">
    <MudIcon Icon="@Icons.Material.Outlined.Science" Size="Size.Large" Color="Color.Default" />
    <MudText Typo="Typo.h5" Class="mt-4">No strategies yet</MudText>
    <MudText Typo="Typo.body1" Color="Color.Secondary" Class="mb-6" Style="max-width:480px; text-align:center">
        The research lifecycle starts with a hypothesis. Create your first strategy,
        run a backtest, then validate it with walk-forward and Monte Carlo studies.
    </MudText>
    <MudStack Row="true" Spacing="2">
        <MudButton Variant="Variant.Filled" Color="Color.Primary" Href="/builder">
            Start from Template
        </MudButton>
        <MudButton Variant="Variant.Outlined" Color="Color.Primary" Href="/builder?mode=ai">
            Use AI Builder
        </MudButton>
    </MudStack>
</MudStack>
```

---

## P2 — Research/Robustness

### Item 17: CPCV Distribution Visualization

**Problem**: CPCV is one of the most informative overfitting tests, but its result page only shows a 3-card KPI summary with no distribution chart.

**Implementation**:

1. Add `PathSharpeRatios` to `CpcvResult`:
   ```csharp
   public record CpcvResult(
       ...,
       IReadOnlyList<decimal> PathSharpeRatios);
   ```

2. In `CpcvWorkflow`, populate `PathSharpeRatios` with the Sharpe ratio of each combinatorial OOS path.

3. In the CPCV result renderer (create `CpcvResultRenderer.razor`):
   - Render a Plotly.NET (or ApexCharts) histogram of `PathSharpeRatios`
   - Color bars: red for Sharpe < 0, yellow for 0 ≤ Sharpe < 1, green for Sharpe ≥ 1
   - Add vertical dashed lines at zero and at the median Sharpe
   - Below the chart, render a percentile table: P10, P25, P50, P75, P90

---

### Item 18: Parameter Sweep Heatmap Metric Selector

**Problem**: `ParameterSweepHeatmap` renders only Sharpe ratio. Researchers need to view MaxDD, WinRate, ProfitFactor, and TotalTrades surfaces.

**Implementation**:

1. Ensure `SweepCell` (or equivalent data type) carries all five metrics: `SharpeRatio`, `MaxDrawdown`, `WinRate`, `ProfitFactor`, `TotalTrades`.

2. Add a `MudSelect<string> MetricSelector` above the heatmap component with options: "Sharpe Ratio", "Max Drawdown", "Win Rate", "Profit Factor", "Trade Count".

3. When the selected metric changes, re-map the heatmap's `ZValues` to the corresponding field. For Max Drawdown, invert the color scale (lower = green).

4. The heatmap re-renders reactively without a page reload.

---

### Item 19: Fill Delay in Sensitivity Analysis

**Problem**: The `SensitivityWorkflow` perturbs slippage and commission but not fill timing. A 1-bar entry delay is a critical realism check.

**Implementation**:

1. In `SensitivityWorkflow`, add `FillDelayBars` as a standard perturbation dimension with values `[0, 1, 2]`.

2. For each fill-delay variant, run a full backtest with `ScenarioConfig.EffectiveExecutionConfig.FillDelayBars` set to the variant value.

3. Expose `FillDelayBars` in the Advanced Overrides section of `StrategyBuilder.razor` (numeric input, 0–5, default 0).

4. Include fill-delay sensitivity results in the sensitivity result renderer.

---

### Item 20: Pluggable Study Result Renderer Registry

**Problem**: `StudyDetail.razor` contains a `switch` on `StudyType` for result rendering. Every new study type requires modifying the page.

**Implementation**:

1. Define in the Application layer:
   ```csharp
   public interface IStudyResultRenderer
   {
       StudyType StudyType { get; }
       Type ComponentType { get; }  // The Blazor component Type
   }
   ```

2. Create a `StudyRendererRegistry` that maps `StudyType → Type` (Blazor component type).

3. Register all existing renderers in `ServiceCollectionExtensions.cs` using `IStudyResultRenderer` implementations.

4. In `StudyDetail.razor`, replace the `switch` with:
   ```razor
   @{
       var rendererType = RendererRegistry.GetComponentType(study.StudyType);
       var parameters = new Dictionary<string, object> { ["Result"] = study.Result };
   }
   <DynamicComponent Type="rendererType" Parameters="parameters" />
   ```

5. Extract each existing `switch` case into its own dedicated renderer component.

---

## P2 — Strategy Creation

### Item 21: AI Strategy Streaming and Refinement

**Problem**: `IAIStrategyAssistant` has a single-turn generate method with no streaming or iterative refinement.

**Implementation**:

1. Add to `IAIStrategyAssistant`:
   ```csharp
   IAsyncEnumerable<string> GenerateStreamAsync(
       string prompt,
       CancellationToken ct = default);

   IAsyncEnumerable<string> RefineStreamAsync(
       AIStrategyDraft current,
       string feedback,
       CancellationToken ct = default);
   ```

2. Add to `AIStrategyDraft`:
   ```csharp
   public record AIStrategyDraft(
       ...,
       IReadOnlyList<string> RefinementHistory = default);
   ```

3. In `StrategyBuilder.razor` (AI mode):
   - When generating, call `GenerateStreamAsync` and append tokens to a `StringBuilder`; call `StateHasChanged()` on each token
   - Show a "Stop generation" `MudIconButton` that calls `cts.Cancel()`
   - When the stream completes, parse the full JSON as `AIStrategyDraft` and auto-populate form fields

4. When a draft exists, show a "Refine with AI feedback" `MudTextField` + submit button:
   - On submit, call `RefineStreamAsync(currentDraft, feedbackText)` and stream the refined response
   - Append the feedback prompt to `draft.RefinementHistory`
   - Show all previous prompts in a collapsible `MudExpansionPanel` with "Revert to this version" buttons

---

### Item 22: Result-Aware Dynamic Study Interpretations

**Problem**: `GetInterpretation()` returns static text regardless of result values, providing no actionable guidance.

**Implementation**:

1. Create `StudyInterpretationService.cs` in `Application/Research/`:
   ```csharp
   public class StudyInterpretationService
   {
       public string Interpret(StudyResult result);
   }
   ```

2. Implement threshold-based interpretation for each study type:
   - **Monte Carlo**: if `RuinProbability > 0.05`, include "⚠ Elevated ruin risk: {value:P1} probability of ruin exceeds the 5% threshold."
   - **CPCV**: if `ProbabilityOfOverfitting > 0.50`, include "🔴 High overfitting probability ({value:P0}). Consider reducing parameter count or using a simpler signal."
   - **Walk-Forward**: if `OosSharpe < 0.5 * IsSharpe`, include "⚠ OOS Sharpe ({oos:F2}) is less than half of IS Sharpe ({is:F2}). Performance may not generalise."
   - All interpretations include the actual numeric values.

3. Register as scoped in `ServiceCollectionExtensions.cs`.

4. In study detail renderer components, inject `StudyInterpretationService` and render the interpretation text in a `MudAlert` below the chart.

5. Remove all inline static interpretation strings from Razor components.

---

### Item 23: Universal Skender Indicator Bridge

**Problem**: The indicator library has only ~7 hand-written indicators. Accessing Skender's 150+ indicators requires writing a wrapper per indicator.

**Implementation**:

1. Create `src/TradingResearchEngine.Application/Indicators/SkenderBridgeIndicator.cs`:
   ```csharp
   public class SkenderBridgeIndicator : IIndicator
   {
       // Generic bridge: wraps any Skender indicator
       // Uses pre-compiled Expression<Func<...>> delegates — ZERO reflection during bar processing
       // Accepts a SkenderIndicatorDescriptor from the catalog
       // Maintains an internal List<Quote> window; on each bar appends a Quote and calls the Skender method
       // Returns the latest result's specified output field
   }
   ```

2. Create `SkenderIndicatorCatalog.cs` describing at minimum: MACD, ADX, Stochastic, Williams %R, OBV, CCI, Supertrend, Keltner Channel, RSI, Bollinger Bands, ATR, EMA, SMA, Donchian (total ≥ 40 entries). Each entry includes:
   - `Name`, `Category`, `Description`
   - `Parameters`: list of `IndicatorParameterDescriptor` (name, type, default, min, max)
   - `OutputFields`: list of output field names (e.g., `["Macd", "Signal", "Histogram"]` for MACD)

3. Pre-compile all Skender method delegates at catalog initialization time (not per-bar). Use `Expression.Call` → `Expression.Lambda` → `Compile()`.

4. Register all catalog indicators in `IndicatorRegistry.All`.

5. In `StrategyBuilder.razor`, add an `IndicatorPickerPanel` component:
   - Category filter chips (Trend, Momentum, Volatility, Volume)
   - Text search input
   - Scrollable list of matching indicator cards showing name, category, and description
   - "Add to strategy" button that appends the selected indicator to the strategy's indicator list with default parameters

**Performance requirement**: Processing 100,000 bars through the Skender MACD bridge must complete in under 500ms (benchmark in a unit test).

---

## Testing Requirements

For each P0 item, write at least one unit test covering the acceptance criteria:

- **Item 1**: Test that `JobExecutor.Enqueue` returns a `Guid` without awaiting the backtest.
- **Item 2**: Test that two `factory.Create(config)` calls return instances with independent indicator state.
- **Item 3**: Test that a stop-limit order that triggers on bar N but misses the limit fills correctly on bar N+1.
- **Item 4**: Test that `CreateFillAtPrice` uses the timeframe from the current `BarEvent`.
- **Item 7 (Sortino)**: Already verified as correct — add a regression test with a known return series.
- **Item 22 (Skender Bridge)**: Benchmark test: 100,000 bars through MACD bridge < 500ms.

Test project: `tests/TradingResearchEngine.Tests/`

---

## Constraints and Rules

1. **No breaking changes to `IStrategy.OnMarketData`** — all existing strategies must continue to compile without modification (add `Initialize` and `Reset` as methods with default empty implementations if needed to avoid breaking changes, then make them mandatory in a follow-up).

2. **No localStorage or sessionStorage** in any Blazor component — use in-memory state and draft persistence via the existing repository.

3. **Blazor streaming render** is available (.NET 8) — use `StateHasChanged()` with `await InvokeAsync(StateHasChanged)` for thread safety when updating from background callbacks.

4. **MudBlazor** is the only component library — do not introduce additional UI libraries.

5. **All new Application-layer services** must be registered in `ServiceCollectionExtensions.cs`.

6. **Core must remain pure** — no Application or Web layer references in `TradingResearchEngine.Core`.

7. **Existing tests must pass** — do not break any currently passing unit tests.
