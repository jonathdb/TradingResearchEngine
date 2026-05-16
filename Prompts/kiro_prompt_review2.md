# Kiro Implementation Prompt — TradingResearchEngine Second-Pass Review

## Context

You are implementing fixes and improvements for **TradingResearchEngine**, a .NET 8 / Blazor Server / MudBlazor event-driven backtesting engine for quantitative strategy research. The repository is at `https://github.com/jonathdb/TradingResearchEngine` on the `Development` branch.

This prompt covers **23 issues** found in the second-pass code review. They are organized by priority (P0 critical → P3 polish). Implement in order from P0 to P3. For each item, read the referenced file(s) first, then implement the fix, then verify the acceptance criteria before moving to the next item.

---

## Glossary

- **RSUC** — `RunScenarioUseCase` in `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`
- **StrategyRepo** — `IStrategyRepository` and its JSON/SQLite implementation
- **RandomizedOos** — `RandomizedOosWorkflow` in `src/TradingResearchEngine.Application/Research/RandomizedOosWorkflow.cs`
- **Sweep** — `ParameterSweepWorkflow` in `src/TradingResearchEngine.Application/Research/ParameterSweepWorkflow.cs`
- **DSR** — Deflated Sharpe Ratio, computed in `RSUC.EnrichWithTrialCountAndDsrAsync`
- **Preflight** — `PreflightValidator` in `src/TradingResearchEngine.Application/Engine/PreflightValidator.cs`
- **BacktestEngine** — `BacktestEngine` in `src/TradingResearchEngine.Core/Engine/BacktestEngine.cs`
- **SealedTestSetGuard** — `src/TradingResearchEngine.Application/Engine/SealedTestSetGuard.cs`
- **CompositeStrategy** — `src/TradingResearchEngine.Application/Strategies/Composite/CompositeStrategy.cs`
- **Dashboard** — `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`
- **StudyDetail** — `src/TradingResearchEngine.Web/Components/Pages/StudyDetail.razor`

---

## P0 — Critical Bugs (Implement First)

---

### Item 1: Fix `EnrichWithTrialCountAndDsrAsync` Full Table-Scan

**File to read first:** `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`

**Problem:** `EnrichWithTrialCountAndDsrAsync` calls `strategyRepo.ListAsync()` followed by `GetVersionsAsync()` in a nested loop to find a single version by ID. This is an O(N×M) operation executed on every completed backtest, including inside parallel sweeps where it runs N-combinations times concurrently.

**Implementation:**

1. Add a new method to `IStrategyRepository`:
   ```csharp
   Task<StrategyVersion?> GetVersionByIdAsync(Guid versionId, CancellationToken ct = default);
   ```

2. Implement it in the concrete repository to query directly by `StrategyVersionId` without loading all strategies.

3. Replace the nested loop in `EnrichWithTrialCountAndDsrAsync` with a single call:
   ```csharp
   var version = await strategyRepo.GetVersionByIdAsync(result.StrategyVersionId.Value, ct);
   if (version is null) return result;
   ```

**Acceptance Criteria:**
- `IStrategyRepository` exposes `GetVersionByIdAsync(Guid, CancellationToken)`.
- `EnrichWithTrialCountAndDsrAsync` contains no nested `foreach` loop over all strategies and versions.
- The method performs at most 2 repository calls per invocation (one read, one write).
- A 500-combination sweep no longer triggers ListAsync on the strategy repository.

---

### Item 2: Fix `TotalTrialsRun` Write Race in Parallel Sweeps

**File to read first:** `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`

**Problem:** The read-modify-write pattern on `TotalTrialsRun` inside the parallel hot path causes lost updates. All workers read the same value, increment it locally, and overwrite each other.

```csharp
// BUGGY — all 500 workers read TotalTrialsRun=5, all write 6
var updatedVersion = version with { TotalTrialsRun = version.TotalTrialsRun + 1 };
await strategyRepo.SaveVersionAsync(updatedVersion, ct);
```

**Implementation:**

1. Add a dedicated atomic increment method to `IStrategyRepository`:
   ```csharp
   Task IncrementTrialCountAsync(Guid versionId, CancellationToken ct = default);
   ```

2. In the SQLite implementation, issue an atomic SQL update:
   ```sql
   UPDATE strategy_versions SET TotalTrialsRun = TotalTrialsRun + 1 WHERE StrategyVersionId = @id
   ```
   or the equivalent JSON-patch update if the repository is file-backed.

3. Replace the increment block in `EnrichWithTrialCountAndDsrAsync`:
   ```csharp
   if (shouldIncrement)
       await strategyRepo.IncrementTrialCountAsync(result.StrategyVersionId.Value, ct);
   // Re-read the updated version to get the correct TotalTrialsRun for DSR
   version = await strategyRepo.GetVersionByIdAsync(result.StrategyVersionId.Value, ct) ?? version;
   ```

**Acceptance Criteria:**
- `IStrategyRepository` exposes `IncrementTrialCountAsync(Guid, CancellationToken)`.
- Running a 200-combination sweep against a single `StrategyVersionId` results in `TotalTrialsRun` being exactly 200 higher after the sweep completes.
- No manual `version with { TotalTrialsRun = version.TotalTrialsRun + 1 }` pattern remains in `RSUC`.

---

### Item 3: Fix `RandomizedOosWorkflow` — Non-Contiguous OOS Bars Break Indicator Warmup

**File to read first:** `src/TradingResearchEngine.Application/Research/RandomizedOosWorkflow.cs`

