# TradingResearchEngine — Development Branch Review v3: Full Code Audit

## Executive Summary

The Development branch represents a dramatically more capable platform than what was reviewed in v1 and v2. Ten implementation gates have been merged: composite strategy engine, multi-timeframe support, MAE/MFE trade anatomy, walk-forward analytics enrichment, VaR/CVaR/Omega metrics restoration, discriminated union config, attribute-based parameter schemas, async I/O throughout, paper trading with a streaming pipeline, and a substantially expanded UI. The architecture is clean, the test suite is broad (119 Razor components, unit tests, property tests, and integration tests), and the Kiro hook chain keeps documentation and CI in sync.

This review identifies **4 residual bugs**, **2 structural gaps**, and **18 new improvement opportunities** across engine capability, product UX, and developer experience.

***

## Part 1 — Residual Issues

### 🔴 Bug 1: `PortfolioBacktestRunner` ignores `ConcurrencyBudget` — creates unbounded parallelism

`PortfolioBacktestRunner.RunAsync` (line 58) computes `maxParallelism = Math.Max(1, Environment.ProcessorCount - 1)` and passes it directly to `Parallel.ForEachAsync`. Every other parallel workflow in the codebase (`CpcvStudyHandler`, `ParameterPerturbationWorkflow`, `RealismSensitivityWorkflow`, `WalkForwardWorkflow`) uses the injected `ConcurrencyBudget` singleton for global throttling. `PortfolioBacktestRunner` is not injected with `ConcurrencyBudget` at all. When a user launches a 10-symbol portfolio backtest alongside a Monte Carlo study, the portfolio runner creates an independent thread pool without respecting existing permit reservations, allowing oversubscription on machines with 4–8 cores.

**Fix:** Inject `ConcurrencyBudget` into `PortfolioBacktestRunner` and replace the `Parallel.ForEachAsync` `MaxDegreeOfParallelism` hard-cap with `_concurrencyBudget.AcquireAsync(ct)` inside the loop body, matching the pattern in `CpcvStudyHandler`.

***

### 🔴 Bug 2: `IResearchJournalRepository` has no infrastructure implementation — DI fails at runtime

`IResearchJournalRepository` is defined in Application with three methods (`ListByStrategyAsync`, `ListByDateRangeAsync`, `SaveAsync`). `ResearchJournalEntry` implements `IHasId` and is fully fleshed out. However, `ServiceCollectionExtensions` has no registration for `IResearchJournalRepository` — the interface is never bound to an implementation. Any Razor component or service that injects `IResearchJournalRepository` will throw an `InvalidOperationException` at runtime. The generic `JsonFileRepository<ResearchJournalEntry>` already handles all the required operations, so this is a one-liner fix.

**Fix:** Add to `ServiceCollectionExtensions`:
```csharp
services.AddSingleton<IResearchJournalRepository>(sp =>
    new JsonFileRepository<ResearchJournalEntry>(
        sp.GetRequiredService<IOptions<RepositoryOptions>>(),
        sp.GetRequiredService<ILogger<JsonFileRepository<ResearchJournalEntry>>>()));
```

***

### 🟠 Bug 3: `WalkForwardWorkflow.WithDateRange` still builds a raw `Dictionary<string, object>` for date slicing — bypasses `DataProviderConfig`

The typed `DataProviderConfig` discriminated union is complete and `ScenarioConfig.DataProviderOptions` is marked `[Obsolete]`. However, `WalkForwardWorkflow.WithDateRange` (lines 349–356) still constructs a legacy `Dictionary<string, object>` keyed with `"From"` / `"To"` when slicing IS/OOS windows. This means every window config produced by walk-forward carries an obsolete dictionary as its data source spec, and all window runs hit the `#pragma disable CS0618` fallback path in `ScenarioConfig.EffectiveDataConfig`. The typed path is never exercised for walk-forward runs, undermining the migration.

**Fix:** Change `WithDateRange` to accept and return the typed `DataConfig` (or mutate `ScenarioConfig.Data.TypedProviderConfig.From/To` via `with` expressions) and update the callers on lines 151 and 189 accordingly.

***

### 🟡 Bug 4: `PropFirmVarianceWorkflow.Run` accepts `Dictionary<string, object>` for `userPreset` — pattern inconsistent with typed config migration

