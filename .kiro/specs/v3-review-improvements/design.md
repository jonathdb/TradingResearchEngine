# Design Document

## Introduction

This design covers the implementation of 23 requirements from the v3 review improvements specification. The changes span four tracks: engine/quant capabilities (Requirements 1, 4, 5, 7, 19, 21, 23), product UX (Requirements 2, 6, 8, 9, 10, 11, 12, 13, 14, 22), architecture/code quality (Requirements 3, 15, 16, 17, 18), and testing (Requirements 19, 20).

## Architecture Overview

All changes follow the existing dependency rule: Core ← Application ← Infrastructure ← Web. No new project references are introduced. New types are placed in existing folders where appropriate.

```
Core (no changes to public contracts)
  └── Results/BacktestResult.cs (existing — consumed, not modified)

Application
  ├── Research/
  │   ├── GridOptimizer.cs (extended: TimeWeightedReturn objective, CompositeParameterGrid, guardrail)
  │   ├── OptimizationObjective.cs (extended: TimeWeightedReturn enum value)
  │   ├── CompositeParameterGrid.cs (new)
  │   ├── ResearchChecklistService.cs (extended: DSR + MinBTL items)
  │   ├── WalkForwardWorkflow.cs (extended: CompositeParameterGrid support)
  │   ├── RobustnessAdvisoryService.cs (extended: parameter drift warning)
  │   ├── RobustnessThresholds.cs (extended: ParameterDriftThreshold)
  │   └── ResearchJournalEntry.cs (existing)
  ├── Helpers/
  │   └── ChartComputationHelpers.cs (modified: nullable monthly returns)
  ├── Metrics/
  │   └── MinBtlCalculator.cs (existing — consumed by checklist)
  ├── Strategies/
  │   ├── ConfigDraft.cs (extended: StrategyId, StrategyVersionId, SessionGuid key)
  │   └── Composite/
  │       └── CompositeStrategyConfigValidator.cs (extended: length/depth guards)
  ├── PaperTrading/
  │   └── SimulatedPaperTradingSession.cs (modified: subscriber exception resilience)
  ├── Export/
  │   └── ExportValidator.cs (rewritten: source-generated regex)
  └── Configuration/
      ├── SweepGuardrailOptions.cs (new)
      └── PollingProviderOptions.cs (new)

Infrastructure
  ├── Reporting/
  │   └── MarkdownReporter.cs (extended: VaR95, CVaR95, OmegaRatio, UlcerIndex)
  ├── DataProviders/
  │   └── PollingRestStreamingDataProvider.cs (new)
  └── Persistence/
      └── (existing JsonFileRepository — no changes)

Web
  ├── Components/
  │   ├── Pages/
  │   │   ├── Compare.razor (modified: deep linking via query params)
  │   │   ├── Backtests/ResultDetail.razor (modified: tags/notes, keyboard shortcut)
  │   │   ├── Backtests/BacktestList.razor (modified: tag filtering)
  │   │   ├── Strategies/Journal.razor (new)
  │   │   └── PaperTrading/SessionSetup.razor (modified: feed mode, observability)
  │   ├── Builder/
  │   │   ├── ParameterGroupEditor.razor (modified: sensitivity chips)
  │   │   └── BuilderViewModel.cs (modified: auto-save with debounce)
  │   ├── Charts/
  │   │   └── MonthlyReturnsHeatmap.razor (modified: null month rendering)
  │   └── Shared/
  │       └── KeyboardShortcutOverlay.razor (modified: R shortcut registration)
  └── Services/
      └── DraftAutoSaveService.cs (new)
```

## Detailed Design

### 1. Composite Strategy Parameter Sweep (Requirements 1, 21, 23)

#### New Type: CompositeParameterGrid

