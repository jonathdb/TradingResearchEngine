# TradingResearchEngine — Dev Branch Review v2: Doneness Audit & New Improvement Opportunities

## Executive Summary

The PR-gate implementation plan has been executed to a high standard. All eight categories of findings from the first review have been addressed. Critical correctness bugs (empty parameter grid, null catalog entries, portfolio hot-path allocation) are resolved. Concurrency architecture is coherent and production-ready. Configuration canonicalization, job retry, persistence reconciliation, and beginner-mode realism defaults are all in place. The codebase is materially more production-ready than it was before the implementation effort.

This review documents **3 incomplete items** that need minor follow-up, **5 new bugs or correctness concerns** discovered in the updated code, and **14 new improvement opportunities** for the next iteration cycle.

***

## Part 1 — Doneness Audit

### ✅ Fully Resolved From Previous Review

| Previous Finding | Resolution |
|---|---|
| Empty parameter grid in walk-forward | `GridOptimizer` + `ParameterGrid` implemented; `WalkForwardWorkflow` uses grid path when grid is non-null |
| Nested parallelism oversubscription | `ConcurrencyBudget` singleton registered; `MonteCarloWorkflow`, `CpcvStudyHandler`, `ParameterPerturbationWorkflow` all consume it |
| `Portfolio.Positions` allocating per call | Cached snapshot with dirty flag; `RebuildSnapshots()` only runs on state change |
| Sequential Monte Carlo / CPCV / Perturbation | All three now use `Parallel.ForEachAsync` with `ConcurrencyBudget` |
| Null entry in `SkenderIndicatorCatalog` | `IndicatorCatalogValidationService` exists; startup validation path now in place |
| Beginner mode zero slippage/commission | `StrategyTemplate.DefaultRealismProfile` defaults to `StandardBacktest`; `FastResearch` is never the default |
| No confirmation gate before sealing test set | `FinalValidationUseCase.ExecuteAsync(userConfirmed: bool)` implemented; `AlreadyConsumed` and `Cancelled` results returned correctly |
| Research checklist passive | `ResearchChecklistService` now has `NavigationPaths` and `ConfidenceExplanations`; checklist integrated into final validation gate |
| `ScenarioConfig` dual-schema drift | `ScenarioConfigNormalizer` implemented in-memory; typed access via `DataProviderOptionsAdapter` in some paths |
| No retry policy for background jobs | `JobFailureType` enum, `Retrying` state, `MarkRetryingAsync`, and `MarkFailedWithTypeAsync` are all present |
| SQLite/JSON store silent divergence | `ConsistencyReconciler : IHostedService` runs at startup; JSON is authoritative; integration tests cover it |
| Gemini AI calls had no timeout | `GeminiOptions.CallTimeout` (default 60 s); per-attempt `CancellationTokenSource` linked to caller token in `GeminiClient` |

***

### 🟡 Incomplete Items — Minor Follow-Up Required

#### 1. `WalkForwardWorkflow` still uses raw string-key dictionary access for `DataProviderOptions`

`CpcvStudyHandler` was updated to use the typed `GetFrom()` / `GetTo()` extension methods (lines 43–44). `WalkForwardWorkflow` was not — it still falls back to the old `TryGetValue("From", ...)` / `TryGetValue("To", ...)` pattern on lines 55–58 and in `BenchmarkComparisonWorkflow` (lines 99–103). This inconsistency means the typed-access path is only partially adopted. A silent `DateTimeOffset.MinValue` / `DateTimeOffset.MaxValue` fallback occurs rather than a structured validation error when the date fields are missing.

**Fix:** Replace the two `TryGetValue` blocks in `WalkForwardWorkflow.RunAsync` and `BenchmarkComparisonWorkflow.RunAsync` with `dataOpts.GetFrom()` / `dataOpts.GetTo()` and propagate the `InvalidOperationException` that those extension methods already throw.

#### 2. `CAGR` objective in `GridOptimizer` is total return, not annualised CAGR

`GridOptimizer.ComputeCagr` is documented as "CAGR as total return percentage" but the method name and the `OptimizationObjective.CAGR` label promise annualised growth rate. The formula `(EndEquity − StartEquity) / StartEquity` produces total return, which is window-length-dependent — a shorter IS window will systematically score lower than a longer one even with identical alpha, making cross-window comparison incoherent when using the CAGR objective.

**Fix:** Rename the objective to `TotalReturn` (or compute true annualised CAGR using `BarsPerYear` and the window length in `WalkForwardWindow`), and update the exclusion reason message accordingly.

#### 3. `RealismSensitivityWorkflow` is still sequential

`MonteCarloWorkflow`, `CpcvStudyHandler`, and `ParameterPerturbationWorkflow` were parallelised in this implementation. `RealismSensitivityWorkflow` runs its three profiles in a plain `foreach` loop (line 31) and was not updated. It is not in the original task list, but it fits the same pattern and its sequential execution is avoidable since all three profiles are independent runs.