**Problem:** OOS bar indices are randomly scattered across the full timeline. When fed to the engine in timestamp order they arrive without preceding context, so any strategy with a multi-period indicator (e.g., SMA-200) never warms up correctly. The OOS Sharpe measurements are invalid for any strategy with a warmup period > ~10 bars.

**Implementation:**

1. Replace the scattered-index approach with **random contiguous blocks**:
   ```csharp
   // Pick a random OOS start index leaving room for a full OOS window
   int maxOosStart = allBars.Count - oosCount - warmupBuffer;
   if (maxOosStart < 1)
       throw new InvalidOperationException(
           $"Insufficient data for OOS fraction {options.OosFraction} with warmup {warmupBuffer} bars.");

   int oosStart = rng.Next(0, maxOosStart + 1);
   var oosBars = allBars.Skip(oosStart).Take(oosCount).ToList();
   var isBars  = allBars.Take(oosStart)
                        .Concat(allBars.Skip(oosStart + oosCount))
                        .ToList();
   ```

2. Add a `WarmupBars` property to `RandomizedOosOptions` (default `200`):
   ```csharp
   /// <summary>
   /// Number of bars prepended to the OOS window as indicator warmup context.
   /// These bars are fed to the engine but not counted in OOS performance measurement.
   /// Default is 200 to accommodate common long-period indicators.
   /// </summary>
   public int WarmupBars { get; set; } = 200;
   ```

3. When building the `oosConfig`, prepend `WarmupBars` bars from before `oosStart` to the OOS bar list but mark them as warmup-only (pass via `DataProviderOptions["WarmupBars"] = warmupBuffer`).

4. Validate that `allBars.Count >= oosCount + WarmupBars + 10` and throw `ArgumentException` with a clear message if not.

**Acceptance Criteria:**
- OOS bars form a contiguous time block, not scattered indices.
- `RandomizedOosOptions.WarmupBars` defaults to `200`.
- Insufficient-data scenarios throw `InvalidOperationException` with a descriptive message before any engine run.
- The scattered-index `HashSet<int>` approach is fully removed.
- Running `RandomizedOosWorkflow` on a strategy with a 50-bar SMA produces non-null OOS Sharpe values.

---

### Item 4: Fix `IRiskLayer`/`IExecutionHandler` Singleton State in Parallel Runs

**File to read first:** `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`

**Problem:** `IRiskLayer` and `IExecutionHandler` are resolved from the root `IServiceProvider`. If they are registered as `Singleton` or `Scoped`, all parallel sweep workers share the same stateful instance. `SimulatedExecutionHandler.RealismAdvisories` is clearly a mutable list that will receive writes from all workers simultaneously.

**Implementation:**

1. Confirm the DI registration lifetimes of `IRiskLayer`, `IExecutionHandler`, and `SimulatedExecutionHandler` in `Program.cs` / the DI composition root. If any is `Singleton`, change it to `Transient`.

2. Wrap per-run service resolution in an `IServiceScope` inside `RunScenarioUseCase.RunAsync`:
   ```csharp
   using var scope = _services.CreateScope();
   var scopedServices = scope.ServiceProvider;
   var riskLayer         = scopedServices.GetRequiredService<IRiskLayer>();
   var executionHandler  = scopedServices.GetRequiredService<IExecutionHandler>();
   ```

3. Ensure `BacktestEngine` is constructed from `scopedServices` instead of `_services`.

4. Verify `SimulatedExecutionHandler.RealismAdvisories` is an instance field (not static).

**Acceptance Criteria:**
- Every call to `RunAsync` uses a fresh `IServiceScope`.
- `SimulatedExecutionHandler.RealismAdvisories` on one run never contains advisories from another run.
- Running 50 concurrent backtest runs via a sweep produces 50 independent `RealismAdvisories` collections.
- No `Singleton` registration exists for `IRiskLayer` or `IExecutionHandler`.

---

## P1 — Architecture & Correctness

---

### Item 5: Memoize `CreateStrategy` Reflection

**File to read first:** `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`

**Problem:** `CreateStrategy` calls `GetConstructors()` and executes full parameter-matching reflection on every invocation, including 500+ times inside a sweep. Reflection is expensive and allocates heavily.

**Implementation:**

1. Add a `ConcurrentDictionary<Type, Func<Dictionary<string, object>, IStrategy>>` factory-delegate cache as a static or instance field on `RunScenarioUseCase`.

2. On first access for a given `Type`, compile a `Func<Dictionary<string, object>, IStrategy>` using `Expression.New` or `Activator.CreateInstance` with cached `ConstructorInfo`.

3. On subsequent access, invoke the cached delegate directly, bypassing reflection entirely.

```csharp
private static readonly ConcurrentDictionary<Type, Func<Dictionary<string, object>, IStrategy>>
    _strategyFactoryCache = new();

private IStrategy CreateStrategy(Type strategyType, Dictionary<string, object> parameters)
{
    var factory = _strategyFactoryCache.GetOrAdd(strategyType, BuildFactory);
    return factory(parameters);
}

private static Func<Dictionary<string, object>, IStrategy> BuildFactory(Type type)
{
    // Compile and cache the constructor invocation once
    var ctor = type.GetConstructors()
        .OrderByDescending(c => c.GetParameters().Length)
        .First();
    // ... compiled delegate or expression tree
}
```

**Acceptance Criteria:**
- `GetConstructors()` is called at most once per unique `strategyType` across the application lifetime.
- A 500-combination sweep on the same strategy type results in exactly 1 constructor reflection scan.
- All existing strategy construction behaviour (parameter matching, defaults, fallbacks) is preserved.

---

### Item 6: Fix `ConvertJsonElement` Silent Fallback for Unhandled Types

**File to read first:** `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`

