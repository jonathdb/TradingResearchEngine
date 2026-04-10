# Design Document — Strategy Library Curation & Research Alignment

## Overview

This design curates the built-in strategy catalog from 8 overlapping implementations down to 6 distinct quant archetypes, adds a lightweight `StrategyDescriptor` metadata record to the existing template system, enriches the Strategy Builder and Strategy Detail UX with family/hypothesis context, and replaces the sample scenario files with a clean 1:1 mapping to the curated catalog.

All changes live in Application (strategies, templates, descriptor), Web (builder/detail UX), and samples. Core is untouched. No new NuGet packages are required.

---

## Architecture

### Layer Ownership

```
Application/Strategies/   — 3 new strategy classes, 3 kept, 5 removed
Application/Strategy/     — StrategyDescriptor record, StrategyFamily constants,
                            StrategyTemplate amended with optional Descriptor
                            DefaultStrategyTemplates updated
Web/Components/Pages/     — StrategyBuilder.razor and StrategyDetail.razor amended
samples/scenarios/        — 6 scenario JSON files (replaces 7)
UnitTests/                — New test classes for new strategies, removed tests for deleted strategies
```

### Dependency Rule (preserved)

```
Core ← Application ← Infrastructure ← { Cli, Api, Web }
```

No Core changes. No Infrastructure changes beyond removing any references to deleted strategy types in DI registration (if any exist — the registry is assembly-scanned, so this is unlikely).

### Kept Strategy Type Keys (unchanged)

The following strategies are "kept, reframed" — their runtime type keys and persisted `StrategyType` strings remain unchanged. Only their descriptor display names, descriptions, and hypotheses change:

- `stationary-mean-reversion` → `StationaryMeanReversionStrategy` (class name unchanged)
- `macro-regime-rotation` → `MacroRegimeRotationStrategy` (class name unchanged)
- `donchian-breakout` → `DonchianBreakoutStrategy` (class name unchanged, kept as-is)

---

## Components and Interfaces

### StrategyDescriptor (Application/Strategy)

```csharp
namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Lightweight metadata for a built-in strategy, surfaced in the Builder
/// and Strategy Detail UX. Lookup is by StrategyType string match.
/// Missing descriptor is non-fatal — UI falls back to raw type name.
/// </summary>
public sealed record StrategyDescriptor(
    string StrategyType,
    string DisplayName,
    string Family,
    string Description,
    string Hypothesis,
    string? BestFor = null,
    string[]? SuggestedStudies = null);
```

### StrategyFamily (Application/Strategy)

```csharp
namespace TradingResearchEngine.Application.Strategy;

/// <summary>String constants for strategy family classification.</summary>
public static class StrategyFamily
{
    public const string Trend = "Trend";
    public const string MeanReversion = "MeanReversion";
    public const string Breakout = "Breakout";
    public const string RegimeAware = "RegimeAware";
    public const string Benchmark = "Benchmark";
}
```

### StrategyTemplate Amendment

```csharp
// Existing record gains one optional trailing parameter
public sealed record StrategyTemplate(
    string TemplateId,
    string Name,
    string Description,
    string StrategyType,
    string TypicalUseCase,
    Dictionary<string, object> DefaultParameters,
    string RecommendedTimeframe,
    ExecutionRealismProfile RecommendedProfile = ExecutionRealismProfile.StandardBacktest,
    StrategyDescriptor? Descriptor = null  // NEW — backwards-compatible
) : IHasId;
```

### DefaultStrategyTemplates (revised)

The 6 templates with full descriptors:

| TemplateId | Name | StrategyType | Family | Hypothesis |
|---|---|---|---|---|
| tpl-vol-trend | Volatility-Scaled Trend | volatility-scaled-trend | Trend | Persistent directional moves continue long enough for trend entries to overcome costs when sized by volatility |
| tpl-zscore-mr | Z-Score Mean Reversion | zscore-mean-reversion | MeanReversion | Short-term price dislocations around a rolling equilibrium tend to mean-revert in non-trending regimes |
| tpl-donchian | Donchian Breakout | donchian-breakout | Breakout | Range expansion after compression signals the start of a sustained move |
| tpl-stationary-mr | Stationary Mean Reversion | stationary-mean-reversion | MeanReversion | Mean reversion signals are only reliable when the return series is statistically stationary |
| tpl-regime-rotation | Macro Regime Rotation | macro-regime-rotation | RegimeAware | No single signal family dominates across all regimes; switching behavior by regime improves robustness |
| tpl-buy-hold | Buy & Hold Baseline | baseline-buy-and-hold | Benchmark | Markets have a positive long-term drift; any active strategy must outperform passive exposure to justify its complexity |

### Descriptor → Suggested Studies Mapping

| Strategy | SuggestedStudies | BestFor |
|---|---|---|
| volatility-scaled-trend | WalkForward, AnchoredWalkForward, MonteCarlo, RegimeSegmentation | Trending markets with sustained directional moves |
| zscore-mean-reversion | Sensitivity, RegimeSegmentation, MonteCarlo, ParameterStability | Range-bound or mean-reverting instruments |
| donchian-breakout | WalkForward, MonteCarlo, Sensitivity | Markets with periodic range expansion |
| stationary-mean-reversion | RegimeSegmentation, Sensitivity, MonteCarlo, ParameterStability | Instruments with statistically stationary return series |
| macro-regime-rotation | RegimeSegmentation, WalkForward, AnchoredWalkForward, Realism | Broad market indices with regime shifts |
| baseline-buy-and-hold | MonteCarlo | Benchmark comparison for any active strategy |

---

## New Strategy Implementations

### VolatilityScaledTrendStrategy

```
Family: Trend
Type key: volatility-scaled-trend
Parameters: fastPeriod (int, 10), slowPeriod (int, 50), atrPeriod (int, 14)

Signal logic:
  - Fast SMA and Slow SMA computed via O(1) rolling sum accumulators
  - ATR computed via Wilder smoothing (O(1) per bar after warmup)
  - Entry: fastSMA > slowSMA AND not already long → Long signal, Strength = Close / ATR
  - Exit: fastSMA <= slowSMA AND long → Flat signal

Warmup: max(slowPeriod, atrPeriod) bars before first signal

ATR computation:
  - First atrPeriod bars: simple average of True Range
  - Subsequent bars: ATR = (prevATR * (atrPeriod - 1) + currentTR) / atrPeriod
  - True Range = max(High - Low, |High - prevClose|, |Low - prevClose|)
```

### ZScoreMeanReversionStrategy

```
Family: MeanReversion
Type key: zscore-mean-reversion
Parameters: lookback (int, 30), entryThreshold (decimal, 2.0), exitThreshold (decimal, 0.0)

Signal logic:
  - Rolling SMA and StdDev via O(1) sum and sum-of-squares accumulators
  - z = (Close - SMA) / StdDev
  - Entry: z < -entryThreshold AND not already long → Long signal
  - Exit: z > exitThreshold AND long → Flat signal

Warmup: lookback bars before first signal
```

### BaselineBuyAndHoldStrategy

```
Family: Benchmark
Type key: baseline-buy-and-hold
Parameters: warmupBars (int, 1)

Signal logic:
  - Count bars
  - On bar warmupBars: emit Direction.Long, Strength = 1.0m (neutral constant — benchmark sizing should not scale with price level)
  - Never emit Flat
  - No further signals after entry

Warmup: warmupBars bars
```

---

## Strategy Builder UX Changes

### Step 1 — Template Card Enhancement

Current card shows: Name, Description, TypicalUseCase chip.

