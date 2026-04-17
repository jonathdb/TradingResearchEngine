# Kiro Development Prompt — TradingResearchEngine V6

## Context & Project Overview

You are continuing development of **TradingResearchEngine**, a personal quantitative strategy research platform built with .NET 8 / C# 12 and a Blazor Server UI (MudBlazor). The project is at **V5** (fully implemented per `.kiro/specs/v5-engine-usability-and-quant-upgrades/tasks.md`). The current V5 codebase includes:

- A Clean Architecture solution: `Core ← Application ← Infrastructure ← { Cli, Api, Web }`
- A full event-driven backtest engine with bar-level and tick-level replay
- Research workflows: Monte Carlo (block bootstrap), Walk-Forward (rolling + anchored), Parameter Sweep, Sensitivity, Regime Segmentation, Benchmark Comparison, Variance, Realism Sensitivity, Perturbation
- An 8-item `ResearchChecklistService` with `TrialBudgetStatus` and `NextRecommendedAction`
- A 5-step Strategy Builder Wizard with `StrategyParameterSchema` / `[ParameterMeta]` attribute support
- PropFirm evaluation module with multi-phase rule packs
- Dukascopy data provider (LZMA `.bi5` decoding, per-day CSV cache, tick support)
- JSON flat-file persistence (`JsonFileRepository<T>`) via `IRepository<T>` interface
- `Direction.Short` enum exists with `LongOnlyGuard` — short execution is guarded and deferred to **this V6 sprint**
- `BarsPerYear` defaults exist for Daily (252) and H4 (1512) but NOT for intraday timeframes below H4

**Primary data timeframes in use: 1m, 5m, 15m, 30m, 1H, 2H, 4H** — all Dukascopy Forex/CFD data.

Read the full steering documents in `.kiro/steering/` before writing any code. They define all architectural rules, naming conventions, async patterns, logging standards, and test requirements. Violating the dependency rule (`Core ← Application ← Infrastructure`) is never acceptable.

---

## V6 Scope — Four Development Tracks

V6 is split into four sequential tracks. Complete each track's checkpoint before starting the next.

---

### Track 1 — Short Selling Engine (Priority 1)

Remove the `LongOnlyGuard` restriction and implement full long/short execution for all bar-level strategies.

#### 1.1 Core — Short Execution in SimulatedExecutionHandler

In `TradingResearchEngine.Application/Engine/SimulatedExecutionHandler.cs`:

- Remove the `LongOnlyGuard.EnsureLongOnly(direction)` call from the fill path
- When `Direction == Short` and no position is open → open a short position (negative quantity)
- When `Direction == Flat` and a short position is open → close the short:
  - Realised PnL for short = `(EntryPrice - ExitPrice) × Quantity × ContractSize`
  - This mirrors the existing long close formula in reverse
- When `Direction == Long` and a short position is open → reverse: close short, optionally open long (configurable via `AllowReversals` flag on `ExecutionConfig`, default `false`)
- Short positions must be tracked in `Portfolio` with correct mark-to-market:
  - `UnrealisedPnl` for short = `(EntryPrice - CurrentPrice) × Quantity × ContractSize`
  - `TotalEquity` must correctly reflect open short unrealised PnL on every bar

In `TradingResearchEngine.Core/Portfolio/Portfolio.cs`:

- Add `ShortPositions` dictionary (parallel to existing `Positions`)
- `GetExposureBySymbol()` must sum both long and short exposure (absolute values)
- `OpenPositionCount` must count both long and short open positions
- Mark-to-market on `BarEvent` must update short unrealised PnL

#### 1.2 Core — Direction.Short in IPositionSizingPolicy

All four `IPositionSizingPolicy` implementations (`FixedFractionalSizing`, `FixedLotSizing`, `VolatilityTargetSizing`, `PercentOfEquitySizing`) must return a negative quantity when `direction == Direction.Short`. The existing `DefaultRiskLayer` uses `Math.Abs(quantity)` for exposure checks — update to handle signed quantities correctly.