```csharp
// Application/Research/CompositeParameterGrid.cs
namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Maps composite strategy indicator IDs to numeric parameter ranges for sweep/walk-forward.
/// Each entry targets a specific indicator within a CompositeStrategyConfig.
/// </summary>
public sealed record CompositeParameterGrid(
    IReadOnlyList<CompositeParameterRange> Ranges);

/// <summary>
/// A single sweep dimension targeting a specific indicator parameter.
/// </summary>
/// <param name="IndicatorId">The unique ID of the indicator within the CompositeStrategyConfig.</param>
/// <param name="ParameterName">The parameter name on the IndicatorConfig to override.</param>
/// <param name="Start">Start of the sweep range (inclusive).</param>
/// <param name="End">End of the sweep range (inclusive).</param>
/// <param name="Step">Step increment between values.</param>
public sealed record CompositeParameterRange(
    string IndicatorId,
    string ParameterName,
    decimal Start,
    decimal End,
    decimal Step);
```

#### GridOptimizer Extension

The `GridOptimizer` gains a new overload:

```csharp
public GridOptimizationResult Optimize(
    IReadOnlyList<BacktestResult> candidates,
    OptimizationObjective objective,
    CompositeParameterGrid? compositeGrid = null)
```

A new static validation method is added:

```csharp
public static ValidationResult ValidateCompositeGrid(
    CompositeParameterGrid grid,
    CompositeStrategyConfig config,
    IOptions<SweepGuardrailOptions> options)
```

This method:
1. Checks each `IndicatorId` exists in the config — returns error if not found
2. Checks at least one range produces values — returns error if zero dimensions
3. Computes total combination count — returns error if exceeds `SweepGuardrailOptions.MaxCombinations` (default: 10000)

#### SweepGuardrailOptions

```csharp
// Application/Configuration/SweepGuardrailOptions.cs
public sealed class SweepGuardrailOptions
{
    public int MaxCombinations { get; set; } = SweepGuardrailDefaults.MaxCombinations;
}

public static class SweepGuardrailDefaults
{
    public const int MaxCombinations = 10000;
}
```

#### WalkForwardWorkflow Extension

`WalkForwardWorkflow.RunAsync` gains an optional `CompositeParameterGrid?` parameter. When provided, `GenerateCombinations` clones the `CompositeStrategyConfig` for each combination, injecting parameter overrides into the matching `IndicatorConfig`.

#### Persistence Backward Compatibility (Requirement 21)

The `CompositeParameterGrid` field on `WalkForwardOptions` and `SweepOptions` is serialised as an optional nullable JSON property. `System.Text.Json` default behaviour ignores unknown properties on deserialisation, so older versions that do not recognise the field will skip it. When loading options that lack the field, it deserialises as `null`.

No custom `JsonConverter` is needed — the default `System.Text.Json` behaviour handles both directions.

### 2. Live Data Feed and Polling Provider (Requirements 2, 22)

#### PollingRestStreamingDataProvider

```csharp
// Infrastructure/DataProviders/PollingRestStreamingDataProvider.cs
public sealed class PollingRestStreamingDataProvider : IStreamingDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly PollingProviderOptions _options;
    private readonly ILogger<PollingRestStreamingDataProvider> _logger;

    // Observable metrics
    public DateTimeOffset? LastSuccessfulPoll { get; private set; }
    public int ConsecutiveFailureCount { get; private set; }
    public DataFeedMode CurrentMode { get; private set; }
}
```

#### PollingProviderOptions

```csharp
// Application/Configuration/PollingProviderOptions.cs
public sealed class PollingProviderOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int ConsecutiveFailureWarningThreshold { get; set; } = 5;
    public string? EndpointUrl { get; set; }
}
```

#### Observability (Requirement 22)

The provider exposes `LastSuccessfulPoll`, `ConsecutiveFailureCount`, and `CurrentMode` as public properties. The `SessionSetup` page binds to these via a timer-based polling UI refresh (every 5 seconds while a session is active).

When `ConsecutiveFailureCount` exceeds the configured threshold, the provider emits a structured log:
```
LogWarning("PollingFailureThresholdExceeded", "Consecutive failures: {Count}, threshold: {Threshold}", ...)
```