**Fix:** Apply the same `Parallel.ForEachAsync` + `ConcurrencyBudget` pattern used in `ParameterPerturbationWorkflow`. The workflow only runs three iterations, so the impact is modest, but the consistency is meaningful.

***

## Part 2 — New Bugs and Correctness Concerns

### 🔴 Bug 1: `CpcvStudyHandler` passes a stale concurrency count to `MaxDegreeOfParallelism`

`CpcvStudyHandler` line 85 passes `MaxDegreeOfParallelism = _concurrencyBudget.Available` to `ParallelOptions`, then also calls `await _concurrencyBudget.AcquireAsync(token)` inside the loop body. This is a double-gate: `Available` is a snapshot taken before iteration starts, so if other concurrent workflows have already claimed permits, the parallelism degree is underestimated. Worse, if `Available` is 0 at the snapshot moment, `MaxDegreeOfParallelism` becomes 0, which causes `Parallel.ForEachAsync` to throw an `ArgumentOutOfRangeException`. `ParameterPerturbationWorkflow` has exactly the same pattern on line 82.

**Fix:** Remove `MaxDegreeOfParallelism` from `ParallelOptions` entirely for workflows that already throttle via `ConcurrencyBudget.AcquireAsync`. The semaphore is the right gate; the `MaxDegreeOfParallelism` cap is redundant and dangerous when derived from a live count.

### 🔴 Bug 2: `GeminiStrategyAssistant` throws on construction when API key is null

The constructor currently contains:
```csharp
if (string.IsNullOrWhiteSpace(_options.ApiKey))
    throw new InvalidOperationException("Gemini API key is not configured...");
```

This means `GeminiStrategyAssistant` cannot be registered in the DI container when the API key is not configured, even though `GeminiOptions.ApiKey` is documented as "When null or empty, AI assistant features are disabled gracefully." The `FallbackStrategyIdeaTranslator` checks `_options.EnableAIStrategyAssist` at call time — but `GeminiStrategyAssistant` crashes at registration time if the key is absent, regardless of the feature flag. This forces every deployment to configure a dummy API key even when AI features are intentionally disabled.

**Fix:** Move the API key check from the constructor to the call site — return a graceful failure result (`new AIStrategyDraft(Success: false, FailureReason: "API key not configured")`) instead of throwing at construction.

### 🟠 Bug 3: `IReportExporter` does not expose `ComparisonReport` export; `MarkdownReporter.RenderToMarkdown(ComparisonReport)` result is never persisted to disk

`MarkdownReporter.RenderToMarkdown(ComparisonReport)` renders a full Markdown table (lines 62–78) but returns it as a `string`. There is no `IReportExporter.ExportComparisonMarkdownAsync` method and no caller path that writes the string to disk. `ScenarioComparisonUseCase.Compare` builds a `ComparisonReport` in memory but has no save method. The comparison result is entirely ephemeral — it disappears when the in-memory object is garbage-collected.

**Fix:** Add `Task<string> ExportComparisonMarkdownAsync(ComparisonReport report, CancellationToken ct = default)` to `IReportExporter` and implement it in `ReportExporter`. Call it from `ScenarioComparisonUseCase` after building the report.

### 🟠 Bug 4: `ClosedTrade` has no MAE/MFE tracking

`ClosedTrade` records entry/exit price, direction, quantity, and P&L, but no intra-trade extremes. The requirements document lists MAE and MFE as analytics targets, and the tasks file includes them. The engine processes mark-to-market on every bar via the equity curve, which means intra-trade extremes are observable at runtime but not captured. Without MAE/MFE on the `ClosedTrade` record, downstream analytics (edge ratio, R-multiple distribution, entry/exit quality scoring) cannot be computed.

**Fix:** Add `decimal MaxAdverseExcursion` and `decimal MaxFavorableExcursion` fields to `ClosedTrade`. Update the engine's fill/close logic to track the running high-water mark and low-water mark of unrealised P&L between entry and exit.

### 🟡 Bug 5: `WalkForwardWorkflow` local `SemaphoreSlim` ignores `ConcurrencyBudget`

Unlike `CpcvStudyHandler` and `ParameterPerturbationWorkflow`, `WalkForwardWorkflow` constructs its own `SemaphoreSlim` on line 69 (`var semaphore = new SemaphoreSlim(maxConcurrency)`) using `Environment.ProcessorCount - 1` rather than accepting `ConcurrencyBudget` via dependency injection. This means walk-forward windows bypass the global concurrency budget, allowing oversubscription when walk-forward runs concurrently with sweep or Monte Carlo jobs.

**Fix:** Inject `ConcurrencyBudget` into `WalkForwardWorkflow` and replace the local `SemaphoreSlim` with `_concurrencyBudget.AcquireAsync(token)`.