#### 1.3 Application — Strategy Templates for Short

Add `Direction` parameter support to the following strategies (all in `TradingResearchEngine.Application/Strategies/`):

- `DonchianBreakoutStrategy.cs` — add `[ParameterMeta]` for a `Direction` parameter (Long / Short / Both). When `Both`, emit `Long` on upper breakout and `Short` on lower breakout.
- `VolatilityScaledTrendStrategy.cs` — add bidirectional signal support
- `ZScoreMeanReversionStrategy.cs` — already signals both sides by z-score threshold; wire `Direction.Short` instead of `Direction.Flat` on short entry
- `StationaryMeanReversionStrategy.cs` — same as ZScore

`BaselineBuyAndHoldStrategy` and `MacroRegimeRotationStrategy` remain long-only.

#### 1.4 Core — BarsPerYear Intraday Defaults

`ScenarioConfig.BarsPerYear` is the canonical annualisation source. The steering doc (`tech.md`) defines defaults only for Daily (252) and H4 (1512). Add the full intraday table to `TradingResearchEngine.Core/Configuration/BarsPerYearDefaults.cs` (create this file):

```csharp
public static class BarsPerYearDefaults
{
    // Trading hours per year: ~252 days × 24h for Forex, 6.5h for equities
    // Forex (24h/day × 252 trading days)
    public const int M1  = 362880;   // 252 × 1440
    public const int M5  = 72576;    // 252 × 288
    public const int M15 = 24192;    // 252 × 96
    public const int M30 = 12096;    // 252 × 48
    public const int H1  = 6048;     // 252 × 24
    public const int H2  = 3024;     // 252 × 12
    public const int H4  = 1512;     // 252 × 6
    public const int D1  = 252;      // 252 × 1
}
```

Wire this into `PreflightValidator` (step 4.2 in V5): when `ScenarioConfig.BarsPerYear` is default (252) but the resolved timeframe is intraday, emit a `PreflightSeverity.Warning` with code `"BARSYEAR_MISMATCH_INTRADAY"` and suggested value.

Update `Step2DataExecutionWindow.razor` to auto-populate `BarsPerYear` from the selected timeframe using `BarsPerYearDefaults`.

Extend the timeframe selector in `Step2DataExecutionWindow.razor` to include: `1m`, `5m`, `15m`, `30m`, `1H`, `2H`, `4H`, `Daily`. Remove all hardcoded `MudSelectItem` lists and drive from a `TimeframeOptions` static class in the Web layer.

#### 1.5 Tests — Short Selling

All new short-selling logic requires test coverage per `.kiro/steering/testing-standards.md`:

- `UnitTests/V6/ShortSellingTests.cs`:
  - Short open: fill at correct price with negative quantity
  - Short close: correct PnL = (entry - exit) × qty
  - Short mark-to-market: `TotalEquity` moves inversely to price
  - Short reversal when `AllowReversals = true`
  - `LongOnlyGuard` removed: `Direction.Short` no longer throws
- `UnitTests/V6/BarsPerYearDefaultsTests.cs`:
  - All 8 timeframe constants are positive integers
  - M1 = 252 × 1440 exactly
  - Preflight emits warning for daily BarsPerYear on M1 data
- Property test: `ShortLongPnlSymmetry` — a long trade and a symmetric short trade on the same price series produce equal-magnitude PnL

**Checkpoint 1**: All existing tests still pass. New unit tests pass. No `LongOnlyGuard.EnsureLongOnly` calls remain (except in the deleted guard class itself, which can be removed or marked `[Obsolete]`).

---

### Track 2 — Persistence & Performance (Priority 2)

Replace the full-scan JSON persistence bottleneck and parallelise the walk-forward and sweep workflows.

#### 2.1 Infrastructure — SQLite Index Layer

The `IRepository<T>` interface is already defined in Core. Add a `SqliteIndexRepository<T>` implementation in `TradingResearchEngine.Infrastructure/Persistence/SqliteIndexRepository.cs`.