`PropFirmVarianceWorkflow.Run` takes an optional `Dictionary<string, object>? userPreset` and uses `TryGetValue` with string keys to read `GrossMonthlyReturnPercent`, `PayoutFrictionFactor`, and `PassRatePercent`. This is the same untyped dictionary pattern that was retired from `ScenarioConfig.DataProviderOptions`. The `InstantFundingConfig` record already contains exactly these fields. A typed `UserPresetOverrides` record would be a three-field record that takes five minutes to create.

**Fix:** Replace the `Dictionary<string, object>? userPreset` parameter with a typed `InstantFundingConfig? userPreset` (or a small `PropFirmPresetOverrides` record), and remove the `TryGetValue` / cast block.

***

## Part 2 — Structural Gaps

### Gap 1: `CompositeStrategy` cannot participate in parameter sweep or walk-forward

`ParameterGrid` operates over `ScenarioConfig.StrategyParameters` (a `Dictionary<string, object>`). `CompositeStrategy` is configured via `CompositeStrategyConfig` — a rich JSON object with indicators, entry/exit condition strings, and a `DirectionMode` enum. `StrategyParameters` for a composite run contains the serialised `CompositeStrategyConfig` as a single blob, not individual numeric ranges. `ParameterRange` only supports `decimal` bounds with a `Step`, so there is no mechanism to sweep indicator periods (e.g., SMA fast period 10→50, step 5) within a composite strategy.

This is the single largest usability gap for researchers who want to use the visual condition builder to define a strategy and then optimise its indicator parameters. Without sweep/walk-forward support, composite strategies are permanently restricted to manual iteration.

**Recommended approach:** Add a `CompositeParameterGrid` that maps indicator IDs to numeric ranges:
```json
{
  "indicatorId": "sma_fast",
  "parameterName": "period",
  "start": 10, "end": 50, "step": 5
}
```
`GridOptimizer` would clone the `CompositeStrategyConfig`, inject the overridden period into the matching `IndicatorConfig`, and run the sweep as it does for standard strategies.

***

### Gap 2: No real broker feed implementation for `IStreamingDataProvider` — paper trading cannot operate in live mode

`SimulatedPaperTradingSession` has an excellent design: it reuses the full backtest execution pipeline against a streaming data source. `IStreamingDataProvider` is cleanly defined in Core. However, the only registered implementation of `IStreamingDataProvider` in `ServiceCollectionExtensions` (line 251) is a simulated replay provider backed by the local CSV/Dukascopy cache. There is no live feed adapter (e.g., Yahoo Finance WebSocket, IBKR TWS, or even a polling REST provider). A researcher who sets `PaperTradingMode = Live` will receive simulated bars, not real market data. The `SessionSetup.razor` page does not communicate this limitation.

**Fix (short-term):** Add a UI warning in `SessionSetup.razor` that live mode uses simulated playback until a real feed is configured. Add a `DataFeedMode` enum (`Replay | Live`) to `PaperTradingOptions` and display it clearly.

**Fix (long-term):** Implement a `PollingRestStreamingDataProvider` that polls a public REST endpoint (e.g., Yahoo Finance or Alpha Vantage) on a configurable interval, emitting bars as they arrive. This does not require a WebSocket — polling is sufficient for daily/hourly paper trading.

***

## Part 3 — New Improvement Opportunities

### Engine & Quant Capabilities

#### Opp 1: VaR/CVaR/Omega/Ulcer are computed but not displayed in `MarkdownReporter`

`BacktestResult` now has `VaR95`, `CVaR95`, `OmegaRatio`, and `UlcerIndex`. `MetricsCalculator` computes them. `ResultMetricsPanel.razor` presumably renders some of them. However, `MarkdownReporter.RenderToMarkdown(BacktestResult)` does not include any of these four fields in the Markdown report table — the export omits the metrics that were specifically restored in Gate 9. Researchers who export reports will see an incomplete metrics table compared to the UI.

**Fix:** Add VaR95, CVaR95, OmegaRatio, and UlcerIndex rows to the `MarkdownReporter` performance table.

#### Opp 2: `TradeExcursionTracker` uses bar close price for MAE/MFE — intra-bar extremes are missed