***

## Part 3 — New Improvement Opportunities

### Research Product Capabilities

#### Opp 1: Trade anatomy analytics are absent despite engine readiness

The equity curve captures mark-to-market on every bar. The missing step is tracking the per-trade running max adverse and max favorable excursion between entry and exit, and then exposing them as a distribution in `BacktestResult`. Once MAE/MFE is captured in `ClosedTrade`, the following analytics become trivially computable: edge ratio (MFE/MAE), entry quality score (how close to the extremum the entry was placed), exit quality score, and the classic "R-multiple" distribution. These are among the most actionable metrics for strategy refinement.

#### Opp 2: Concatenated OOS equity curve for walk-forward results is missing

`WalkForwardResult` contains `IReadOnlyList<WalkForwardWindow>` and each window has an OOS `BacktestResult` with its own equity curve. There is no property that stitches these OOS curves into a single time-ordered equity series. The concatenated OOS curve is the standard presentation of walk-forward robustness — it is how the "live" performance of a parameter-adaptive strategy would look. Researchers currently have to reconstruct this manually.

**Fix:** Add a `IReadOnlyList<EquityCurvePoint> ConcatenatedOosEquityCurve` computed property to `WalkForwardResult` (or to a `WalkForwardSummary` extension type), built by appending each `WalkForwardWindow.OutOfSampleResult.EquityCurve` in window index order.

#### Opp 3: No OOS profitability rate statistic in walk-forward summary

Walk-forward currently surfaces efficiency ratio and mean OOS Sharpe. The research literature's primary walk-forward quality metric is the **OOS profitability rate**: the fraction of OOS windows where the strategy was profitable. A high in-sample Sharpe combined with a low OOS profitability rate (e.g., 3 of 10 windows profitable) is a strong overfitting signal. The `WalkForwardSummary` should expose `decimal OosProfitabilityRate` computed as `profitable windows / total windows`.

#### Opp 4: `ScenarioComparisonUseCase` only supports same-metric best-of logic

`ComparisonReport` identifies "best by Sharpe" and "best by drawdown" but offers no multi-criteria ranking. Researchers frequently want to filter comparison candidates by a minimum win rate threshold, then rank the survivors by Calmar ratio. A lightweight `ComparisonFilter` record (`MinWinRate`, `MinTrades`, `MaxDrawdown`) and an optional sort key would enable this without breaking the existing API.

#### Opp 5: No strategy versioning across research cycles

`StrategyVersion` / `DevelopmentStage` exists, but there is no structured way to compare two version IDs side-by-side in the UI. A researcher who finishes a walk-forward pass, tweaks a stop-loss parameter, and re-runs wants to see the delta in all metrics across both versions. This is distinct from `ScenarioComparisonUseCase`, which compares arbitrary `BacktestResult` instances — a version-aware comparison would pin the results to specific strategy versions and display deltas.

***

### Architecture and Code Quality

#### Opp 6: `StrategyRegistry` uses reflection for default parameter inference

`StrategyRegistry` lines 161–162 use a `switch` on `typeof(int)` / `typeof(decimal)` to infer default parameter values via reflection. `ReflectionStrategyFactory` (lines 95–100) also uses reflection for parameter deserialization from `JsonElement`. Both paths exist alongside the typed factory pattern. The reflection paths are not wrong, but they are untestable for correctness by type and will silently produce wrong defaults for any new parameter type added to a strategy constructor. A `[StrategyParameter(default: 14)]` attribute or a `IStrategyParameterSchema` static interface method would make defaults explicit and schema-driven rather than inferred at runtime.

#### Opp 7: `DataProviderOptions` dictionary is `Dictionary<string, object>` throughout the core

The typed `CsvDataProviderOptions`, `HttpDataProviderOptions`, and `DukascopyDataProviderOptions` classes exist in Application/Configuration, but `ScenarioConfig.DataProviderOptions` remains `Dictionary<string, object>`. This means all callers — including `WalkForwardWorkflow`, `BenchmarkComparisonWorkflow`, and `CpcvStudyHandler` — must use string keys. The `DataProviderOptionsAdapter` extension methods (`GetFrom`, `GetTo`) partially abstract this, but the typed classes are never the authoritative shape in `ScenarioConfig`. Migrating `ScenarioConfig.DataProviderOptions` to a sealed discriminated union (`CsvDataProviderConfig | HttpDataProviderConfig | DukascopyDataProviderConfig`) would eliminate all string key usage and make malformed configs a compile-time error.

#### Opp 8: `ReportExporter` uses synchronous `File.WriteAllText` on async methods