New card shows:
```
┌─────────────────────────────────────┐
│ [Family Badge]  Display Name        │
│                                     │
│ Short description text              │
│                                     │
│ 💡 "Hypothesis text in italics"     │
│                                     │
│ [TypicalUseCase chip]               │
└─────────────────────────────────────┘
```

Family badge colors:
- Trend → `Color.Info` (blue)
- MeanReversion → `Color.Secondary` (purple)
- Breakout → `Color.Warning` (orange)
- RegimeAware → `Color.Tertiary` (teal)
- Benchmark → `Color.Default` (grey)

Implementation: read `Descriptor` from the `StrategyTemplate`. If null, render current layout (no family badge, no hypothesis). If `SuggestedStudies` is null or empty, render all study buttons without recommendation markers.

### Step 5 — Hypothesis Prefill

When saving a new strategy from a template that has a `Descriptor`:
- Pre-fill `_hypothesisText` with `Descriptor.Hypothesis`
- Show it in an editable text field on the Review step
- User can modify before saving
- On clone/edit flows where `StrategyIdentity.Hypothesis` is already set, do NOT overwrite

### Implementation approach

Add a `_hypothesis` field to the builder `@code` block. In `SelectTemplate()`, set `_hypothesis = tpl.Descriptor?.Hypothesis ?? ""`. In `SaveStrategy()`, pass `_hypothesis` to the `StrategyIdentity` constructor.

---

## Strategy Detail UX Changes

### Header Band — Family Badge

Add a family badge chip next to the existing strategy type chip:

```razor
@if (_descriptor is not null)
{
    <MudChip T="string" Size="MudBlazor.Size.Small" Color="@FamilyColor">
        @_descriptor.Family
    </MudChip>
}
```

Lookup: on `LoadVersionData()`, find the matching `StrategyDescriptor` from `DefaultStrategyTemplates.All` by `StrategyType`. Store as `_descriptor`. If not found, `_descriptor` is null and the badge doesn't render.

### Research Tab — Suggested Studies

In the "LAUNCH STUDY" bar, if `_descriptor?.SuggestedStudies` contains the study type name, add a small star icon or "(Recommended)" label next to the button:

```razor
<MudButton ...>
    Monte Carlo
    @if (_descriptor?.SuggestedStudies?.Contains("MonteCarlo") == true)
    {
        <MudIcon Icon="@Icons.Material.Filled.Star" Size="MudBlazor.Size.Small"
                 Class="ml-1" Style="color: var(--mud-palette-warning)" />
    }
</MudButton>
```

---

## Sample Scenario Files

### File Mapping

| File | Strategy Type | Description |
|---|---|---|
| volatility-scaled-trend-spy.json | volatility-scaled-trend | Volatility-scaled trend following on SPY daily. ATR-normalized signal strength. |
| zscore-meanrev-spy.json | zscore-mean-reversion | Z-score mean reversion on SPY daily. Buy at z < -2, exit at mean. |
| donchian-breakout-spy.json | donchian-breakout | 20-day Donchian Channel Breakout on SPY daily. Long-only trend follower. |
| stationary-meanrev-spy.json | stationary-mean-reversion | Stationary Mean Reversion with ADF test on SPY daily. |
| macro-regime-spy.json | macro-regime-rotation | Macro Regime Rotation on SPY daily. Vol/trend/momentum regime detection. |
| baseline-buy-and-hold-spy.json | baseline-buy-and-hold | Buy-and-hold benchmark on SPY daily. Passive exposure baseline. |

All use: `csv` provider, `samples/data/spy-daily.csv`, `InitialCash: 100000`, `AnnualRiskFreeRate: 0.05`, `ZeroSlippageModel`, `ZeroCommissionModel`.

After cleanup, the `samples/scenarios/` directory SHALL contain exactly 6 files — one per Tier 1 strategy.

---

## Data Models

No new data models beyond `StrategyDescriptor` and `StrategyFamily`. No Core changes. No persistence changes.

---

## Error Handling