`TradeExcursionTracker.UpdatePrice` accepts a single `currentPrice` on each bar, and the engine feeds it the bar's close. For daily data this misses intra-bar extremes — the actual MAE could be significantly worse (and MFE better) than the close-based approximation. For intraday data, where high/low data is available in `BarRecord`, this approximation understates excursions significantly.

**Fix:** Change `UpdatePrice` to `UpdateBar(BarRecord bar)` and use `bar.Low` as the adverse extreme and `bar.High` as the favorable extreme for long positions (inverted for short). This is the industry-standard approach for MAE/MFE on OHLC data and requires no additional data storage.

#### Opp 3: `GridOptimizer.TotalReturn` uses simple total return, not time-weighted — window-length bias

`OptimizationObjective.TotalReturn` computes `(EndEquity − StartEquity) / StartEquity`. In walk-forward, IS windows may have different lengths (e.g., the first window is longer in expanding mode). A parameter set with a 12-month IS window that earns 15% total return will score higher than one with an 8-month IS window earning 14%, even if the annualised rate of the 8-month result is higher. Using time-weighted return (`(EndEquity / StartEquity)^(BarsPerYear / windowBars) − 1`) would eliminate this bias. The `WalkForwardWindow` already carries the IS `BacktestResult`, whose `Metadata.DataRangeStart/End` can be used to compute window length.

#### Opp 4: `WalkForwardSummary.ParameterDriftScore` is defined but has no tooltip or interpretation guide in the UI

`WalkForwardAnalytics.ParameterDriftScore` and the per-window `ParameterHistory` are computed and available. The `WalkForwardCompositeChart.razor` shows the concatenated OOS equity curve. However, there is no UI element that explains what the parameter drift score means numerically, what a "high" vs "low" score implies, or what action a researcher should take when it is high. Without interpretive guidance, researchers will ignore the metric.

**Fix:** Add a `MudTooltip` or info panel to the walk-forward result page that explains: "A drift score above X suggests the strategy is highly sensitive to parameter choice — walk-forward gains may not be reproducible." The `RobustnessAdvisoryService` pattern (already implemented for Monte Carlo and sweep) should also evaluate parameter drift and emit a robustness warning when the score exceeds a configurable threshold.

#### Opp 5: `DsrCalculator` and `MinBtlCalculator` are not surfaced in the research checklist

`BacktestResult.DeflatedSharpeRatio` (V4) and `MinBtlCalculator` compute the Bailey–López de Prado overfitting penalty. These are among the most important quantitative safeguards against multiple-testing bias. The `ResearchChecklistService` does not include a checklist item that verifies DSR is computed and above a minimum threshold (e.g., > 0.5). A researcher can complete the entire checklist and reach final validation without the DSR being evaluated.

**Fix:** Add a `DsrCheck` checklist item to `ResearchChecklistService` that passes when `BacktestResult.DeflatedSharpeRatio` is not null and is above a configurable minimum.

#### Opp 6: Monthly returns heatmap (`MonthlyReturnsHeatmap.razor`) has no corresponding data computation in `MetricsCalculator`

`MonthlyReturnsHeatmap.razor` exists as a UI component. Monthly returns require grouping `EquityCurve` points by calendar month and computing the percentage return for each month. There is no method in `MetricsCalculator` or `ChartComputationHelpers` that produces monthly return data — the component must be computing this inline or receiving raw `EquityCurve` data. Inline computation in Razor components bypasses testing and duplicates logic that belongs in the Application layer.

**Fix:** Add `IReadOnlyList<MonthlyReturn> ComputeMonthlyReturns(IReadOnlyList<EquityCurvePoint> curve)` to `ChartComputationHelpers` (or `MetricsCalculator`) and have `MonthlyReturnsHeatmap` call it. Add a unit test for the monthly grouping logic.

***

### Product UX

#### Opp 7: No dedicated UI page for Research Journal — `IResearchJournalRepository` has no front-end surface

The `ResearchJournalEntry` record and `IResearchJournalRepository` are fully designed, but there is no Razor page that lets researchers view or add journal entries. The `ResearchChecklist` and `ResearchSummaryRail` components exist, but the narrative decision log (promotes, rejects, revisions, notes) has no dedicated page. For a tool explicitly designed for iterative strategy research, the absence of a research log is a significant product gap — researchers cannot trace why they made parameter changes.

