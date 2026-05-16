# TradingResearchEngine — Strategy Research Improvement Blueprint

## Overview

The engine's current foundation is solid: look-ahead bias is eliminated, numeric correctness is
verified, and the clean-architecture separation (strategies emit signals only, sizing/risk/execution
are separate concerns) is the right model. V3's identity and study concepts address the biggest
product gaps. This document focuses on what should be **added, expanded, or changed at the
research and analytical layer** to make the engine a genuinely powerful strategy development tool —
not just a backtest runner.

The improvements are grouped into six areas:

1. **Validation methodology** — the tests themselves
2. **Overfitting detection** — quantitative anti-overfitting tools
3. **Regime awareness** — market context in every analysis
4. **Execution realism** — the gap between backtest and live
5. **Strategy creation workflow** — guided research from hypothesis to confidence
6. **Analytics and reporting** — making results actionable

---

## 1. Validation Methodology

### What the engine currently has

The existing research workflows are: Monte Carlo (path simulation), Walk-Forward (rolling
in-sample/out-of-sample windows), Parameter Stability, Sensitivity Analysis, and Regime
Segmentation. These are a strong starting set.

### What should be added or expanded

#### Combinatorial Purged Cross-Validation (CPCV)

Walk-forward uses a single train/test split path, which can still produce a lucky OOS result if the
test windows happen to favour the strategy. CPCV generates all possible combinations of
training/test splits across N non-overlapping folds and measures the **Probability of Backtest
Overfitting (PBO)** — the fraction of paths where OOS performance is worse than the IS median.
[web:43][web:49] A robust strategy has a PBO below 5%. A PBO above 25% is a strong signal of
curve-fitting regardless of how good the headline backtest looks.

The PBO calculation from Bailey, Borwein, Lopez de Prado & Zhu is the accepted standard for
quantifying this risk: with 45 independent backtests on random data, chance alone will produce one
Sharpe > 1.5. [web:42] The engine should implement CPCV as a study type and surface PBO as a
headline metric alongside the existing robustness verdict.

**Domain model addition:**
```csharp
public enum StudyType {
    MonteCarlo,
    WalkForward,
    AnchoredWalkForward,       // NEW
    CombinatorialPurgedCV,     // NEW
    Sensitivity,
    ParameterSweep,
    Realism,
    ParameterStability,
    RegimeSegmentation
}
```

#### Anchored Walk-Forward

The existing walk-forward rolls both the start and end of the training window forward. Anchored
walk-forward keeps the start fixed and expands the training window — this tests whether the
strategy degrades as more data is added, which is the more realistic live-trading scenario. [web:37]
It should be offered as an option on the Walk-Forward study alongside the existing rolling mode.

Add a field to the study configuration:
```csharp
public enum WalkForwardMode { Rolling, Anchored }
```

#### Held-Out Final Test Set

The engine has no concept of a permanent, untouched test set. The critical rule of OOS testing is
that the held-out data may only be used **once** — if the user modifies the strategy based on
OOS results and retests, it becomes training data by contamination. [web:48] The engine should
enforce this at the data layer:

- When a strategy is created, the builder should prompt the user to designate the last N% of the
  date range as a sealed test set (recommended: 20-30%, minimum 1 year).
- The sealed test set is locked — it cannot be used in walk-forward windows, sensitivity analysis,
  or parameter sweeps.
- It becomes available only via a one-time "Final Validation" run, clearly labelled.
- After a Final Validation run, the strategy is marked as "Validated" and further parameter
  changes trigger a warning: "You have already run a Final Validation. Changing parameters
  invalidates it."

This is a workflow discipline feature as much as an engine feature.

#### Deflated Sharpe Ratio (DSR)

The raw Sharpe ratio is meaningless without knowing how many strategies were tested to find it.
The Deflated Sharpe Ratio adjusts for the number of trials, the skewness, and the kurtosis of
returns to give the probability that the true Sharpe exceeds a benchmark. [web:42] This should be
computed automatically on every run result:

```
PSR(SR*) = Φ[(SR - SR*) × √(T-1) / √(1 - γ₃SR + (γ₄-1)/4 × SR²)]
```

where γ₃ is return skewness, γ₄ is kurtosis, T is the number of observations, and SR* is the
expected maximum Sharpe from N independent trials.

The DSR should appear as a secondary metric on the Run Detail screen next to the raw Sharpe, with
a tooltip explaining what it represents. A DSR below 0.95 should trigger a "Possible overfitting"
warning badge.