**Problem:** The fallback `return je.ToString()` returns raw JSON text for unhandled types (enums, `TimeSpan`, `DateTimeOffset`, `Guid`). `Convert.ChangeType` then throws, the constructor match silently fails, and `ActivatorUtilities.CreateInstance` is invoked — producing a confusing runtime error.

**Implementation:**

Extend `ConvertJsonElement` with explicit handling for common types:

```csharp
private static object? ConvertJsonElement(JsonElement je, Type targetType)
{
    // Unwrap Nullable<T>
    var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

    if (underlying == typeof(int))          return je.GetInt32();
    if (underlying == typeof(long))         return je.GetInt64();
    if (underlying == typeof(decimal))      return je.GetDecimal();
    if (underlying == typeof(double))       return je.GetDouble();
    if (underlying == typeof(float))        return je.GetSingle();
    if (underlying == typeof(bool))         return je.ValueKind == JsonValueKind.True;
    if (underlying == typeof(string))       return je.GetString();
    if (underlying == typeof(Guid))         return je.GetGuid();
    if (underlying == typeof(DateTimeOffset)) return je.GetDateTimeOffset();
    if (underlying == typeof(DateTime))     return je.GetDateTime();
    if (underlying == typeof(TimeSpan) && je.ValueKind == JsonValueKind.String)
        return TimeSpan.Parse(je.GetString()!);
    if (underlying.IsEnum && je.ValueKind == JsonValueKind.String)
        return Enum.Parse(underlying, je.GetString()!, ignoreCase: true);
    if (underlying.IsEnum && je.ValueKind == JsonValueKind.Number)
        return Enum.ToObject(underlying, je.GetInt32());

    // Unhandled type — emit a structured warning instead of silent fallback
    throw new NotSupportedException(
        $"Cannot convert JsonElement of kind {je.ValueKind} to {targetType.Name}. " +
        "Add an explicit conversion in ConvertJsonElement.");
}
```

Catch `NotSupportedException` in the constructor-matching loop and add the type name to the preflight error list rather than silently falling through.

**Acceptance Criteria:**
- `Enum`, `TimeSpan`, `DateTimeOffset`, `Guid`, and `Nullable<T>` parameters are correctly converted.
- When an unhandled type is encountered, a `PreflightSeverity.Error` message is surfaced to the user instead of a silent fallback.
- All existing supported types continue to convert correctly.

---

### Item 7: Fix DSR Return Moments — Use Trade Returns, Not Equity Curve Bars

**File to read first:** `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`

**Problem:** `ComputeReturnMoments` uses bar-by-bar equity curve returns. For a strategy that is flat 90% of the time, this series is dominated by zeros, artificially compressing skewness and kurtosis toward a Gaussian and understating the DSR overfitting correction.

**Implementation:**

1. Modify `ComputeReturnMoments` to prefer trade P&L as the return series:
   ```csharp
   private static (decimal Skewness, decimal Kurtosis) ComputeReturnMoments(BacktestResult result)
   {
       List<double> returns;

       // Prefer trade-level returns for DSR (Bailey & López de Prado use the same series as Sharpe)
       if (result.Trades is { Count: >= 3 })
       {
           returns = result.Trades
               .Where(t => t.EntryPrice > 0)
               .Select(t => (double)((t.PnL) / (t.EntryPrice * t.Quantity)))
               .ToList();
       }
       else
       {
           // Fallback to equity curve bar returns
           returns = new List<double>();
           for (int i = 1; i < result.EquityCurve.Count; i++)
           {
               var prev = (double)result.EquityCurve[i - 1].TotalEquity;
               var curr = (double)result.EquityCurve[i].TotalEquity;
               if (prev > 0) returns.Add(curr / prev - 1.0);
           }
       }

       if (returns.Count < 3) return (0m, 0m);
       // ... existing skew/kurt math unchanged
   }
   ```

2. Ensure `BacktestResult.Trades` is available at the point `EnrichWithTrialCountAndDsrAsync` is called (it should be — the engine populates trades before returning the result).

**Acceptance Criteria:**
- When `result.Trades.Count >= 3`, DSR skewness and kurtosis are derived from trade P&L returns.
- When `result.Trades` is empty or null, the equity curve fallback is used.
- A strategy with infrequent trades (e.g., 20 trades/year) produces a materially different DSR than when equity-curve bar returns are used.

---

### Item 8: Fix Shallow Dictionary Copy in Parallel `with`-Clone Pattern

**Files to read first:**
- `src/TradingResearchEngine.Application/Research/ParameterSweepWorkflow.cs`
- `src/TradingResearchEngine.Application/Research/RandomizedOosWorkflow.cs`
- Any other workflow using `baseConfig with { ... }`

**Problem:** `ScenarioConfig with { DataProviderOptions = ... }` creates a new `ScenarioConfig` but all `Dictionary<string, object>` properties (including any not reassigned) are shallow-copied. If a workflow mutates a dictionary key inside a parallel worker, it corrupts other workers' configs.

**Implementation:**

1. Add a `DeepClone()` extension method for `ScenarioConfig`:
   ```csharp
   public static ScenarioConfig DeepClone(this ScenarioConfig config) => config with
   {
       DataProviderOptions      = new Dictionary<string, object>(config.DataProviderOptions),
       StrategyParameters       = new Dictionary<string, object>(config.StrategyParameters),
       ResearchWorkflowOptions  = config.ResearchWorkflowOptions is not null
           ? new Dictionary<string, object>(config.ResearchWorkflowOptions)
           : null,
       ExecutionOptions         = config.ExecutionOptions is null ? null : config.ExecutionOptions with { },
   };
   ```