**Fix:** Add `/strategies/{id}/journal` as a dedicated page with a timeline view of `ResearchJournalEntry` records (grouped by action type), a "Add Note" dialog, and inline stage-transition entries that are automatically created when `DevelopmentStage` changes.

#### Opp 8: `Compare.razor` has no deep link — comparison state is not bookmarkable or shareable

The comparison page is a single canonical route at `/compare`, which was cleaned up in Gate 8. However, the selected result IDs are held in component state and are lost on navigation or refresh. A researcher who builds a comparison between three specific runs cannot share a URL or bookmark it. For a research tool, reproducible comparisons are essential.

**Fix:** Encode selected result IDs as query parameters: `/compare?ids=id1,id2,id3`. Read them on `OnInitializedAsync` and pre-populate the comparison. This also enables linking from result detail pages with a "Compare to..." flow.

#### Opp 9: Strategy builder does not show `SensitivityHint` annotations in the sweep UI

`StrategyParameterSchema.SensitivityHint` classifies each parameter as `Low`, `Medium`, or `High` sensitivity to overfitting. `ParameterGroupEditor.razor` renders the parameter editor, but there is no visible indicator of overfitting sensitivity. When researchers configure a parameter sweep range, they receive no warning that sweeping a high-sensitivity parameter with a tight step will inflate false discovery risk.

**Fix:** Render a `MudTooltip` or coloured `MudChip` (green/amber/red) next to each parameter in the sweep range editor, sourced from `StrategyParameterSchema.SensitivityHint`. Add a sweep-scope overfitting warning when the total combinations count exceeds a threshold and any dimension has `High` sensitivity.

#### Opp 10: `BacktestResult.Tags` and `BacktestResult.Notes` (V9) are not surfaced in the History / Result Detail pages

`BacktestResult` now carries `Tags` and `Notes` as V9 additions (confirmed in CHANGELOG). The `ResultDetail.razor` page renders metrics, equity curve, trade log, and realism panels — but there is no UI element to view, add, or edit notes or tags on a result. Tags would enable filtering the History page (`BacktestList.razor`) by user-defined label (e.g., "post-FOMC", "pre-2008-data"). This is a partially implemented feature.

**Fix:** Add a "Notes & Tags" tab or expandable panel to `ResultDetail.razor`. Add tag filtering chips to `BacktestList.razor` / `History.razor`. Persist updates via the existing `IRepository<BacktestResult>` `SaveAsync`.

#### Opp 11: No keyboard shortcut for launching a new run from result detail

`KeyboardShortcutOverlay.razor` exists, and there is a keyboard shortcut infrastructure. A common researcher workflow is: run → review result → tweak parameters → re-run. Currently this requires navigating back to the builder. A keyboard shortcut (`R` for "re-run with same config", `N` for "new run") from the result detail page would materially accelerate the iteration loop that is the core purpose of the tool.

#### Opp 12: `StrategyBuilder` multi-step wizard has no draft auto-save

`Step2DataExecutionWindow.razor`, `Step3StrategyParameters.razor` etc. form a wizard flow. The `ConfigDraft` and `ConfigDraftValidator` infrastructure exists for persistent drafts. However, there is no observable auto-save indicator in the builder — a researcher who closes the browser mid-wizard may lose work. A debounced auto-save on parameter change (every 3 seconds after last keystroke) combined with a "Draft saved" timestamp in the builder header would eliminate data loss anxiety.

***

### Architecture & Code Quality

#### Opp 13: `ScenarioConfig.DataProviderOptions` is marked `[Obsolete]` but still written by two active callers

`WalkForwardWorkflow.WithDateRange` writes a new `Dictionary<string, object>` with `"From"` / `"To"` keys and assigns it to `DataProviderOptions`. This means every IS/OOS window config produced at runtime carries an obsolete field. The `[Obsolete]` attribute on `DataProviderOptions` generates compiler warnings but is not treated as an error (`[Obsolete("...", error: true)]`). Upgrading the attribute to `error: true` would make the remaining two write sites compile-time failures and force resolution.

**Fix:** Change to `[Obsolete("...", error: true)]` after resolving `WalkForwardWorkflow.WithDateRange` (see Bug 3).

#### Opp 14: `CompositeStrategyConfig` `EntryCondition` and `ExitCondition` are `string` — no character length guard