### 3. MarkdownReporter Metrics Completeness (Requirement 3)

The `RenderToMarkdown` method in `MarkdownReporter` is extended to include four additional rows in the metrics table:

```csharp
AppendMetricRow(sb, "VaR (95%)", result.VaR95, dp);
AppendMetricRow(sb, "CVaR (95%)", result.CVaR95, dp);
AppendMetricRow(sb, "Omega Ratio", result.OmegaRatio, dp);
AppendMetricRow(sb, "Ulcer Index", result.UlcerIndex, dp);
```

Where `AppendMetricRow` renders "N/A" when the value is null.

### 4. TradeExcursionTracker OHLC Bar Support (Requirement 4)

The `TradeExcursionTracker` method signature changes from:

```csharp
public void UpdatePrice(decimal price)
```

To:

```csharp
public void UpdateBar(BarRecord bar)
```

Internal logic:
- Long position: `adversePrice = bar.Low`, `favorablePrice = bar.High`
- Short position: `adversePrice = bar.High`, `favorablePrice = bar.Low`

The existing close-only path is preserved as a convenience overload that constructs a synthetic bar with `Open = High = Low = Close = price`.

### 5. Time-Weighted Return Objective (Requirement 5)

#### OptimizationObjective Extension

```csharp
public enum OptimizationObjective
{
    Sharpe,
    TotalReturn,
    MAR,
    /// <summary>Annualised return normalised by window duration: (End/Start)^(BarsPerYear/windowBars) − 1.</summary>
    TimeWeightedReturn
}
```

#### Computation

In `GridOptimizer.ExtractObjectiveValue`:

```csharp
OptimizationObjective.TimeWeightedReturn => ComputeTimeWeightedReturn(candidate),
```

```csharp
private static decimal? ComputeTimeWeightedReturn(BacktestResult candidate)
{
    if (candidate.StartEquity <= 0m) return null;
    int windowBars = candidate.EquityCurve.Count;
    if (windowBars <= 0) return null;

    int barsPerYear = candidate.ScenarioConfig.BarsPerYear;
    double growthRatio = (double)(candidate.EndEquity / candidate.StartEquity);
    double exponent = (double)barsPerYear / windowBars;
    double annualised = Math.Pow(growthRatio, exponent) - 1.0;
    return (decimal)annualised;
}
```

`windowBars` is deterministically `BacktestResult.EquityCurve.Count` — the actual number of bars processed by the engine during the IS window.

### 6. Parameter Drift Score Interpretation (Requirement 6)

#### RobustnessThresholds Extension

```csharp
public decimal ParameterDriftThreshold { get; set; } = 0.6m;
```

#### RobustnessAdvisoryService Extension

A new check in `GetStructuredWarnings`:

```csharp
// Requires WalkForwardSummary to be passed or drift score on result metadata
if (driftScore > _thresholds.ParameterDriftThreshold)
{
    warnings.Add(new RobustnessWarning(
        RobustnessSeverity.High,
        "HIGH_PARAMETER_DRIFT",
        $"Parameter drift score ({driftScore:F2}) exceeds threshold ({_thresholds.ParameterDriftThreshold:F2})",
        "Strategy is highly sensitive to parameter choice — walk-forward gains may not be reproducible",
        Cause: "High drift indicates optimal parameters change significantly between windows",
        Remediation: "Reduce parameter sensitivity or use wider parameter ranges",
        CauseCategory: "ParameterFragility"));
}
```

#### UI Tooltip

The WalkForward result page displays an info icon next to the drift score with a tooltip:
> "Parameter drift measures how much the optimal parameters change between walk-forward windows. A high score (> threshold) suggests the strategy is highly sensitive to parameter choice and walk-forward gains may not be reproducible."

### 7. DSR and MinBTL in Research Checklist (Requirement 7)

#### ResearchChecklistService Extension

Two new checklist items are added to `ComputeAsync`:

**DSR Item:**
```csharp
var dsrStatus = result.DeflatedSharpeRatio switch
{
    null => ChecklistStatus.Incomplete("DSR has not been computed"),
    var dsr when dsr < _options.MinDsrThreshold =>
        ChecklistStatus.Failed($"DSR {dsr:F3} below threshold {_options.MinDsrThreshold}"),
    _ => ChecklistStatus.Passed
};
```

**MinBTL Item:**
```csharp
int minBtl = MinBtlCalculator.MinimumBarsRequired(
    result.SharpeRatio ?? 0m,
    result.TrialCount ?? 1,
    skewness,  // computed from equity curve returns
    kurtosis); // computed from equity curve returns
int actualBars = result.EquityCurve.Count;
var minBtlStatus = actualBars >= minBtl
    ? ChecklistStatus.Passed
    : ChecklistStatus.Failed($"Backtest has {actualBars} bars but MinBTL requires {minBtl}");
```

#### Configuration

```csharp
// Added to existing ResearchChecklistOptions or a new section
public decimal MinDsrThreshold { get; set; } = 0.5m;
```

### 8. Monthly Returns Computation (Requirement 8)

#### ChartComputationHelpers Modification

The `MonthlyReturn` record changes to use nullable return:

```csharp
public sealed record MonthlyReturn(int Year, int Month, decimal? ReturnPercent);
```

The `ComputeMonthlyReturns` method is updated:

```csharp
foreach (var group in grouped)
{
    var points = group.OrderBy(p => p.Timestamp).ToList();
    if (points.Count < 2)
    {
        results.Add(new MonthlyReturn(group.Key.Year, group.Key.Month, null));
        continue;
    }
    var first = points[0].TotalEquity;
    var last = points[^1].TotalEquity;
    var returnPct = first != 0m ? (last - first) / first * 100m : 0m;
    results.Add(new MonthlyReturn(group.Key.Year, group.Key.Month, returnPct));
}
```

#### MonthlyReturnsHeatmap Modification

The heatmap renders null months with a distinct "no data" visual state — a grey cell with "—" text instead of a coloured cell with "0.0%".

### 9. Research Journal UI Page (Requirement 9)

#### New Page: Journal.razor

Route: `/strategies/{id}/journal`

The page:
1. Loads `ResearchJournalEntry` records from `IResearchJournalRepository`
2. Groups entries by action type in a timeline view
3. Provides an "Add Note" dialog (modal with text area)
4. Supports filtering by action type and date range via filter chips

Stage-transition entries are created automatically by the existing `StrategyVersion` save logic when `DevelopmentStage` changes.

### 10. Compare Page Deep Linking (Requirement 10)

#### Compare.razor Modification

- On initialisation, read `ids` query parameter and pre-populate comparison
- On selection change, update URL via `NavigationManager.NavigateTo` with `replace: true`
- Invalid IDs produce a warning toast and are excluded from the loaded set

URL format: `/compare?ids=guid1,guid2,guid3`

### 11. Sensitivity Hint Display (Requirement 11)

#### ParameterGroupEditor.razor Modification

Each parameter row renders a coloured chip based on `StrategyParameterSchema.SensitivityHint`:
- `Low` → green chip
- `Medium` → amber chip
- `High` → red chip

When total combinations exceed the configured threshold AND any dimension has `High` sensitivity, an overfitting warning banner appears:
> "⚠️ Sweeping high-sensitivity parameters increases false discovery risk. Consider reducing step count or using walk-forward validation."

### 12. Tags and Notes on Result Detail (Requirement 12)

#### ResultDetail.razor Extension

A "Notes & Tags" panel is added below the metrics section:
- Notes: inline text editor with save button (calls `IRepository<BacktestResult>.SaveAsync`)
- Tags: chip input with add/remove (calls `IRepository<BacktestResult>.SaveAsync`)
- Empty state: "Add notes or tags to annotate this result"

#### BacktestList.razor Extension

Tag filter chips appear above the results table. Selecting a chip filters results to those containing the tag.