2. Replace all `baseConfig with { ... }` mutation patterns in parallel workflows with `baseConfig.DeepClone() with { ... }`.

3. Search the entire codebase for `baseConfig with {` and `config with {` patterns used inside `Parallel.ForEachAsync` bodies and apply the same fix.

**Acceptance Criteria:**
- `ScenarioConfig.DeepClone()` exists and creates independent copies of all dictionary properties.
- Mutating `DataProviderOptions` on one parallel-sweep worker's config does not affect any other worker's config.
- All workflow `with`-clone sites inside parallel bodies use `DeepClone()`.

---

### Item 9: Make `BacktestEngine` Injectable via Interface

**Files to read first:**
- `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`
- `src/TradingResearchEngine.Core/Engine/BacktestEngine.cs`

**Problem:** `BacktestEngine` is instantiated with `new` inside `RunScenarioUseCase.RunAsync`, making it impossible to mock in unit tests and preventing the engine from receiving injected `IProgress<ProgressUpdate>` transparently.

**Implementation:**

1. In `TradingResearchEngine.Core`, define:
   ```csharp
   public interface IBacktestEngine
   {
       Task<BacktestResult> RunAsync(ScenarioConfig config, IProgress<ProgressUpdate>? progress = null, CancellationToken ct = default);
   }
   ```

2. Have `BacktestEngine : IBacktestEngine`.

3. Add `IBacktestEngineFactory` to Core:
   ```csharp
   public interface IBacktestEngineFactory
   {
       IBacktestEngine Create(
           IDataProvider dataProvider,
           IStrategy strategy,
           IRiskLayer riskLayer,
           IExecutionHandler executionHandler,
           ISessionCalendar? sessionCalendar = null,
           BarDataPool? barDataPool = null);
   }
   ```

4. Register `BacktestEngineFactory : IBacktestEngineFactory` in DI (Transient).

5. Inject `IBacktestEngineFactory` into `RunScenarioUseCase` and replace the `new BacktestEngine(...)` call with `_engineFactory.Create(...)`.

**Acceptance Criteria:**
- `RunScenarioUseCase` contains no `new BacktestEngine(...)` call.
- `IBacktestEngine` and `IBacktestEngineFactory` are defined in `TradingResearchEngine.Core`.
- Unit tests can substitute a mock `IBacktestEngine` without instantiating the real engine.
- All existing backtest behaviour is preserved end-to-end.

---

### Item 10: Fix `RandomizedOosResult` Silent Failure Absorption

**File to read first:** `src/TradingResearchEngine.Application/Research/RandomizedOosWorkflow.cs`

**Problem:** Failed iterations are silently skipped. `MeanOosSharpe` is computed over only the succeeding subset with no indication that iterations failed.

**Implementation:**

1. Add a `FailedIterationCount` property to `RandomizedOosResult`:
   ```csharp
   public sealed record RandomizedOosResult(
       IReadOnlyList<RandomizedOosIteration> Iterations,
       decimal MeanOosSharpe,
       decimal StdDevOosSharpe,
       decimal MeanIsSharpe,
       int FailedIterationCount);   // NEW
   ```

2. Track failed iterations in the loop:
   ```csharp
   int failedCount = 0;
   // ... existing loop
   if (isResult.IsSuccess && ...)
       iterations.Add(...);
   else
       Interlocked.Increment(ref failedCount);
   ```

3. Add a realism advisory when failure rate > 20%:
   ```csharp
   if (failedCount > 0 && (double)failedCount / options.Iterations > 0.20)
   {
       // Emit a warning visible in the result renderer
       // Use the same RealismAdvisory pattern as SimulatedExecutionHandler
   }
   ```

4. Include `failedCount` in the returned `RandomizedOosResult`.

**Acceptance Criteria:**
- `RandomizedOosResult.FailedIterationCount` reflects the actual number of failed iterations.
- When > 20% of iterations fail, a warning advisory is attached to the result.
- `MeanOosSharpe` denominator is the succeeded count, clearly documented in the XML comment.

---

### Item 11: Enforce Explicit Sort in `ParameterSweepWorkflow`

**File to read first:** `src/TradingResearchEngine.Application/Research/ParameterSweepWorkflow.cs`

**Problem:** `ConcurrentBag<T>` enumeration order is non-deterministic. The comment says results are "ranked by Sharpe ratio descending" but no explicit sort is applied before constructing `SweepResult`.

**Implementation:**

1. Add `SweepSortMetric` to `SweepOptions`:
   ```csharp
   public enum SweepSortMetric { SharpeRatio, MaxDrawdown, ProfitFactor, WinRate, CalmarRatio }

   public sealed class SweepOptions
   {
       // ... existing properties
       public SweepSortMetric SortBy { get; set; } = SweepSortMetric.SharpeRatio;
   }
   ```

2. After collecting all results from the `ConcurrentBag`, apply an explicit sort:
   ```csharp
   var sortedResults = results.ToList();
   sortedResults = options.SortBy switch
   {
       SweepSortMetric.SharpeRatio   => sortedResults.OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue).ToList(),
       SweepSortMetric.MaxDrawdown   => sortedResults.OrderBy(r => r.MaxDrawdownPercent ?? decimal.MaxValue).ToList(),
       SweepSortMetric.ProfitFactor  => sortedResults.OrderByDescending(r => r.ProfitFactor ?? decimal.MinValue).ToList(),
       SweepSortMetric.WinRate       => sortedResults.OrderByDescending(r => r.WinRate ?? decimal.MinValue).ToList(),
       SweepSortMetric.CalmarRatio   => sortedResults.OrderByDescending(r => r.CalmarRatio ?? decimal.MinValue).ToList(),
       _                             => sortedResults.OrderByDescending(r => r.SharpeRatio ?? decimal.MinValue).ToList()
   };
   ```