**Design:**

- Primary store remains JSON files (no migration required). SQLite is an index-only layer.
- On startup, `SqliteIndexRepository<T>` scans the existing JSON directory and builds an SQLite index in `{AppData}/TradingResearchEngine/index.db`.
- Schema for `BacktestResult`:
  ```sql
  CREATE TABLE IF NOT EXISTS BacktestResultIndex (
      Id TEXT PRIMARY KEY,
      StrategyVersionId TEXT NOT NULL,
      RunDate TEXT,
      Status TEXT,
      FilePath TEXT NOT NULL
  );
  CREATE INDEX IF NOT EXISTS idx_br_version ON BacktestResultIndex(StrategyVersionId);
  CREATE INDEX IF NOT EXISTS idx_br_date ON BacktestResultIndex(RunDate);
  ```
- `ListAsync(ct)` reads the JSON files for ALL entries (backward compat)
- `ListByVersionAsync(versionId, ct)` — new method added to `IStudyRepository` and a new `IBacktestResultRepository` interface — queries the index, then reads only the matching JSON files
- `GetAsync(id, ct)` reads directly from file path stored in index (O(1) instead of O(n))
- Index stays in sync: `SaveAsync` writes JSON file then upserts index row; `DeleteAsync` removes both
- Use `Microsoft.Data.Sqlite` NuGet package (add to `tech.md`)

Add `IBacktestResultRepository` interface in `TradingResearchEngine.Application/Research/`:
```csharp
public interface IBacktestResultRepository : IRepository<BacktestResult>
{
    Task<IReadOnlyList<BacktestResult>> ListByVersionAsync(string versionId, CancellationToken ct = default);
    Task<IReadOnlyList<BacktestResult>> ListByStrategyAsync(string strategyId, CancellationToken ct = default);
}
```

Update `ResearchChecklistService.GetVersionAsync` to use a direct `IStrategyRepository.GetVersionAsync(string versionId)` method instead of the current O(n×m) full-scan loop. Add `GetVersionAsync(string versionId, CancellationToken)` to `IStrategyRepository`.

**Strategy Retirement:**
Add `DevelopmentStage.Retired` awareness to the Strategy Library page (`StrategyLibrary.razor` or equivalent). Strategies with `Stage == Retired` should:
- Be hidden from the default view (show only Active stages by default)
- Have a "Show Retired" toggle to reveal them
- Display a greyed-out "RETIRED" badge on the card
- Support "Un-Retire" action (sets stage back to `Hypothesis`)
- The retirement action should prompt: "Why are you retiring this strategy?" with an optional free-text note stored on `StrategyIdentity.RetirementNote` (add this field as nullable `string?`)

Add `RetirementNote` as a nullable trailing parameter to `StrategyIdentity` in Core.

#### 2.2 Application — Parallel Walk-Forward Execution

In `TradingResearchEngine.Application/Research/WalkForwardWorkflow.cs`:

Replace the sequential `while (true)` window loop with a parallel execution pattern:

1. Pre-compute all window date ranges first (pure date arithmetic, no I/O)
2. Execute windows using `Parallel.ForEachAsync` with a `SemaphoreSlim` limiting concurrency to `Math.Max(1, Environment.ProcessorCount - 1)`
3. Collect results into a `ConcurrentBag<WalkForwardWindow>` and sort by `windowIndex` after completion
4. Each window creates its own `SemaphoreSlim` scope — the inner sweep is itself parallel (Task.WhenAll across sweep combinations)
5. Propagate `CancellationToken` to every parallel unit

```csharp
// Approximate structure
var windowSpecs = PrecomputeWindows(options, dataFrom, dataTo);
var results = new ConcurrentBag<WalkForwardWindow>();
var semaphore = new SemaphoreSlim(Math.Max(1, Environment.ProcessorCount - 1));

await Parallel.ForEachAsync(windowSpecs, ct, async (spec, token) =>
{
    await semaphore.WaitAsync(token);
    try
    {
        var window = await RunWindowAsync(spec, token);
        if (window is not null) results.Add(window);
    }
    finally { semaphore.Release(); }
});
```