### 13. Keyboard Shortcut for Re-Run (Requirement 13)

#### KeyboardShortcutOverlay Extension

Register shortcut:
```csharp
new KeyboardShortcut("R", "Re-run scenario", context: "ResultDetail")
```

#### ResultDetail.razor Handler

```csharp
private void HandleReRun()
{
    NavigationManager.NavigateTo($"/builder?rerun={Result.RunId}");
}
```

Navigation is immediate — no confirmation dialog. The ResultDetail page has no editable state.

The shortcut is context-specific: it is only active when the current page is `ResultDetail`. On the `Compare` page, the "R" key is not registered.

### 14. Strategy Builder Draft Auto-Save (Requirement 14)

#### Draft Identity Key

Drafts are keyed by:
- `(StrategyId, StrategyVersionId)` when editing an existing strategy version
- A transient session GUID when creating a new strategy

This is implemented as a computed `DraftKey` property:

```csharp
public string DraftKey => StrategyId is not null && StrategyVersionId is not null
    ? $"{StrategyId}:{StrategyVersionId}"
    : SessionGuid;
```

#### DraftAutoSaveService

```csharp
// Web/Services/DraftAutoSaveService.cs
public sealed class DraftAutoSaveService : IDisposable
{
    private readonly IRepository<ConfigDraft> _repository;
    private Timer? _debounceTimer;
    private const int DebounceMs = 3000;

    public DateTimeOffset? LastSavedAt { get; private set; }

    public void ScheduleSave(ConfigDraft draft) { /* reset timer */ }
    private async void ExecuteSave(object? state) { /* persist + update LastSavedAt */ }
}
```

#### BuilderViewModel Extension

- On parameter change → call `DraftAutoSaveService.ScheduleSave(currentDraft)`
- On load → check for existing draft via `DraftKey`, restore if found
- Display "Draft saved" timestamp in header

#### ConfigDraft Extension

Add fields:
```csharp
string? StrategyId,
string? StrategyVersionId,
string SessionGuid  // generated on creation for new strategies
```

### 15. Obsolete Attribute Escalation (Requirement 15)

Change:
```csharp
[Obsolete("Use DataProviderConfig instead")]
```
To:
```csharp
[Obsolete("Use DataProviderConfig instead", error: true)]
```

This is a one-line change after all callers are migrated. The design ensures the build succeeds before the attribute is escalated.

### 16. Composite Strategy Condition Length Guard (Requirement 16)

#### CompositeStrategyConfigValidator Extension

```csharp
private static class ConditionLimits
{
    public const int MaxCharacterLength = 2000;
    public const int MaxNestingDepth = 50;
}
```

Validation logic:
1. Check `condition.Length > ConditionLimits.MaxCharacterLength` → error
2. Parse condition and count max operator nesting depth → error if exceeds limit

Error messages include which limit was exceeded and the actual value.

### 17. Paper Trading Session Error Resilience (Requirement 17)

#### SimulatedPaperTradingSession Modification

Wrap subscriber notification in try/catch:

```csharp
private void EmitSafely<T>(Subject<T> subject, T value, string eventType)
{
    try
    {
        subject.OnNext(value);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Subscriber exception during {EventType} emission", eventType);
        // Session state machine is NOT affected — continue running
    }
}
```

All `_barSubject.OnNext(...)` and `_tradeSubject.OnNext(...)` calls are replaced with `EmitSafely(...)`.

### 18. Source-Generated Regex in ExportValidator (Requirement 18)

#### ExportValidator Rewrite

The class becomes `partial` and uses `[GeneratedRegex]`:

```csharp
public sealed partial class ExportValidator
{
    [GeneratedRegex(@"\bstrategy\s*\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PineStrategyPattern();

    [GeneratedRegex(@"\b(int\s+)?OnInit\s*\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MqlOnInitPattern();

    [GeneratedRegex(@"\b(void\s+)?OnTick\s*\(", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MqlOnTickPattern();
}
```