**New field on BacktestResult:**
```csharp
public decimal? DeflatedSharpeRatio { get; init; }
public int? TrialCount { get; init; }  // Number of prior runs on this strategy version
```

#### Minimum Backtest Length Check

The engine currently warns on trades < 30 but not on data length. The Minimum Backtest Length
formula (MinBTL from Lopez de Prado) computes the minimum history needed to be 95% confident a
given Sharpe is not spurious, given N trials. [web:42] This should be a automatic check:

```csharp
// In BacktestResultAnalyzer:
public static int MinimumBarsRequired(decimal observedSharpe, int trialCount,
    decimal skewness, decimal kurtosis)
{
    // MinBTL = (SR / SR*)² × correction factor
    // Returns minimum bar count required for 95% confidence
}
```

If the actual bar count is below MinBTL, surface the warning: "This backtest is too short to
be statistically significant given the number of strategies you have tested. Need at least N
more bars."

---

## 2. Overfitting Detection

### Fragility Score — Expand It

The existing fragility score (parameter island detection) is the right concept but should be
expanded. The current approach perturbs parameters and measures Sharpe degradation. The expanded
version should:

1. **Use a 2D performance surface**, not just 1D perturbations. For a strategy with parameters
   `fastPeriod` and `slowPeriod`, produce a heatmap of Sharpe values across the full parameter
   grid. A fragile strategy has a sharp peak surrounded by poor values. A robust strategy has a
   flat plateau. [web:42]

2. **Report the "width" of the performance peak** as the fragility score: the area of the
   parameter space where Sharpe remains within 20% of the optimal value. A narrow peak = high
   fragility. A wide plateau = low fragility.

3. **Show this as a heatmap chart** on the Study Detail page for any parameter sweep study.
   This makes overfitting immediately visual.

**New study type addition:**
```csharp
public record ParameterSurfaceStudy : StudyRecord
{
    public string PrimaryParameter { get; init; }   // X axis
    public string SecondaryParameter { get; init; } // Y axis
    public decimal[,] SharpeMatrix { get; init; }   // the performance surface
    public decimal FragilityIndex { get; init; }    // area of robust parameter space
}
```

### Overfitting Budget Tracking

Every time a user runs a parameter sweep or sensitivity analysis on a strategy version, they are
effectively spending from their "overfitting budget." The more trials they run on the same data, the
less trustworthy any good result becomes. The engine should track this:

```csharp
// On StrategyVersion:
public int TotalTrialsRun { get; set; }  // incremented on every run and sweep

// DSR is then always calculated with this trial count
```

Show on the Strategy Hub: "You have run 47 trials on this strategy version. A Sharpe of 1.4 is
expected from random chance alone after 45 trials. Consider running a Final Validation on a
held-out test set."

---

## 3. Regime Awareness

### What exists

Regime segmentation exists as an analysis workflow — it divides the backtest into market regimes
(trend, mean-reversion, high/low volatility) and reports performance per segment.

### What should be expanded

#### Regime-Conditional Walk-Forward

The existing walk-forward does not account for regime. A strategy that works in trending markets
will appear to fail in a walk-forward window that happens to cover a mean-reverting period — not
because the strategy is bad, but because it is being tested in the wrong regime. [web:46]

Regime-conditional walk-forward should:
1. Label each walk-forward window with its dominant regime (using the existing regime detector)
2. Group OOS window results by regime: "Trending OOS Sharpe: 1.6 (4 windows). Mean-reverting OOS
   Sharpe: -0.2 (3 windows)"
3. Flag when a strategy's OOS performance depends heavily on a single regime — this is a regime
   overfitting risk

#### Regime Filter as a First-Class Parameter

Currently, regime detection is a post-hoc analysis. It should be offered as a strategy-level filter
in the builder:

- "Apply this strategy only in trending regimes (ADX > 25)"
- "Pause this strategy in high-volatility regimes (ATR percentile > 80)"

This makes regime awareness a live concern, not just an analytical curiosity.

#### Market Stress Periods

Add a specific test for performance during known stress periods. Ship with a `data/stress-periods.json`
file containing:

```json
[
    { "name": "COVID Crash", "start": "2020-02-20", "end": "2020-04-01" },
    { "name": "2022 Rate Hike Regime", "start": "2022-01-01", "end": "2022-12-31" },
    { "name": "GFC", "start": "2008-09-01", "end": "2009-03-31" },
    { "name": "DotCom Unwind", "start": "2000-03-01", "end": "2002-10-31" }
]
```

Every run result should automatically check how much of the backtest period overlaps with stress
periods and flag whether performance during those periods is materially worse than the baseline.
A strategy that looks great but crashes during every stress period is not prop-firm safe.