Update `IProgressReporter` progress reporting for parallel WF to report aggregate completion (windows completed / total windows).

#### 2.3 Application — Parallel Parameter Sweep

In `TradingResearchEngine.Application/Research/ParameterSweepWorkflow.cs`:

Apply the same `Parallel.ForEachAsync` + `SemaphoreSlim` pattern to the parameter grid. Each parameter combination is an independent backtest run with no shared mutable state (each creates its own `EventQueue` instance per the domain boundaries doc). Concurrency limit: `Environment.ProcessorCount - 1`.

#### 2.4 Infrastructure — Intraday Data Caching Improvement

The `DukascopyDataProvider` currently caches at 1-minute granularity per day. For intraday timeframes (5m, 15m, 30m, 1H, 2H, 4H), the aggregation step (`DukascopyHelpers.Aggregate`) is re-run on every backtest even when cached 1m data is available.

Add a second-level aggregated cache:
- Cache path: `{CacheDir}/{symbol}/{priceType}/{year}/{month}/{day}_{interval}.csv`
- Before aggregating, check if the aggregated CSV exists and is newer than the source 1m cache
- Write aggregated result to cache after first aggregation
- This eliminates redundant aggregation for repeated runs at the same timeframe

Add cache hit/miss logging at `LogLevel.Debug` to match existing patterns.

#### 2.5 Tests — Persistence & Parallelism

- `UnitTests/V6/SqliteIndexRepositoryTests.cs`:
  - Index built correctly from existing JSON files
  - `ListByVersionAsync` returns only matching items
  - Save → index row created; Delete → index row removed
  - Cold start (no index file) falls back to full scan and rebuilds index
- `UnitTests/V6/ParallelWalkForwardTests.cs`:
  - Same seed + same config → identical results as sequential execution
  - Cancellation token respected (all windows cancelled promptly)
  - Window count formula unchanged (existing Property 7 still passes)
- `UnitTests/V6/StrategyRetirementTests.cs`:
  - Retired strategy hidden from default library view
  - RetirementNote persisted and retrieved correctly
  - Un-retire sets stage to Hypothesis

**Checkpoint 2**: All tests pass. `ListAsync` is replaced by `ListByVersionAsync` in `ResearchChecklistService` and `StrategyDetail.razor`. Walk-forward and sweep benchmarks (add simple timing test) show parallel > sequential for ≥4 cores.

---

### Track 3 — Visualisation (Priority 3)

Add interactive charts to Strategy Detail and Research result pages. Choose **Plotly.Blazor** (`Plotly.Blazor` NuGet package, MIT licence) as the charting library. Add it to `tech.md` in the NuGet packages table.

#### 3.1 Shared — EquityCurveChart Component

Create `TradingResearchEngine.Web/Components/Charts/EquityCurveChart.razor`:

```razor
@* Parameters: IReadOnlyList<EquityCurvePoint> Curve, string Title, bool ShowDrawdown *@
```

- Primary series: `TotalEquity` line, labelled by `Timestamp`
- Secondary series (when `ShowDrawdown = true`): running drawdown as a filled area below zero, right Y-axis
- Colour scheme: equity = MudBlazor primary colour; drawdown = MudBlazor error colour with 30% opacity
- Responsive width (100%), fixed height 320px
- Tooltip: Timestamp, Equity, Unrealised PnL, Drawdown %
- No external data fetch — accept the curve as a parameter

#### 3.2 StrategyDetail.razor — Equity Curve on Overview Tab

In the Overview tab of `StrategyDetail.razor`, below the metrics summary chips and above the Research Checklist, add:

- `<EquityCurveChart Curve="_latestRun.EquityCurve" Title="Latest Run — Equity Curve" ShowDrawdown="true" />`
- Show only when `_latestRun is not null`
- Empty state: `<MudText Typo="Typo.body2" Color="Color.Secondary">Run a backtest to see the equity curve</MudText>`

#### 3.3 ResultDetail.razor — Full Metrics Chart Suite

In `ResultDetail.razor`, add a "Charts" tab (alongside the existing Trades tab) containing:

1. **Equity Curve** — full `EquityCurveChart` component with drawdown overlay
2. **Monthly Returns Heatmap** — calendar grid showing monthly return % per cell, green/red colour scale
   - Computed from the equity curve: group by month, compute month return
   - Component: `MonthlyReturnsHeatmap.razor`
3. **Trade Distribution** — histogram of individual trade PnL (NetPnl), binned into 20 buckets
   - Component: `TradePnlHistogram.razor`
4. **Holding Period Distribution** — histogram of trade duration in bars
   - Component: `HoldingPeriodHistogram.razor`

#### 3.4 Research Pages — Monte Carlo Fan Chart

In the Monte Carlo result display (under `Research/` pages), add:

Create `TradingResearchEngine.Web/Components/Charts/MonteCarloFanChart.razor`:
- Parameters: `IReadOnlyList<MonteCarloPercentileBand> Bands`, `decimal StartEquity`
- Three series: P10 (red dashed), P50 (white/primary solid), P90 (green dashed)
- Filled band between P10 and P90 with 20% opacity
- X-axis: trade number; Y-axis: equity
- Horizontal reference line at `StartEquity` (labelled "Start")
- Display ruin probability as a subtitle: `"Ruin probability: {ruinProb:P1}"`

Wire this chart into the Monte Carlo study result page wherever `MonteCarloResult` is displayed.

#### 3.5 Research Pages — Walk-Forward Composite Curve

In the Walk-Forward result display, add:

Create `TradingResearchEngine.Web/Components/Charts/WalkForwardCompositeChart.razor`:
- Parameters: `WalkForwardSummary Summary`
- Show the stitched OOS composite equity curve (`Summary.CompositeOosEquityCurve`) as a line
- Shade each walk-forward window with alternating background colours (light bands) to show window boundaries
- Show a vertical dashed line at each window boundary
- Secondary Y-axis: per-window OOS Sharpe ratio as a bar chart (green if positive, red if negative)

#### 3.6 Research Pages — Parameter Sweep Heatmap

Create `TradingResearchEngine.Web/Components/Charts/ParameterSweepHeatmap.razor`:
- Parameters: `SweepResult SweepResult`, `string XParam`, `string YParam`, `string MetricName`
- 2D heatmap: X = first parameter values, Y = second parameter values, cell colour = Sharpe ratio
- Colour scale: red (negative) → white (zero) → green (positive)
- Tooltip: exact parameter values + Sharpe, MaxDD, WinRate
- Dropdowns above the chart to select which two parameters are on X/Y axes (when sweep has >2 params)
- Display only when sweep has ≥2 numeric parameters and ≥4 unique values per parameter

Wire into the Parameter Sweep result page.

#### 3.7 Strategy Builder — TimelineSplitVisualizer Enhancement

In `Step2DataExecutionWindow.razor`, enhance the `TimelineSplitVisualizer` with a mini bar-count density plot:
- Show a horizontal bar representing the full date range
- Colour segments: IS (blue), OOS (orange), Sealed (red/hatched if exists)
- Below each segment: bar count label (derived from `BarsPerYear × segment_years`)
- Warning icon if IS segment has < `MinBTL` bars

#### 3.8 Tests — Chart Components

Chart components are Blazor UI components and do not require xUnit tests. However, the data transformation logic should be extracted into pure static helpers and unit tested:

- `UnitTests/V6/MonthlyReturnCalculatorTests.cs`:
  - Equity curve covering 3 months → 3 monthly returns computed correctly
  - Single bar → single month return
  - Returns normalised as percentages (not decimals)