All `new Regex(...)` calls and `static readonly Regex` fields are removed. Behavioral equivalence is maintained — same patterns, same match semantics.

### 19. Property-Based Test for TradeExcursionTracker (Requirement 19)

#### TradeExcursionTrackerProperties

```csharp
// UnitTests/TradeExcursionTrackerProperties.cs
[Properties(MaxTest = 100)]
public class TradeExcursionTrackerProperties
{
    // Feature: trading-research-engine, Property 9: Direction symmetry in normalized excursion terms
    [Property]
    public Property DirectionSymmetry_NormalizedExcursion()
    {
        // MAE_short(prices) / entryPrice == MFE_long(prices) / entryPrice
        // for the same price sequence and entry price
    }

    // Feature: trading-research-engine, Property 10: MAE is always non-negative
    [Property]
    public Property MaeIsNonNegative() { ... }

    // Feature: trading-research-engine, Property 11: MFE is always non-negative
    [Property]
    public Property MfeIsNonNegative() { ... }
}
```

The symmetry property is expressed in normalized terms: `MAE_short(prices) / entryPrice == MFE_long(prices) / entryPrice` within floating-point tolerance, where both MAE and MFE are non-negative values.

### 20. Integration Test for Paper Trading Replay (Requirement 20)

#### SimulatedPaperTradingSessionTests

```csharp
// IntegrationTests/SimulatedPaperTradingSessionTests.cs
public class SimulatedPaperTradingSessionTests
{
    [Fact]
    public async Task ReplayToCompletion_MetricsMatchStandardBacktest()
    {
        // 1. Load fixture CSV
        // 2. Run standard backtest
        // 3. Run paper trading session to completion
        // 4. Assert metrics match within tolerance (1e-6)
    }
}
```

Uses existing fixture CSV from `src/TradingResearchEngine.IntegrationTests/fixtures/`.

### 22. Live Polling Provider Observability (Requirement 22)

Covered in Section 2 above. The `SessionSetup` page binds to provider metrics via a scoped service that exposes the provider's observable properties.

### 23. Composite Sweep Execution Guardrail (Requirement 23)

Covered in Section 1 above. The `GridOptimizer.ValidateCompositeGrid` method enforces the hard limit at the Application layer, complementing the UX warning in Requirement 11.

## Data Flow

### Composite Parameter Sweep Flow

```
ParameterGroupEditor (UI)
  → CompositeParameterGrid (validated)
    → GridOptimizer.ValidateCompositeGrid (guardrail check)
      → WalkForwardWorkflow.RunAsync (with composite grid)
        → GenerateCombinations (clones CompositeStrategyConfig per combination)
          → BacktestEngine.RunAsync (per combination)
            → GridOptimizer.Optimize (ranks results)
```

### Draft Auto-Save Flow

```
StrategyBuilder (parameter change)
  → DraftAutoSaveService.ScheduleSave (debounce 3s)
    → IRepository<ConfigDraft>.SaveAsync (keyed by DraftKey)
      → UI header: "Draft saved at {timestamp}"

StrategyBuilder (page load)
  → IRepository<ConfigDraft>.FindByIdAsync(DraftKey)
    → Restore draft → Resume from last completed step
```

### Monthly Returns Flow

```
BacktestResult.EquityCurve
  → ChartComputationHelpers.ComputeMonthlyReturns
    → IReadOnlyList<MonthlyReturn> (with nullable ReturnPercent)
      → MonthlyReturnsHeatmap.razor
        → null months: grey "no data" cell
        → valued months: coloured cell with percentage
```

## Configuration Summary