`CompositeStrategyConfigValidator.Validate` checks that entry/exit conditions are non-empty and that indicator IDs referenced in expressions are defined. It does not guard against pathologically long expressions. `ConditionParser.Parse` and `ExpressionCompiler.Compile` are both recursive — a deeply nested condition string (e.g., 500-level nested `AND`/`OR`) will produce a stack overflow rather than a validation error. While adversarial input is not the primary concern for a single-user research tool, accidental runaway expressions are plausible.

**Fix:** Add a `MaxConditionLength` check (e.g., 2000 characters) and a maximum operator depth check to `CompositeStrategyConfigValidator`.

#### Opp 15: `SimulatedPaperTradingSession` uses `System.Reactive.Subjects.Subject<T>` — no error-handling path on the subject

`SimulatedPaperTradingSession` emits `PaperBarEvent` and `PaperTradeEvent` via `Subject<T>`. If a subscriber's `OnNext` handler throws, the `Subject` will propagate the exception and terminate the observable stream. All subsequent events will be silently dropped — the session will appear to be running (state machine remains `Running`) but will stop emitting events. This is the Rx "observer exception terminates stream" footgun.

**Fix:** Wrap subject emissions in `try/catch` and log the exception without terminating the stream:
```csharp
try { _barSubject.OnNext(paperBarEvent); }
catch (Exception ex) { _logger.LogError(ex, "Subscriber threw during PaperBarEvent emission"); }
```

#### Opp 16: `ExportValidator` uses compiled `Regex` objects declared as `static readonly` inside the method body — minor performance issue

`ExportValidator.ValidatePineScript` and `ValidateMql` contain `static readonly Regex` patterns declared inline inside the method. In .NET 8+, the idiomatic approach is source-generated regex (`[GeneratedRegex]` attribute on a `static partial` method), which compiles the pattern at build time, eliminates the `static readonly` field dance, and produces zero-allocation matching. This is a low-priority quality improvement but aligns with modern .NET patterns.

***

### Testing

#### Opp 17: No property-based test for `TradeExcursionTracker` direction correctness

`TradeExcursionTrackerTests` exists, but a quick review of the unit test suite shows example-based tests only. The tracker has a subtle inversion for short positions — for a short, favorable price movement is downward. A property test (`for any sequence of prices, MAE for short equals MFE for the equivalent long on the same price sequence`) would catch direction inversion bugs that example tests may miss. The `CompositeStrategy` has excellent property coverage (`CrossesDetectionProperties`, `ConditionShortCircuitProperties`) — excursion tracking deserves the same treatment.

#### Opp 18: No integration test for `SimulatedPaperTradingSession` replay-to-completion

`WalkForwardIntegrationTests` (Gate 10 Opp 11) is implemented. There is no integration test that starts a `SimulatedPaperTradingSession` with the sample CSV data, runs it to completion, and verifies the `PaperTradingResult` matches a reference backtest run over the same data. The "metric equivalence between paper and backtest" invariant, which is explicitly stated in the `SimulatedPaperTradingSession` XML doc, has no automated verification.

***

## Summary Matrix

| Category | Count | Priority |
|---|---|---|
| Residual bugs from previous reviews | 4 | Fix now |
| Structural gaps blocking key workflows | 2 | High |
| Engine/quant improvements | 6 | Medium–High |
| Product UX improvements | 6 | Medium |
| Architecture/code quality | 4 | Low–Medium |
| Testing gaps | 2 | Medium |

### Recommended Next Kiro Prompt Priority Order

1. Register `IResearchJournalRepository` in DI (one line — zero risk)
2. Fix `PortfolioBacktestRunner` — inject `ConcurrencyBudget`
3. Fix `WalkForwardWorkflow.WithDateRange` — migrate to typed `DataProviderConfig`
4. Fix `PropFirmVarianceWorkflow` — replace `Dictionary<string, object>` preset
5. Fix `TradeExcursionTracker.UpdatePrice` → `UpdateBar(BarRecord)` for OHLC extremes
6. Add `IResearchJournalRepository` UI page at `/strategies/{id}/journal`
7. Add `BacktestResult.Tags`/`Notes` editing to `ResultDetail.razor`
8. Fix `MarkdownReporter` to include VaR95/CVaR95/Omega/Ulcer in export
9. Add `CompositeParameterGrid` support to `GridOptimizer` and walk-forward
10. Add DSR checklist item to `ResearchChecklistService`