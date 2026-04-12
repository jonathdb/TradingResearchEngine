# V5 Quant Assumptions

This document describes the execution realism models, gap and volume handling, bar-resolution limitations, directional scope, overfitting defense methodology, and benchmarking approach used by TradingResearchEngine.

---

## Execution Realism Models

### Slippage Models

All slippage models implement `ISlippageModel` and return a non-negative price adjustment applied adversely to the fill price (added for buys, subtracted for sells).

| Model | Class | Behavior |
|-------|-------|----------|
| Zero | `ZeroSlippageModel` | Returns 0. No slippage applied. Default fallback. |
| Fixed Spread | `FixedSpreadSlippageModel` | Returns a configured fixed half-spread (e.g. $0.01) on every fill, regardless of market conditions. |
| Percent of Price | `PercentOfPriceSlippageModel` | Returns `price × basisPoints / 10,000`. Default: 5 bps (0.05%). Scales with price level. |
| ATR-Scaled | `AtrScaledSlippageModel` | Returns `rollingATR × atrFraction`. Default: 14-period ATR × 10%. Adapts to current volatility using Wilder smoothing. |
| Session-Aware | `SessionAwareSlippageModel` | Applies different slippage rates depending on whether the fill occurs during or outside active session hours. |
| Volatility Bucket | `VolatilityBucketSlippageModel` | Assigns slippage from discrete volatility buckets based on recent realized volatility. |

### Commission Models

All commission models implement `ICommissionModel` and return a non-negative USD fee.

| Model | Class | Behavior |
|-------|-------|----------|
| Zero | `ZeroCommissionModel` | Returns 0. No commission charged. Default fallback. |
| Per Trade | `PerTradeCommissionModel` | Returns a fixed flat fee per trade regardless of size (e.g. $4.95). |
| Per Share | `PerShareCommissionModel` | Returns `feePerShare × quantity` (e.g. $0.005/share). Scales with position size. |

### Realism Profiles

Profiles bundle slippage, commission, and session settings into named presets via `ExecutionRealismProfile`:

| Profile | Slippage | Commission | Use Case |
|---------|----------|------------|----------|
| `FastResearch` | Zero | Zero | Quick hypothesis testing with no cost friction. |
| `StandardBacktest` | FixedSpread | PerTrade | Baseline evaluation with moderate, predictable costs. |
| `BrokerConservative` | AtrScaled | PerShare | Conservative realism with volatility-aware costs and session rules. |

---

## Gap Handling

The `SimulatedExecutionHandler` detects overnight/weekend price gaps and adjusts fill prices accordingly.

### Detection

A gap is detected when the absolute difference between the current bar's Open and the previous bar's Close exceeds `2× rollingATR` (14-period ATR). The threshold constant is `ExecutionRealismDefaults.GapAtrMultiple = 2.0`.

### Fill Adjustment

When a gap is detected and a pending stop or limit order would have been triggered during the gap:
- The fill price is set to the **gap bar's Open price**, not the stop/limit trigger price.
- A realism advisory is recorded on `BacktestResult.RealismAdvisories`.

Market orders are not gap-adjusted — they fill at the bar's standard price.

### Rationale

In live markets, stop orders triggered during a gap fill at the opening price (or worse), not at the stop price. This model prevents unrealistically favorable fills during gap events.

---

## Volume Constraint Behavior

V5 adds volume-aware fill constraints via `ExecutionOptions.MaxFillPercentOfVolume`.

### Cap Enforcement

When `MaxFillPercentOfVolume` is set (e.g. `0.05` for 5%), the fill quantity is capped at `MaxFillPercentOfVolume × bar.Volume`. If the requested quantity exceeds this cap, the fill is reduced and a realism advisory is recorded.

### Volume Warning

Regardless of whether `MaxFillPercentOfVolume` is configured, any fill exceeding **10% of bar volume** (`ExecutionRealismDefaults.VolumeWarningThreshold = 0.10`) triggers:
- A `VolumeWarning` log event
- A realism advisory on `BacktestResult.RealismAdvisories`

### Limitations

- Volume constraints only apply to bar data. Tick-level fills do not have volume context.
- The model assumes bar volume is representative. Thinly traded instruments or aggregated bars may produce misleading volume figures.
- Partial fills from volume capping do not generate separate fill events — the quantity is simply reduced.

---

## Bar-Resolution Caveats and Known Limitations

### Intra-Bar Price Path Unknown

Bar data provides Open, High, Low, Close, and Volume. The actual intra-bar price path is unknown. This means:
- Stop and limit orders assume the worst-case intra-bar path (High reached before Low, or vice versa) for fill determination.
- Multiple orders on the same bar may interact in ways that differ from live execution.