3. Wire `SweepSortMetric` to the UI heatmap selector so the selected metric drives both the sort and the heatmap colouring.

**Acceptance Criteria:**
- `SweepOptions.SortBy` property exists with `SweepSortMetric` enum.
- `SweepResult` ranked order is deterministic and matches the selected sort metric.
- The UI heatmap metric selector drives `SweepOptions.SortBy`.
- Sorting by `MaxDrawdown` returns results with the smallest drawdown first.

---

### Item 12: Add `BarsPerYear` ↔ `Interval` Preflight Consistency Check

**File to read first:** `src/TradingResearchEngine.Application/Engine/PreflightValidator.cs`

**Problem:** A user setting `BarsPerYear=252` (daily) with `Interval="1H"` produces silently wrong Sharpe, Calmar, and DSR values. This is one of the most common misconfiguration mistakes.

**Implementation:**

1. Add a static lookup of expected `BarsPerYear` ranges per interval to `PreflightValidator`:
   ```csharp
   private static readonly Dictionary<string, (int Min, int Max)> BarsPerYearByInterval = new(StringComparer.OrdinalIgnoreCase)
   {
       ["1D"]  = (200,  260),
       ["1H"]  = (1500, 2000),
       ["4H"]  = (500,  700),
       ["30m"] = (3000, 4500),
       ["15m"] = (6000, 9000),
       ["5m"]  = (18000, 28000),
       ["1m"]  = (80000, 140000),
   };
   ```

2. In the validator, if the resolved interval is in the lookup and `BarsPerYear` falls outside the expected range, add a `PreflightSeverity.Warning`:
   ```csharp
   // In Validate(ScenarioConfig config):
   if (BarsPerYearByInterval.TryGetValue(interval, out var range) &&
       (config.BarsPerYear < range.Min || config.BarsPerYear > range.Max))
   {
       findings.Add(new PreflightFinding(
           PreflightSeverity.Warning,
           $"BarsPerYear={config.BarsPerYear} appears inconsistent with Interval='{interval}'. " +
           $"Expected range: {range.Min}–{range.Max}. Metrics may be annualised incorrectly."));
   }
   ```

**Acceptance Criteria:**
- Setting `BarsPerYear=252` with `Interval="1H"` produces a `PreflightSeverity.Warning` visible in the builder.
- Setting `BarsPerYear=252` with `Interval="1D"` produces no warning.
- Unknown/custom intervals (e.g., `"tick"`) are silently skipped by the check.

---

### Item 13: Add Audit Log to `SealedTestSetGuard`

**File to read first:** `src/TradingResearchEngine.Application/Engine/SealedTestSetGuard.cs`

**Problem:** When `ResearchPhase.FinalTest` is unlocked, there is no record of when it happened or under what conditions. In a research workflow this accountability gap allows the test set to be "peeked" and re-sealed without any evidence.

**Implementation:**

1. Define `TestSetAuditEntry`:
   ```csharp
   public sealed record TestSetAuditEntry(
       Guid StrategyVersionId,
       DateTimeOffset UnlockedAt,
       string? Reason);
   ```

2. Add a `ITestSetAuditLog` interface and a JSON-file-backed implementation:
   ```csharp
   public interface ITestSetAuditLog
   {
       Task RecordUnlockAsync(Guid versionId, string? reason, CancellationToken ct = default);
       Task<IReadOnlyList<TestSetAuditEntry>> GetEntriesAsync(Guid versionId, CancellationToken ct = default);
   }
   ```

3. Inject `ITestSetAuditLog` into `SealedTestSetGuard` and call `RecordUnlockAsync` when the phase transitions to `FinalTest`.

4. Add a read-only "Test Set Audit" section to the `StrategyDetail` page showing the unlock history.

**Acceptance Criteria:**
- Every transition to `ResearchPhase.FinalTest` is recorded with a timestamp.
- `ITestSetAuditLog.GetEntriesAsync(versionId)` returns the full unlock history for a strategy version.
- The `StrategyDetail` page displays the audit log if any entries exist.
- Re-sealing (moving back from `FinalTest`) also records an entry.

---

## P2 — Research Features & Improvements

---

### Item 14: Add IS/OOS Efficiency Ratio Distribution Chart to Randomized OOS Renderer

**File to read first:** `src/TradingResearchEngine.Web/Components/Pages/StudyDetail.razor`

**Problem:** `RandomizedOosResult.Iterations` contains `EfficiencyRatio` per iteration but the result renderer shows only summary KPIs. A histogram of efficiency ratios is the most diagnostic view for this study type.

**Implementation:**

1. Add a `RandomizedOosResultRenderer.razor` component (or extend the existing renderer) that renders:
   - A histogram of `EfficiencyRatio` values across iterations (binned, 10 bins)
   - A vertical reference line at `EfficiencyRatio = 1.0`
   - Summary stats: mean efficiency, % of iterations with ratio ≥ 0.5, failed iteration count
   - A colour-coded badge: green if mean efficiency ≥ 0.7, amber if 0.4–0.7, red if < 0.4

2. Register the renderer in `StudyRendererRegistry` for `StudyType.RandomizedOos`.

3. Use the existing charting library (MudBlazor charts or ApexCharts if available) for the histogram.

**Acceptance Criteria:**
- The Randomized OOS study page shows an efficiency ratio histogram.
- The histogram has a reference line at 1.0.
- `FailedIterationCount` is displayed with a warning badge if > 0.
- The colour-coded badge correctly reflects the mean efficiency ratio.

---