`ReportExporter.ExportMarkdownAsync`, `ExportTradeCsvAsync`, `ExportEquityCsvAsync`, and `ExportJsonAsync` all call `File.WriteAllText` (synchronous) despite being declared `async Task<string>`. This blocks a thread-pool thread during I/O under load. The fix is mechanical — replace with `File.WriteAllTextAsync`.

#### Opp 9: `GeminiStrategyAssistant` has no prompt-length guard

`GenerateStrategyAsync` and `StreamGenerateAsync` concatenate the system prompt and user message without any length validation. A user who pastes a large document into the strategy description field will produce a request that exceeds the model's context window, resulting in an opaque API error rather than a clear validation message. A configurable `MaxPromptLength` property in `GeminiOptions` and a pre-call check would give researchers a useful error message.

#### Opp 10: `BacktestResult` has commented-out metrics (VaR95, CVaR95, OmegaRatio, UlcerIndex)

Four metric fields are commented out in `BacktestResult` (lines 13–16):
```csharp
// decimal? VaR95,
// decimal? CVaR95,
// decimal? OmegatRatio,
// decimal? UlcerIndex,
```
The comment implies they were implemented in V1 but removed for unknown reasons. The metrics calculator infrastructure already exists (`DsrCalculator`, `MinBtlCalculator`). If these metrics are intentionally deferred, they should be tracked as a known gap in the CHANGELOG. If they are unintentionally omitted, they are straightforward to restore.

***

### Developer Experience and Tooling

#### Opp 11: No end-to-end integration test covering the full walk-forward → OOS → persist cycle

Integration tests cover `CsvDataProvider`, persistence (JSON + SQLite), and strategy export. There is no integration test that runs `WalkForwardWorkflow` end-to-end against the sample CSV data, verifies that OOS windows are populated, and confirms that the result is persisted and retrievable. Given the complexity of the walk-forward data-slicing and grid optimization path, an end-to-end test here would be high-value.

#### Opp 12: `JobWorkerService` has no observable job queue depth metric

The job queue is a `ConcurrentQueue` inside `JobWorkerService`, but its depth is not exposed via any health check endpoint or structured log. Operators have no way to observe queue buildup without reading job records from the repository. A simple `IJobQueueMetrics` interface with `int PendingCount`, `int RunningCount`, and `int FailedCount` — sourced from `JobExecutor`'s progress cache and repository queries — would make the job system observable without adding external infrastructure.

#### Opp 13: Kiro automation hooks have no test for architecture violations

`.kiro/hooks/architecture-check.md` describes a save-triggered architecture check. However, there is no automated test (e.g., a `[Fact]` that scans all `*.cs` files for upward `using` references) that enforces the dependency rule in CI. The hook fires on save in the IDE but is invisible to the build pipeline. A `ArchitectureDependencyTests.cs` using `NetArchTest.Rules` or equivalent would close this gap.

#### Opp 14: `CHANGELOG.md` does not reflect the PR gate implementation

The changelog ends at a version prior to the PR gate changes. Kiro hooks include a `doc-update.md` hook designed to keep docs in sync. After the PR gate implementation is complete, the CHANGELOG should document all eight gates under a new version entry. This is particularly important because `BacktestResult.Notes` and `Tags` (V9 additions visible in the record) are not mentioned in the CHANGELOG, making the version progression harder to reconstruct.

***

## Summary Matrix

| Category | Status | Count |
|---|---|---|
| Previous findings fully resolved | ✅ Done | 12 |
| Previous findings partially done | 🟡 Follow-up needed | 3 |
| New bugs (critical/high) | 🔴🟠 Fix before next release | 5 |
| New improvement opportunities | 💡 Next iteration | 14 |

### Priority Order for Next Kiro Prompt

1. **Bug fix:** Remove `MaxDegreeOfParallelism = _concurrencyBudget.Available` from `CpcvStudyHandler` and `ParameterPerturbationWorkflow`
2. **Bug fix:** Move `GeminiStrategyAssistant` API-key guard from constructor to call site
3. **Bug fix:** Inject `ConcurrencyBudget` into `WalkForwardWorkflow` to replace local `SemaphoreSlim`
4. **Follow-up:** Replace `TryGetValue("From",...)` in `WalkForwardWorkflow` and `BenchmarkComparisonWorkflow` with typed extension methods
5. **Bug fix / feature:** Add `ExportComparisonMarkdownAsync` to `IReportExporter`; persist `ComparisonReport` to disk
6. **Feature:** Add MAE/MFE fields to `ClosedTrade` and wire up engine tracking
7. **Feature:** Add concatenated OOS equity curve and OOS profitability rate to `WalkForwardResult`
8. **Fix:** Rename `OptimizationObjective.CAGR` to `TotalReturn` or compute true annualised CAGR
9. **Improvement:** Replace `File.WriteAllText` with `File.WriteAllTextAsync` in `ReportExporter`
10. **Improvement:** Add `MaxPromptLength` guard to `GeminiStrategyAssistant`