| Option | Section | Default | Requirement |
|--------|---------|---------|-------------|
| `SweepGuardrailOptions.MaxCombinations` | `SweepGuardrails` | 10000 | 23 |
| `PollingProviderOptions.PollingInterval` | `PollingProvider` | 1 minute | 2 |
| `PollingProviderOptions.ConsecutiveFailureWarningThreshold` | `PollingProvider` | 5 | 22 |
| `RobustnessThresholds.ParameterDriftThreshold` | `RobustnessThresholds` | 0.6 | 6 |
| `ResearchChecklistOptions.MinDsrThreshold` | `ResearchChecklist` | 0.5 | 7 |
| `SweepUiOptions.CombinationWarningThreshold` | `SweepUi` | 1000 | 11 |

## Correctness Properties

### Property 9: Direction Symmetry (Normalized Excursion)

For any price sequence and entry price, `MAE_short(prices) / entryPrice == MFE_long(prices) / entryPrice` within floating-point tolerance. Both MAE and MFE are expressed as non-negative values.

- **Generator**: Random list of positive decimal prices (length 2–200), random positive entry price
- **Oracle**: Compute MAE for short, MFE for long, normalize by entry price, assert equality within 1e-10

### Property 10: MAE Non-Negativity

For any price sequence and any direction, MAE >= 0.

- **Generator**: Random prices, random direction (Long/Short)
- **Oracle**: Assert `mae >= 0`

### Property 11: MFE Non-Negativity

For any price sequence and any direction, MFE >= 0.

- **Generator**: Random prices, random direction (Long/Short)
- **Oracle**: Assert `mfe >= 0`

### Property 12: OHLC MAE Dominance

For any bar sequence, MAE computed from High/Low extremes >= MAE computed from Close prices only.

- **Generator**: Random list of BarRecords with valid OHLC constraints (Low <= Open,Close <= High)
- **Oracle**: Compare OHLC-based MAE against close-only MAE

### Property 13: OHLC MFE Dominance

For any bar sequence, MFE computed from High/Low extremes >= MFE computed from Close prices only.

- **Generator**: Random list of BarRecords
- **Oracle**: Compare OHLC-based MFE against close-only MFE

### Property 14: TimeWeightedReturn Monotonicity

For a fixed growth ratio, TimeWeightedReturn increases as windowBars decreases (shorter windows with same growth produce higher annualised returns).

- **Generator**: Random StartEquity > 0, EndEquity > StartEquity, two windowBars values where w1 < w2
- **Oracle**: `TWR(w1) > TWR(w2)`

### Property 15: Monthly Returns Round-Trip Consistency

For any equity curve, the sum of monthly returns (when all months have data) approximates the total return over the full period.

- **Generator**: Monotonically timestamped equity curve points spanning multiple months
- **Oracle**: Compound monthly returns ≈ total return within tolerance

### Property 16: CompositeParameterGrid Combination Count

For any valid grid, the total combination count equals the product of `ceil((End - Start) / Step) + 1` for each range.

- **Generator**: Random list of CompositeParameterRange with positive Step and End >= Start
- **Oracle**: Assert computed count matches expected product formula

## Testing Strategy

### Unit Tests (UnitTests project)

- `GridOptimizerTests`: TimeWeightedReturn objective, composite grid validation, guardrail enforcement
- `ChartComputationHelpersTests`: Nullable monthly returns, fewer-than-2-points case
- `ResearchChecklistServiceTests`: DSR null/below-threshold/passing, MinBTL pass/fail
- `CompositeStrategyConfigValidatorTests`: Length guard, depth guard, boundary values
- `ExportValidatorTests`: Behavioral equivalence of source-generated regex patterns
- `TradeExcursionTrackerTests`: OHLC bar support, direction correctness
- `TradeExcursionTrackerProperties`: Property-based tests (Properties 9–13)
- `RobustnessAdvisoryServiceTests`: Parameter drift threshold warning

### Integration Tests (IntegrationTests project)

- `SimulatedPaperTradingSessionTests`: Replay-to-completion metric equivalence
- `PollingRestStreamingDataProviderTests`: Poll cycle, error resilience, metric exposure

### Manual Verification

- UI components (heatmap null state, sensitivity chips, keyboard shortcuts, deep linking)
- Draft auto-save restore flow
- Research journal timeline rendering