### Item 15: Add Buy-and-Hold Benchmark Overlay to Equity Curve Chart

**Files to read first:**
- `src/TradingResearchEngine.Web/Components/Pages/StudyDetail.razor`
- `src/TradingResearchEngine.Core/Results/BacktestResult.cs`

**Problem:** The equity curve chart shows strategy performance in isolation. Without a buy-and-hold baseline it is impossible to determine visually whether alpha is real or market beta.

**Implementation:**

1. Add `BenchmarkEquityCurve` to `BacktestResult`:
   ```csharp
   public IReadOnlyList<EquityPoint>? BenchmarkEquityCurve { get; init; }
   ```

2. In `RunScenarioUseCase.RunAsync`, after the engine run, compute the benchmark:
   ```csharp
   // Fetch closing prices for the same date range and compute buy-and-hold equity
   var benchmarkCurve = await ComputeBenchmarkAsync(config, result.EquityCurve, ct);
   result = result with { BenchmarkEquityCurve = benchmarkCurve };
   ```

3. In `ComputeBenchmarkAsync`, load the same bars from `IDataProvider`, normalise to `InitialCash`, and produce an `EquityPoint` list aligned to the strategy's equity curve timestamps.

4. In the equity curve chart component, render `BenchmarkEquityCurve` as a secondary line in a muted colour (e.g., `--color-text-faint`) labelled "Buy & Hold".

**Acceptance Criteria:**
- `BacktestResult.BenchmarkEquityCurve` is populated for all completed backtests.
- The equity curve chart renders two lines: strategy and buy-and-hold.
- The benchmark line starts at the same `InitialCash` value as the strategy.
- When the data provider cannot provide benchmark data, `BenchmarkEquityCurve` is `null` and the chart renders with a single line (no crash).

---

### Item 16: Add Strategy Comparison View

**Files to read first:**
- `src/TradingResearchEngine.Web/Components/Pages/Dashboard.razor`
- `src/TradingResearchEngine.Core/Results/BacktestResult.cs`

**Problem:** There is no way to compare two or more backtest runs side by side. This is a fundamental research workflow.

**Implementation:**

1. Create `src/TradingResearchEngine.Web/Components/Pages/CompareRuns.razor`:
   - Accepts query param `?runIds=id1,id2,...` (up to 5 run IDs)
   - Loads each `BacktestResult` from the repository
   - Renders an overlaid equity curve chart (each run a distinct colour)
   - Renders a metrics comparison table (Sharpe, Sortino, MaxDD, WinRate, ProfitFactor, Calmar, DSR)
   - Highlights the best value in each metric row with a subtle green background

2. On the `Dashboard.razor` run table, add checkboxes to each row and a "Compare Selected" button that navigates to `/compare-runs?runIds=...`.

3. Add a "Compare with current" button to `StudyDetail.razor` that pre-selects the current run and opens the comparison view.

**Acceptance Criteria:**
- `/compare-runs?runIds=a,b` loads and displays two runs side by side.
- Equity curves for all selected runs appear on the same chart, each with a distinct colour and legend label.
- The metrics table highlights the best value per row.
- Selecting more than 5 runs shows an error: "Select up to 5 runs to compare."
- The "Compare Selected" button on the dashboard is only enabled when 2–5 rows are checked.

---

### Item 17: Add CSV/JSON Export for Trade Log and Equity Curve

**Files to read first:**
- `src/TradingResearchEngine.Web/Components/Pages/StudyDetail.razor`
- `src/TradingResearchEngine.Core/Results/BacktestResult.cs`

**Problem:** Researchers cannot export backtest data for external analysis in Python, R, or Excel.

**Implementation:**

1. Add an `ExportService` to the Web layer:
   ```csharp
   public sealed class ExportService
   {
       public string TradesToCsv(IReadOnlyList<TradeRecord> trades);
       public string EquityCurveToCsv(IReadOnlyList<EquityPoint> curve);
       public string ResultToJson(BacktestResult result);
   }
   ```

2. In `StudyDetail.razor` and the backtest result page, add an "Export" dropdown button with three options:
   - "Trades as CSV" — downloads `trades_{runId}.csv`
   - "Equity Curve as CSV" — downloads `equity_{runId}.csv`
   - "Full Result as JSON" — downloads `result_{runId}.json`

3. Use Blazor's JS interop `downloadFileFromStream` pattern (or `IJSRuntime.InvokeVoidAsync("downloadBlob", ...)`) to trigger browser downloads.

4. Trade CSV columns: `EntryTime, ExitTime, Direction, EntryPrice, ExitPrice, Quantity, PnL, PnLPct, Commission, RunningEquity`.

5. Equity curve CSV columns: `Timestamp, TotalEquity, CashBalance, OpenEquity, DrawdownPct`.

**Acceptance Criteria:**
- Clicking "Trades as CSV" downloads a valid UTF-8 CSV file with the correct columns.
- Clicking "Equity Curve as CSV" downloads a valid CSV with correct columns.
- Clicking "Full Result as JSON" downloads the full serialised `BacktestResult`.
- Export buttons are disabled when the result has no trades or an empty equity curve.
- File names include the `RunId` for traceability.

---

### Item 18: Add Composite Strategy Tree Visualiser

**Files to read first:**
- `src/TradingResearchEngine.Application/Strategies/Composite/CompositeStrategy.cs`
- `src/TradingResearchEngine.Application/Strategies/Composite/CompositeStrategyConfig.cs`

**Problem:** A `CompositeStrategy` combining multiple sub-strategies is opaque in the UI. There is no visual representation of the combination logic.

**Implementation:**