- `UnitTests/V6/TradePnlBinningTests.cs`:
  - 20 bins cover full PnL range
  - Empty trades list → empty histogram

**Checkpoint 3**: All chart components render without errors on the Strategy Detail overview. Monte Carlo fan chart, walk-forward composite, and sweep heatmap are visible on their respective result pages.

---

### Track 4 — Quant Depth & Lifecycle Improvements (Priority 4)

Finish incomplete quant features and wire up the remaining TODO items from V5.

#### 4.1 PropFirm Evaluation — Wire Checklist Item

In `ResearchChecklistService.cs`, replace:
```csharp
bool propFirmEvaluation = false; // TODO: wire to prop firm evaluation persistence
```

Add `IPropFirmEvaluationRepository` in `Application/PropFirm/`:
```csharp
public interface IPropFirmEvaluationRepository
{
    Task<bool> HasCompletedEvaluationAsync(string strategyVersionId, CancellationToken ct = default);
    Task SaveEvaluationAsync(string strategyVersionId, PropFirmEvaluationRecord record, CancellationToken ct = default);
}
```

Create `PropFirmEvaluationRecord` (sealed record: `StrategyVersionId`, `FirmName`, `PhaseName`, `Passed`, `EvaluatedAt`).

Implement `JsonPropFirmEvaluationRepository` in Infrastructure, following the standard `JsonFileRepository<T>` pattern.

Wire `HasCompletedEvaluationAsync` into `ResearchChecklistService.ComputeAsync`.

This unblocks the 8th checklist item and allows `ConfidenceLevel == "HIGH"` to be reached.

#### 4.2 BenchmarkExcessSharpe — Wire to BenchmarkComparisonResult

In `StrategyDetail.razor`, replace:
```csharp
// Approximate excess Sharpe from available data
_benchmarkExcessSharpe = _latestRun.SharpeRatio;
```

- Load the most recent completed `StudyType.BenchmarkComparison` study result for the current strategy version from `IStudyRepository`
- Deserialise `StudyResult.ResultJson` as `BenchmarkComparisonResult`
- Set `_benchmarkExcessSharpe = benchmarkResult.ExcessSharpe`
- If no benchmark study exists, show the chip as `null` with tooltip: "Run a Benchmark Comparison study to see excess Sharpe vs Buy & Hold"

#### 4.3 PropFirm Pack Loader — Extract Service

Create `IPropFirmPackLoader` interface in `Application/PropFirm/`:
```csharp
public interface IPropFirmPackLoader
{
    Task<IReadOnlyList<PropFirmRulePack>> LoadAllPacksAsync(CancellationToken ct = default);
    Task<PropFirmRulePack?> LoadPackAsync(string firmId, CancellationToken ct = default);
}
```

Implement `JsonPropFirmPackLoader` in Infrastructure — reads from `data/firms/*.json` asynchronously.

Replace all `LoadBuiltInPacks()` calls in `StrategyDetail.razor` and `PropFirmEvaluation.razor` with `IPropFirmPackLoader` injected via DI.

Register `IPropFirmPackLoader` as a singleton in `Program.cs`.

#### 4.4 CPCV — Implement CpcvStudyHandler

`CpcvStudyHandler.cs` is currently a stub. Implement Combinatorial Purged Cross-Validation (De Prado, 2018):

**Algorithm:**
1. Given `N` paths (folds), choose `k` test folds → `C(N, k)` combinations
2. For each combination: train on remaining `N-k` folds, test on the `k` test folds
3. Collect all OOS Sharpe ratios across combinations → distribution
4. Compute:
   - `MedianOosSharpe`: median of the OOS Sharpe distribution
   - `ProbabilityOfOverfitting`: fraction of combinations where OOS Sharpe < IS Sharpe
   - `PerformanceDegradation`: 1 - (median OOS Sharpe / IS Sharpe)
5. Default: `N=6` paths, `k=2` test folds → `C(6,2) = 15` combinations