### Fill Timing

The default `FillMode.NextBarOpen` fills orders at the next bar's Open price, eliminating same-bar look-ahead bias. `FillMode.IntraBar` uses limit/stop logic within the current bar but is subject to the intra-bar path ambiguity above.

### BarsPerYear and Annualization

`ScenarioConfig.BarsPerYear` is the canonical source for Sharpe/Sortino annualization. Defaults by timeframe:

| Timeframe | BarsPerYear |
|-----------|-------------|
| Daily | 252 |
| H4 | 1,512 |
| H1 | 6,048 |
| M15 | 24,192 |

Mismatches between declared `Timeframe` and `BarsPerYear` produce a `TIMEFRAME_MISMATCH` preflight warning.

### No Sub-Bar Execution Simulation

The engine does not simulate order book dynamics, queue priority, or partial fills from liquidity. All fills are immediate at the determined price (after slippage adjustment).

---

## Long-Only Scope and Short-Selling Roadmap

### V5: Long-Only with Exhaustive Switch Coverage

V5 adds `Direction.Short` to the `Direction` enum to force exhaustive handling in switch expressions. However, runtime short-selling is **not supported**. The `LongOnlyGuard.EnsureLongOnly()` method throws `NotSupportedException` for `Direction.Short` and is called at all known Direction consumption points.

Strategies emit `Direction.Long` to enter and `Direction.Flat` to exit. Any strategy emitting `Direction.Short` will fail at runtime.

### V6 Roadmap

- Remove `LongOnlyGuard` calls
- Implement short-selling execution logic in `SimulatedExecutionHandler`
- Add short-specific slippage and margin models
- Update `Portfolio` for short position tracking and margin requirements

---

## Overfitting Defense Methodology

V5 systematizes overfitting detection through four complementary mechanisms.

### Deflated Sharpe Ratio (DSR)

The DSR adjusts the observed Sharpe ratio for the number of trials conducted, accounting for multiple testing bias. When `DSR < 0.95`, a "Possible Overfitting" warning badge is displayed on the result detail view.

### Minimum Backtest Length (MinBTL)

`MinBtlCalculator` computes the minimum number of bars required for a statistically meaningful backtest given the strategy's observed Sharpe ratio and the number of trials. The preflight validator warns when the available data is below this threshold.

### Trial Budget

The `ResearchChecklistService` tracks `TotalTrialsRun` per strategy version and computes a `TrialBudgetStatus`:

| Status | Condition | Meaning |
|--------|-----------|---------|
| Green | `TotalTrialsRun ≤ 20` OR a completed walk-forward study exists | Low overfitting risk |
| Amber | `20 < TotalTrialsRun ≤ 50` without walk-forward | Moderate risk — consider walk-forward validation |
| Red | `TotalTrialsRun > 50` without walk-forward | High risk — walk-forward validation strongly recommended |

A completed walk-forward study resets the budget to Green regardless of trial count, because walk-forward validation provides out-of-sample evidence.

An over-optimization warning is triggered when more than 5 parameter sweeps have been run without a walk-forward study (`TrialBudgetDefaults.OverOptimizationSweepThreshold = 5`).

### Fragility Scoring

The `ParameterStabilityWorkflow` computes a fragility score by perturbing strategy parameters and measuring performance sensitivity. High fragility (performance degrades sharply with small parameter changes) indicates overfitting to specific parameter values.

---

## Benchmarking Methodology

### Automatic Buy-and-Hold Benchmark

The `BenchmarkComparisonWorkflow` automatically includes a `BaselineBuyAndHoldStrategy` benchmark when no explicit benchmark is specified. This provides a passive-exposure baseline for every strategy evaluation.

### Excess Metrics

`BenchmarkComparisonResult` computes:

| Metric | Formula | Purpose |
|--------|---------|---------|
| Excess Return | `strategySharpe - benchmarkSharpe` | Raw edge over benchmark |
| Information Ratio | `excessReturn / trackingError` | Risk-adjusted edge |
| Tracking Error | `stddev(strategyReturns - benchmarkReturns)` | Deviation from benchmark |
| Max Relative Drawdown | `max(strategyDrawdown - benchmarkDrawdown)` | Worst-case relative underperformance |

### Research Workflow Integration

The benchmark comparison is one of several research workflows. The recommended research progression is:

1. Single run with Fast Idea Check preset
2. Parameter sweep to identify viable parameter regions
3. Walk-forward analysis for out-of-sample validation
4. Sensitivity analysis for cost/delay robustness
5. Benchmark comparison for edge quantification
6. Monte Carlo simulation for distributional robustness

The `ResearchChecklistService` tracks progress through these stages and recommends the next action via `NextRecommendedAction`.