1. Create `CompositeTreeView.razor` in the Web components library:
   - Accepts `CompositeStrategyConfig` as a parameter
   - Renders a tree structure showing sub-strategy names, weights, and the combination operator (AND / OR / Majority)
   - Each node shows strategy type, parameter count, and direction

2. Display `CompositeTreeView` in:
   - The `StrategyDetail` page when `StrategyType == "composite"`
   - Step 2 of the builder wizard when the user selects "composite" mode

3. Use MudBlazor `MudTreeView` or a custom nested `div` layout with connecting lines drawn in CSS.

**Acceptance Criteria:**
- A composite of 3 sub-strategies renders a tree with 3 leaf nodes.
- Each leaf node shows the sub-strategy type and key parameters.
- The combination operator (AND/OR/Majority) is displayed at the root node.
- The tree renders correctly for at least 2 levels of nesting (composite-of-composites).

---

## P3 — Polish & UX

---

### Item 19: Add Keyboard Shortcut System

**Problem:** Power users have no keyboard navigation shortcuts for common actions.

**Implementation:**

1. Create `KeyboardShortcutService` in the Web layer:
   ```csharp
   public sealed class KeyboardShortcutService
   {
       public void Register(string key, string description, Func<Task> handler);
       public void Unregister(string key);
       public IReadOnlyList<ShortcutDefinition> GetAll();
   }
   ```

2. Register global shortcuts in `MainLayout.razor` via JS interop listening to `document.keydown`:
   - `N` — New strategy (navigate to `/strategy-builder`)
   - `D` — Dashboard (navigate to `/`)
   - `?` — Toggle shortcut help overlay
   - `Esc` — Close open dialogs/panels

3. Register context shortcuts in page components (auto-unregistered on `Dispose`):
   - Builder: `1`–`5` navigate wizard steps, `Ctrl+Enter` runs the backtest
   - StudyDetail: `E` exports current result

4. Render a `ShortcutHelpOverlay.razor` triggered by `?` showing all registered shortcuts in a modal.

**Acceptance Criteria:**
- Pressing `N` from any page navigates to the strategy builder.
- Pressing `?` opens the shortcut help overlay.
- Pressing `Esc` closes the overlay.
- Shortcuts are not triggered when focus is inside a text input.
- The help overlay lists all registered shortcuts with descriptions.

---

### Item 20: Add Pre-Launch Study Cost Estimator

**Problem:** Users have no forewarning of how long a Monte Carlo or CPCV study will take before clicking Run.

**Implementation:**

1. Create `StudyCostEstimatorService`:
   ```csharp
   public sealed class StudyCostEstimatorService
   {
       /// <summary>
       /// Returns an estimated run count and wall time for a study,
       /// using the most recent single-run benchmark duration from the result repository.
       /// </summary>
       Task<StudyCostEstimate> EstimateAsync(StudyConfig studyConfig, ScenarioConfig scenarioConfig, CancellationToken ct = default);
   }

   public sealed record StudyCostEstimate(int EstimatedRunCount, TimeSpan EstimatedWallTime, string HumanReadableSummary);
   ```

2. Before the "Launch Study" button in the study launcher, display the estimate as a callout:
   - "~1,000 engine runs · estimated 3–5 min based on your last backtest duration"
   - Show a ⚠️ badge if estimated time > 10 minutes

3. Calculate `EstimatedRunCount` from study type:
   - MonteCarlo: `Iterations`
   - CPCV: `C(n_splits, n_test_splits)` combinations
   - WalkForward: number of windows
   - Sweep: `CartesianProduct.Count`

4. Estimate wall time using the most recent `BacktestResult.Duration` as the per-run baseline, multiplied by `EstimatedRunCount / MaxConcurrency`.

**Acceptance Criteria:**
- The cost estimator callout appears for Monte Carlo, CPCV, WalkForward, and Sweep study types.
- Estimated run count is correct for each study type.
- If no previous backtest duration is available, the estimator shows "Duration unknown — no previous runs found."
- Studies with estimated time > 10 min show a yellow warning badge.

---

### Item 21: Add `StdDev` Double-Arithmetic Fix in `RandomizedOosWorkflow`

**File to read first:** `src/TradingResearchEngine.Application/Research/RandomizedOosWorkflow.cs`

**Problem:** `StdDev` uses `decimal` arithmetic for variance, which can lose precision for very small Sharpe ratios and requires a `double` cast for `Math.Sqrt` anyway.

**Implementation:**

Replace the `decimal` `StdDev` method with a `double`-native version:

```csharp
private static decimal StdDev(List<decimal> values)
{
    if (values.Count < 2) return 0m;
    var doubles = values.Select(v => (double)v).ToList();
    double mean = doubles.Average();
    double variance = doubles.Sum(v => (v - mean) * (v - mean)) / (doubles.Count - 1);
    return (decimal)Math.Sqrt(variance);
}
```

**Acceptance Criteria:**
- `StdDev` performs variance calculation using `double` arithmetic.
- For a list of very small values (e.g., `[0.001m, 0.0015m, 0.0008m]`) the result is numerically correct.
- No behavioural change for normal-range Sharpe ratios.

---

### Item 22: Delete Obsolete `Validate` Method in `RunScenarioUseCase`

**File to read first:** `src/TradingResearchEngine.Application/Engine/RunScenarioUseCase.cs`

**Problem:** The `[Obsolete]` `private static List<string> Validate(...)` method is dead code that confuses readers and may mask test coverage gaps.

**Implementation:**

1. Search the entire solution for all call sites of the old `Validate` method (including test projects).
2. If any tests call it, update them to use `PreflightValidator.Validate` instead.
3. Delete the obsolete `Validate` method entirely.
4. Remove the `[Obsolete]` XML comment block referencing it.