Create `CpcvOptions` record in Application:
```csharp
public sealed record CpcvOptions(
    int NumPaths = 6,        // total folds
    int TestFolds = 2,       // folds held out per combination
    int? Seed = null);
```

Create `CpcvResult` record:
```csharp
public sealed record CpcvResult(
    decimal MedianOosSharpe,
    decimal ProbabilityOfOverfitting,
    decimal PerformanceDegradation,
    IReadOnlyList<decimal> OosSharpeDistribution,
    int TotalCombinations);
```

Implement `CpcvStudyHandler : IResearchWorkflow<CpcvOptions, CpcvResult>` in Application/Research.

Add `StudyType.Cpcv` to the enum (if not already present) and wire into `BackgroundStudyService`.

Add `bool CpcvDone` to `ResearchChecklist` as the 9th item (do not remove any existing items; update `TotalChecks` to 9). Update `ConfidenceLevel` thresholds: HIGH ≥ 8, MEDIUM ≥ 5, LOW < 5.

**Unit tests** in `UnitTests/V6/CpcvTests.cs`:
- `C(6,2)` produces 15 combinations
- Single-trade result → graceful empty output
- Deterministic with same seed
- `ProbabilityOfOverfitting` = 1.0 when all OOS Sharpes are negative

#### 4.5 Timeframe-Aware MinBTL Recommendation

`MinBtlCalculator` currently returns a bar count. Update `PreflightValidator` to translate this bar count into a human-readable duration based on the detected timeframe:

- Given `minBars = 500` and timeframe `M15` → "~52 trading days of 15-minute data required"
- Show this translation in the preflight finding message and in the `Step2DataExecutionWindow.razor` diagnostics

Add `BarsToHumanDuration(int bars, string timeframe)` static helper to `BarsPerYearDefaults`.

#### 4.6 Tests — Quant Depth

- `UnitTests/V6/PropFirmChecklistTests.cs`: `HasCompletedEvaluation` = true unlocks the 8th checklist item; confidence reaches HIGH with 8/9 items
- `UnitTests/V6/CpcvTests.cs`: see Track 4.4 above
- `UnitTests/V6/BarsToHumanDurationTests.cs`: known bar counts at each timeframe produce correct human-readable strings

**Checkpoint 4**: All tests pass. `ConfidenceLevel == "HIGH"` is reachable. CPCV study can be launched from the Research tab. BenchmarkExcessSharpe chip shows real data.

---

## Architectural Rules — Read Before Writing Any Code

1. **Dependency rule**: `Core ← Application ← Infrastructure ← { Cli, Api, Web }`. No upward references. No circular references.
2. **No domain logic in Infrastructure**. `SqliteIndexRepository`, `JsonPropFirmPackLoader`, `JsonPropFirmEvaluationRepository` contain zero business rules — only I/O and mapping.
3. **Records and immutability**: all new domain types (`PropFirmEvaluationRecord`, `CpcvOptions`, `CpcvResult`, `BarsPerYearDefaults`) are `sealed record` or `static class`. No mutable public properties on domain types.
4. **Async discipline**: no `.Result` or `.Wait()`. All new async methods accept `CancellationToken ct = default` and propagate it to every `await`.
5. **No magic numbers**: all new thresholds go into named constant classes. `BarsPerYearDefaults` is the authoritative source for intraday bar counts.
6. **XML doc comments**: all `public` types and members in Core and Application carry `/// <summary>` comments.
7. **Logging**: use `ILogger<T>` injected via constructor. No `Console.WriteLine` outside `ConsoleReporter`.
8. **Package additions**: add `Microsoft.Data.Sqlite` and `Plotly.Blazor` to `tech.md` before using them.
9. **Test project boundaries**: `UnitTests` references Core and Application only. `IntegrationTests` may reference all projects. All new test files follow `<SubjectUnderTest>Tests` naming convention.
10. **Stochastic determinism**: `CpcvStudyHandler` must accept an explicit `Seed` and produce identical output when the same seed and inputs are supplied.