No new error handling patterns. Removed strategies produce `StrategyNotFoundException` via existing `StrategyRegistry.Resolve` behavior.

---

## Testing Strategy

### New Unit Tests (UnitTests project)

| Test Class | Validates | Requirements |
|---|---|---|
| `VolatilityScaledTrendStrategyTests` | Warmup, entry signal, exit signal, Strength = Close/ATR, rolling accumulator correctness | 2.1–2.8 |
| `ZScoreMeanReversionStrategyTests` | Warmup, entry at z < -threshold, exit at z > exitThreshold, rolling accumulator correctness | 3.1–3.6 |
| `BaselineBuyAndHoldStrategyTests` | Single Long signal after warmup with Strength = 1.0, no Flat signal ever emitted | 4.1–4.4 |
| `StrategyDescriptorTests` | Descriptor lookup by type, null fallback, all 6 descriptors populated | 5.1–5.6 |

### Removed Tests

Tests for `SmaCrossoverStrategy`, `BollingerBandsStrategy`, `MeanReversionStrategy`, `BreakoutStrategy`, and `RsiStrategy` are removed along with the strategy classes.

### Existing Tests — Impact Assessment

- Property-based tests in UnitTests that reference specific strategy types (e.g. SmaCrossoverStrategy in integration-style tests) must be updated to use a retained strategy type.
- V2 regression tests in `UnitTests/V2Regression/` do not reference specific strategy implementations by name — they construct test strategies directly. No changes needed.

---

## Folder Structure Changes

```
src/TradingResearchEngine.Application/
  Strategy/
    StrategyDescriptor.cs          # NEW
    StrategyFamily.cs              # NEW
    StrategyTemplate.cs            # AMENDED (Descriptor field)
  Strategies/
    VolatilityScaledTrendStrategy.cs    # NEW
    ZScoreMeanReversionStrategy.cs      # NEW
    BaselineBuyAndHoldStrategy.cs       # NEW
    SmaCrossoverStrategy.cs             # REMOVED
    BollingerBandsStrategy.cs           # REMOVED
    MeanReversionStrategy.cs            # REMOVED
    BreakoutStrategy.cs                 # REMOVED
    RsiStrategy.cs                      # REMOVED
    DonchianBreakoutStrategy.cs         # KEPT
    StationaryMeanReversionStrategy.cs  # KEPT
    MacroRegimeRotationStrategy.cs      # KEPT

src/TradingResearchEngine.UnitTests/
  Strategies/
    VolatilityScaledTrendStrategyTests.cs    # NEW
    ZScoreMeanReversionStrategyTests.cs      # NEW
    BaselineBuyAndHoldStrategyTests.cs       # NEW
    StrategyDescriptorTests.cs               # NEW
    SmaCrossoverStrategyTests.cs             # REMOVED (if exists)
    BollingerBandsStrategyTests.cs           # REMOVED (if exists)
    MeanReversionStrategyTests.cs            # REMOVED (if exists)
    BreakoutStrategyTests.cs                 # REMOVED (if exists)
    RsiStrategyTests.cs                      # REMOVED (if exists)

src/TradingResearchEngine.Web/
  Components/Pages/Strategies/
    StrategyBuilder.razor          # AMENDED (template card, hypothesis prefill)
    StrategyDetail.razor           # AMENDED (family badge, suggested studies)

samples/scenarios/
    volatility-scaled-trend-spy.json    # NEW
    zscore-meanrev-spy.json             # NEW
    baseline-buy-and-hold-spy.json      # NEW
    donchian-breakout-spy.json          # KEPT (updated description)
    stationary-meanrev-spy.json         # KEPT (updated description)
    macro-regime-spy.json               # KEPT (updated description)
    sma-crossover-spy.json              # REMOVED
    sma-crossover-dukascopy.json        # REMOVED
    sma-crossover-yahoo.json            # REMOVED
    bollinger-bands-spy.json            # REMOVED
```