**Acceptance Criteria:**
- `RunScenarioUseCase` contains no `Obsolete`-marked `Validate` method.
- All test coverage previously exercising the old method now covers `PreflightValidator.Validate`.
- The solution compiles with zero new warnings.

---

### Item 23: Add Multi-Symbol / Portfolio Data Provider Foundation

**Problem:** All workflows assume a single `Symbol` in `DataProviderOptions`. Portfolio-level research (pairs trading, sector rotation) requires multiple correlated symbols.

**Implementation (foundation layer only — not full portfolio engine):**

1. Define `IMultiSymbolDataProvider` in Core:
   ```csharp
   public interface IMultiSymbolDataProvider
   {
       /// <summary>
       /// Streams synchronized bar events across multiple symbols.
       /// Each event contains one bar per symbol, aligned to the same timestamp.
       /// </summary>
       IAsyncEnumerable<MultiSymbolBarEvent> GetSynchronizedBars(
           IReadOnlyList<string> symbols,
           string interval,
           DateTimeOffset from,
           DateTimeOffset to,
           CancellationToken ct = default);
   }

   public sealed record MultiSymbolBarEvent(
       DateTimeOffset Timestamp,
       IReadOnlyDictionary<string, BarRecord> Bars);
   ```

2. Implement `SynchronizedDataProvider : IMultiSymbolDataProvider` in the Application layer:
   - Fetches each symbol's bars from `IDataProvider` in parallel
   - Merges streams by timestamp using a priority queue
   - Emits `MultiSymbolBarEvent` only when all requested symbols have a bar for that timestamp (inner-join behaviour)
   - Emits `MultiSymbolBarEvent` with available symbols when configured for outer-join (partial fills)

3. Register `IMultiSymbolDataProvider` in DI.

4. Add `MultiSymbolMode` to `ScenarioConfig` (optional, default false) and `Symbols` as an alternative to `Symbol` in `DataProviderOptions`.

5. This item does **not** require a full multi-asset `BacktestEngine` — it only establishes the data layer contract and a working `SynchronizedDataProvider`.

**Acceptance Criteria:**
- `IMultiSymbolDataProvider` is defined in `TradingResearchEngine.Core`.
- `SynchronizedDataProvider` merges two symbol streams and emits aligned events.
- Unit tests verify alignment: given AAPL and MSFT bars with some timestamps missing from each, the inner-join mode emits only timestamps present in both.
- `ScenarioConfig.Symbols` is a valid alternative to `Symbol` in `DataProviderOptions`.

---

## Implementation Order Summary

| Priority | Item | Files | Effort |
|---|---|---|---|
| P0 | 1. Fix EnrichWithTrialCountAndDsrAsync table-scan | RunScenarioUseCase, IStrategyRepository | S |
| P0 | 2. Fix TotalTrialsRun write race | RunScenarioUseCase, IStrategyRepository | S |
| P0 | 3. Fix RandomizedOos non-contiguous warmup bug | RandomizedOosWorkflow | M |
| P0 | 4. Fix IRiskLayer/IExecutionHandler singleton scope | RunScenarioUseCase, Program.cs | S |
| P1 | 5. Memoize CreateStrategy reflection | RunScenarioUseCase | S |
| P1 | 6. Fix ConvertJsonElement silent fallback | RunScenarioUseCase | S |
| P1 | 7. Fix DSR moments — trade returns not bar returns | RunScenarioUseCase | S |
| P1 | 8. Fix shallow dictionary copy in parallel with-clones | All research workflows | S |
| P1 | 9. Make BacktestEngine injectable via interface | BacktestEngine, RunScenarioUseCase | M |
| P1 | 10. Fix RandomizedOosResult silent failure absorption | RandomizedOosWorkflow | S |
| P1 | 11. Enforce explicit sort in ParameterSweepWorkflow | ParameterSweepWorkflow | S |
| P1 | 12. Add BarsPerYear/Interval preflight check | PreflightValidator | S |
| P1 | 13. Add audit log to SealedTestSetGuard | SealedTestSetGuard | M |
| P2 | 14. Add IS/OOS efficiency ratio histogram | StudyDetail, Renderers | M |
| P2 | 15. Add buy-and-hold benchmark overlay | BacktestResult, equity curve chart | M |
| P2 | 16. Add strategy comparison view | Dashboard, CompareRuns.razor | M |
| P2 | 17. Add CSV/JSON export for trades and equity curve | StudyDetail, ExportService | S |
| P2 | 18. Add composite strategy tree visualiser | CompositeStrategy, StudyDetail | M |
| P3 | 19. Add keyboard shortcut system | MainLayout, all pages | M |
| P3 | 20. Add pre-launch study cost estimator | Study launcher, StudyCostEstimatorService | M |
| P3 | 21. Fix StdDev double-arithmetic in RandomizedOos | RandomizedOosWorkflow | S |
| P3 | 22. Delete obsolete Validate method | RunScenarioUseCase | S |
| P3 | 23. Add multi-symbol data provider foundation | Core, Application | L |

---

## Global Constraints

- Do not change any public `IStrategy`, `IStrategyFactory`, or `IBacktestEngine` interfaces without backward-compatible default interface methods where possible.
- All new services must be registered in the DI container in `Program.cs`.
- All new Blazor components must follow the existing MudBlazor design language and use `--color-*` CSS variables from the theme.
- Do not introduce new NuGet packages without justification. Prefer in-box .NET 8 APIs.
- All new methods with non-trivial logic require an XML doc comment.
- Verify the solution builds with zero errors and zero new warnings after each item before proceeding to the next.