---

## Steering Document Locations

All steering documents are in `.kiro/steering/`:

| File | Contents |
|---|---|
| `product.md` | Version scopes (V1–V3 defined, V4+ in-repo), module boundaries |
| `tech.md` | .NET version, NuGet packages list, async/logging/records standards |
| `domain-boundaries.md` | Dependency rule, Core/Application/Infrastructure ownership, strategy registry |
| `testing-standards.md` | xUnit/FsCheck/Moq rules, required unit and property tests, naming conventions |
| `strategy-registry.md` | `[StrategyName]` attribute, `StrategyRegistry`, plugin upgrade path |
| `api-standards.md` | OpenAPI annotations, endpoint naming, versioning |
| `security-policies.md` | Security requirements (single-user local app) |
| `structure.md` | Project/folder structure |

---

## Deliverable Definition of Done

V6 is complete when:

- [ ] `Direction.Short` fills and closes positions correctly, PnL computed as `(EntryPrice - ExitPrice) × Qty`
- [ ] `LongOnlyGuard.EnsureLongOnly` is no longer called in any execution path (the class may be marked `[Obsolete]`)
- [ ] `DonchianBreakoutStrategy`, `VolatilityScaledTrendStrategy`, `ZScoreMeanReversionStrategy`, `StationaryMeanReversionStrategy` support bidirectional signals
- [ ] `BarsPerYearDefaults` covers all 8 intraday timeframes; timeframe selector in builder includes 1m–4H
- [ ] `SqliteIndexRepository<T>` is used for `BacktestResult` and `Study`; `ListByVersionAsync` is O(log n)
- [ ] `WalkForwardWorkflow` and `ParameterSweepWorkflow` execute in parallel with `SemaphoreSlim` concurrency control
- [ ] `EquityCurveChart` component renders on the Strategy Detail Overview tab
- [ ] Monte Carlo fan chart (P10/P50/P90), Walk-Forward composite chart, and Parameter Sweep heatmap all render on their respective result pages
- [ ] `MonthlyReturnsHeatmap`, `TradePnlHistogram`, `HoldingPeriodHistogram` visible in ResultDetail Charts tab
- [ ] `IPropFirmPackLoader` injected via DI; no duplicate `LoadBuiltInPacks()` calls
- [ ] PropFirm checklist item wired to `IPropFirmEvaluationRepository`; HIGH confidence reachable
- [ ] `BenchmarkExcessSharpe` chip loads from `BenchmarkComparisonResult`
- [ ] `CpcvStudyHandler` fully implemented and launchable from the Research tab
- [ ] Retired strategies hidden by default in Strategy Library with "Show Retired" toggle
- [ ] All new code has XML doc comments on public members
- [ ] All V6 unit tests in `UnitTests/V6/` pass
- [ ] All existing V1–V5 tests still pass
- [ ] `tech.md` updated with new NuGet packages (`Microsoft.Data.Sqlite`, `Plotly.Blazor`)
- [ ] `product.md` V6 scope section added at the bottom

---

## Notes for Kiro

- Work track by track. After completing each checkpoint, verify all tests pass before proceeding to the next track.
- When in doubt about an architectural decision, refer to `domain-boundaries.md`. If the answer isn't there, apply the most restrictive interpretation (keep domain logic in Application, not Infrastructure).
- The `EventQueue` and `Portfolio` are not thread-safe and must never be shared across parallel runs. Each parallel backtest run (in sweep, walk-forward, CPCV) must create its own `EventQueue` instance via the engine constructor.
- The `JsonFileRepository<T>` pattern must be preserved for backward compatibility. The SQLite layer is additive — existing JSON files are the source of truth; SQLite is an index.
- All chart components are Blazor `@rendermode InteractiveServer` — do not use static rendering for any chart.
- When implementing CPCV fold generation, use the standard combinatorics formula: `C(N, k) = N! / (k! × (N-k)!)`. For `N=6, k=2` this yields exactly 15 combinations.