---

## 4. Execution Realism

### What exists

Execution realism is modelled (slippage, commission, session awareness, position sizing policies).
The `ExecutionRealismProfile` approach is correct.

### What should be expanded

#### Spread Modelling Per Session

The current slippage model is uniform. In reality, spreads widen significantly at session opens,
close to major economic announcements, and during low-liquidity periods. The engine should support
a `SessionSpreadModel` that increases effective slippage during:

- 00:00–01:00 UTC (low liquidity)
- Economic announcement windows (news calendar integration, or a configurable list)
- Rollover times (for futures and some forex pairs)

This is the most common source of live-vs-backtest divergence for intraday strategies.

#### Partial Fill Modelling

The current engine assumes all orders fill in full. At large position sizes or in thin markets, this
is unrealistic. Add a `PartialFillProbability` parameter to the realism profile:

```csharp
public sealed record PositionSizingPolicy
{
    // existing fields ...
    public decimal PartialFillProbabilityPercent { get; init; } = 0;  // 0-100
    public decimal MaxFillFractionPerBar { get; init; } = 1.0m;        // 0.0-1.0
}
```

When partial fills are enabled, a position opens over multiple bars, affecting entry price average
and timing. This is material for strategies that take large positions in relatively illiquid pairs.

#### Execution Realism Study

Add a dedicated "Realism Impact" study type that runs the same strategy three times with three
realism profiles (Fast/Standard/Conservative) and produces a comparison table:

| Metric         | Fast Research | Standard | Conservative |
|----------------|--------------|----------|--------------|
| Sharpe         | 1.82         | 1.41     | 0.93         |
| Max DD         | 7.2%         | 8.3%     | 11.1%        |
| Net Profit     | 38.4%        | 29.7%    | 18.2%        |
| Cost Drag      | —            | -8.7pp   | -20.2pp      |

A strategy that degrades severely under realistic costs should be flagged: "This strategy's edge is
highly sensitive to execution costs. Sharpe falls 49% under conservative assumptions."

---

## 5. Strategy Creation Workflow

### The Research Loop Problem

The current V3 spec describes a linear flow: template → configure → run → study → evaluate.
In practice, strategy research is iterative: hypothesis → test → discover → revise → test again.
The engine should model and support this loop explicitly.

#### Strategy Development Stages

Add a `DevelopmentStage` field to `StrategyIdentity`:

```csharp
public enum DevelopmentStage
{
    Hypothesis,      // idea only, no runs yet
    Exploring,       // early runs, still changing parameters frequently
    Optimizing,      // parameter sweep phase
    Validating,      // walk-forward and robustness phase
    FinalTest,       // sealed test set has been run exactly once
    Retired          // no longer under development
}
```

Show this prominently on the Strategy Hub and Library card. The dashboard should group strategies
by stage: "2 in Validating, 1 awaiting Final Test, 3 in Exploring."

This makes the research pipeline visible and prevents users from confusing exploration results with
validated results.

#### Hypothesis Tracking

Before running any backtest, the builder should prompt: "What is your hypothesis for why this
strategy should work?" and store the answer as a `Hypothesis` string on the strategy. This is a
research discipline feature — it forces the user to articulate a reason before seeing results,
which significantly reduces post-hoc rationalization of lucky backtests.

Display the hypothesis prominently at the top of the Strategy Hub and Run Detail screens.

#### Automated Research Checklist

After a backtest run, the engine should compute and display a structured research checklist:

```
Research Progress for EURUSD Mean Reversion v3
─────────────────────────────────────────────
✅ Initial backtest completed (Sharpe 1.42)
✅ Monte Carlo robustness: Robust (P10 positive, ruin < 5%)
⬜ Walk-forward validation not yet run
⬜ Regime sensitivity not yet checked
⬜ Execution realism impact not yet measured
⬜ Parameter surface not yet mapped
⬜ Final held-out test not yet run
⬜ Prop firm evaluation not yet completed
─────────────────────────────────────────────
Confidence Level: LOW (1 of 7 checks passed)
```

The "Confidence Level" score advances with each completed check. This gives users a clear sense
of how much validation remains and prevents premature live trading based on a single backtest.

---

## 6. Analytics and Reporting

### New Metrics to Add

The current metric set covers the essentials. The following additions are high-value for
strategy development decisions:

| Metric | Purpose | Where to show |
|---|---|---|
| **Deflated Sharpe Ratio** | Sharpe adjusted for trial count | KPI tile, primary |
| **Profit Factor** | Gross profit / gross loss | Secondary tab |
| **Expectancy ($ per trade)** | Average expected value per trade | Secondary tab |
| **MAE Distribution** | Maximum Adverse Excursion histogram | Trades tab |
| **MFE Distribution** | Maximum Favourable Excursion histogram | Trades tab |
| **Consecutive Loss Max** | Longest losing streak | Risk section |
| **Recovery Factor** | Net profit / max drawdown | Secondary tab |
| **Calmar Ratio** | Annual return / max drawdown | Secondary tab |
| **Average Holding Period** | Mean bars in trade | Trades tab |
| **K-Ratio** | Already exists — promote to primary KPI | KPI tile primary |
| **DSR** | Already described above | KPI tile primary |
| **PBO (from CPCV)** | Probability of backtest overfitting | Robustness section |

#### MAE/MFE Analysis — Expand This

MAE/MFE (Maximum Adverse/Favourable Excursion) is one of the most useful tools for improving
a strategy and is currently underutilised. It answers:
- "Am I exiting too early? (MFE much larger than actual profit)"
- "Are my stops too tight? (MAE frequently exceeds initial stop)"
- "What is the true risk of a typical trade? (MAE distribution)"

The Trades tab should show a scatter plot of MAE vs MFE for all trades, colour-coded by win/loss.
This chart alone often reveals more about how to improve a strategy than any aggregate metric.

### Run Comparison — Upgrade

The current comparison is a flat table. When comparing two versions of the same strategy, the
engine knows they share the same underlying logic and should highlight **meaningful differences**:

```
Version 2 vs Version 3 — EURUSD Mean Reversion

Parameter changes:
  fastPeriod: 10 → 12  (+20%)
  lookback:   20 → 20  (unchanged)

Performance changes:
  Sharpe:  1.18 → 1.42  (+0.24)  ▲ improved
  Max DD:  11.2% → 8.3%  (-2.9pp) ▲ improved
  Trades:  31 → 23         (-8)   ▼ fewer samples

⚠ Fewer trades in v3 reduces statistical confidence.
```

The delta view — what changed and in what direction — is more useful than two side-by-side columns
of numbers.

### Export Format

The existing Markdown export should be expanded to include a structured JSON export of the full
result for external use (Excel, Python, Jupyter). This makes the engine composable with external
tools rather than a closed system.

```
[Export ▼]
  → Markdown Report
  → JSON (full result + config)
  → CSV (trade log only)
  → CSV (equity curve)
```

---

## Prioritised Roadmap Addition (V3.3 and V3.4)

| Priority | Feature | Complexity | Impact |
|---|---|---|---|
| 🔴 Critical | Sealed held-out test set + DevelopmentStage lifecycle | Medium | Prevents premature live trading |
| 🔴 Critical | Deflated Sharpe Ratio on every run result | Low | Immediate overfitting signal |
| 🔴 Critical | Automated research checklist / confidence score | Low | Guides users through the loop |
| 🟠 High | CPCV + PBO metric as new study type | High | Gold standard OOS validation |
| 🟠 High | Parameter performance surface (2D heatmap) | Medium | Visual fragility detection |
| 🟠 High | Anchored walk-forward mode | Low | More realistic OOS scenario |
| 🟠 High | MAE/MFE scatter chart on Trades tab | Medium | Improves trade management decisions |
| 🟡 Medium | Execution Realism Impact study (3-profile comparison) | Medium | Live trading readiness |
| 🟡 Medium | Regime-conditional walk-forward | High | Prevents regime overfitting |
| 🟡 Medium | Run comparison delta view | Low | Version iteration clarity |
| 🟡 Medium | Stress period overlay on equity curve | Low | Risk awareness |
| 🟢 Lower | MinBTL check and warning | Low | Statistical hygiene |
| 🟢 Lower | Hypothesis field on strategy | Low | Research discipline |
| 🟢 Lower | Overfitting trial budget counter | Low | Transparency |

---

## Summary

The most important gap is not a missing feature — it is the **absence of a structured research
lifecycle** that prevents users from confusing exploration with validation. The engine produces
excellent numbers; it does not yet tell users what those numbers mean in the context of how they
were obtained. Adding the development stage model, the automated checklist, the sealed test set,
and the Deflated Sharpe Ratio addresses this gap at low engineering cost and high user impact.
The more advanced additions — CPCV, 2D parameter surfaces, regime-conditional walk-forward, and
MAE/MFE analysis — add genuine methodological depth that separates this tool from generic
backtesting platforms.
