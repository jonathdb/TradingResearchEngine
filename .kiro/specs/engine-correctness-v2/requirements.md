# Requirements Document

## Introduction

TradingResearchEngine V2 is an engine correctness overhaul. V1 shipped a working event-driven backtesting engine with correct pipeline topology, solid domain boundaries, and reference strategies. An expert quant and statistical engineering review identified critical correctness bugs and medium-priority improvements that make V1 backtest results untrustworthy. V2 addresses all of these before any UI work (UI rework is explicitly V3).

V2 does not discard V1 requirements — it amends and extends them. The existing event-driven architecture, clean-architecture layers, research workflows, and prop-firm module remain intact. V2 focuses exclusively on engine-level numeric correctness, fill simulation fidelity, and performance improvements to strategy computations.

### V2 Scope Summary

- Eliminate look-ahead bias in the engine dispatch loop (BUG-01)
- Fix Sharpe/Sortino ratio computation to use equity curve period returns (BUG-02)
- Add continuous mark-to-market on every bar (BUG-03)
- Resolve Direction.Short ambiguity for long-only scope (BUG-04)
- Fix Monte Carlo to resample normalised returns, not absolute PnL (BUG-05)
- Replace O(n) SMA with O(1) rolling sum (IMP-01)
- Cache ADF stationarity test results (IMP-02)
- Replace R² smoothness with K-Ratio (IMP-03)
- Add bid/ask-aware tick fills (IMP-04)
- Implement intra-bar limit and stop-market fill logic (IMP-05)
- Enrich EquityCurvePoint and ClosedTrade with additional fields
- Add BarsPerYear and FillMode to ScenarioConfig
- Regression unit tests for every bug fix

### Out of Scope for V2

- UI rework (explicitly V3)
- Live or paper trading
- Database persistence
- Named third-party data provider integrations
- Multi-currency portfolio tracking
- Short-selling (V2 is long-only; Direction.Short is removed)
- Additional strategy implementations beyond bug fixes to existing ones

---

## Requirements

---

### Requirement 1: Eliminate Look-Ahead Bias in Engine Dispatch (BUG-01)

**User Story:** As a quant researcher, I want the engine to fill orders on the next bar's open price rather than the current bar's close, so that backtest results do not suffer from look-ahead bias.

#### Acceptance Criteria

1. WHEN an `OrderEvent` is approved by the `IRiskLayer`, THEN the engine SHALL place it into a pending-order queue rather than dispatching it immediately to the `IExecutionHandler`.
2. WHEN a new `MarketDataEvent` arrives, THEN the engine SHALL first fill all pending orders from the previous bar using the new bar's `Open` price as the base fill price for `Market` orders, before passing the new bar to the strategy.
3. THE correct event processing order per bar SHALL be: (a) fill pending orders from previous bar at new bar's Open, (b) update portfolio mark-to-market with new bar's Close, (c) pass new bar to strategy, (d) new signals → risk layer → approved orders enter pending queue for next bar.
4. THE `ScenarioConfig` SHALL expose a `FillMode` enum field with values `NextBarOpen` and `SameBarClose`, defaulting to `NextBarOpen`.
5. WHEN `FillMode` is `SameBarClose`, THEN the engine SHALL use the V1 behaviour (immediate fill at current bar's Close) for backward compatibility.
6. WHEN `FillMode` is `NextBarOpen`, THEN a strategy signalling on bar N SHALL have its order filled at bar N+1's Open price.

---

### Requirement 2: Fix Sharpe and Sortino Ratio Computation (BUG-02)

**User Story:** As a quant researcher, I want Sharpe and Sortino ratios computed from the equity curve's period-by-period returns, so that the ratios are statistically valid regardless of trade frequency.

#### Acceptance Criteria

1. THE `MetricsCalculator` SHALL compute `SharpeRatio` from the time-series equity curve (period-by-period returns derived from `EquityCurvePoint` records), not from the closed trade list.
2. THE `MetricsCalculator` SHALL compute `SortinoRatio` from the time-series equity curve period returns, with downside deviation also computed from period returns.
3. THE annualisation factor SHALL be derived from `ScenarioConfig.BarsPerYear`, not hardcoded as `Math.Sqrt(252)`.
4. THE existing `GetNetReturns(IReadOnlyList<ClosedTrade>)` helper SHALL be removed or replaced.
5. WHEN the equity curve has fewer than 2 points, THEN both ratios SHALL return `null`.
6. WHEN all period returns are identical (zero standard deviation), THEN `SharpeRatio` SHALL return `null`.

---

### Requirement 3: Continuous Portfolio Mark-to-Market (BUG-03)

**User Story:** As a quant researcher, I want the portfolio's total equity updated on every bar, so that drawdown calculations and the equity curve reflect true unrealised P&L between trades.

#### Acceptance Criteria

1. THE `Portfolio` SHALL expose a `MarkToMarket(string symbol, decimal price, DateTimeOffset timestamp)` method that updates unrealised P&L for an open position and appends an `EquityCurvePoint`.
2. THE engine SHALL call `Portfolio.MarkToMarket` on every `MarketDataEvent`, after pending fills are processed and before the strategy is invoked.
3. THE `EquityCurvePoint` SHALL include: `Timestamp`, `TotalEquity`, `CashBalance`, `UnrealisedPnl`, `RealisedPnl`, and `OpenPositionCount`.
4. WHEN no position is open for a symbol, THEN `MarkToMarket` SHALL still append an equity curve point reflecting current portfolio state.
5. THE `Portfolio.TotalEquity` SHALL change between fill events when an open position exists and a new bar arrives with a different price.

---

### Requirement 4: Resolve Direction.Short Ambiguity (BUG-04)

**User Story:** As an engine developer, I want the Direction enum to unambiguously represent long-only semantics, so that portfolio accounting cannot silently produce incorrect results for short signals.

#### Acceptance Criteria

1. THE `Direction` enum SHALL contain only `Long` and `Flat` values; the `Short` value SHALL be removed.
2. ALL existing strategy implementations SHALL use `Direction.Flat` as the exit signal (replacing any use of `Direction.Short`).
3. THE `Direction` enum and `IStrategy` interface SHALL carry XML doc comments stating that all strategies are long-only in V2 and short-selling is out of scope.
4. WHEN a `FillEvent` with `Direction.Flat` arrives and no matching long position exists, THEN the `Portfolio` SHALL not modify `CashBalance` and SHALL log a warning.
5. ALL references to `Direction.Short` across the solution SHALL be updated to `Direction.Flat` or removed.

---

### Requirement 5: Fix Monte Carlo Normalised Return Resampling (BUG-05)

**User Story:** As a quant researcher, I want Monte Carlo simulations to resample normalised per-trade returns rather than absolute PnL, so that the outcome distribution is not biased by varying position sizes.

#### Acceptance Criteria

1. THE `MonteCarloWorkflow` SHALL resample per-trade return on risk (`NetPnl / (EntryPrice * Quantity)`) rather than absolute `NetPnl`.
2. THE `MonteCarloWorkflow` SHALL reconstruct equity paths multiplicatively: `equityPath[i] = equityPath[i-1] * (1 + sampledReturn)`.
3. THE `ClosedTrade` record SHALL expose a computed `ReturnOnRisk` property: `NetPnl / (EntryPrice * Quantity)`.
4. WHEN `EntryPrice` or `Quantity` is zero, THEN `ReturnOnRisk` SHALL return 0.
5. WHEN the same seed is used, Monte Carlo paths SHALL be identical regardless of whether position sizes vary across trades (normalisation is effective).

---

### Requirement 6: BarsPerYear Configuration (REQ-V2-02)

**User Story:** As a quant researcher, I want to specify the number of bars per year in my scenario configuration, so that Sharpe/Sortino annualisation is correct for any bar interval.

#### Acceptance Criteria

1. THE `ScenarioConfig` SHALL expose an `int BarsPerYear` field used by `MetricsCalculator` for Sharpe/Sortino annualisation.
2. THE following defaults SHALL be provided per common intervals: Daily=252, H4=1512, H1=6048, M15=24192.
3. THE `BarsPerYear` value SHALL be the canonical source of truth for annualisation; no hardcoded `252` or `Math.Sqrt(252)` SHALL remain in `MetricsCalculator`.
4. WHEN `BarsPerYear` is not specified in the config, THEN the default SHALL be 252 (daily bars).

---

### Requirement 7: Equity Curve as First-Class Output (REQ-V2-01)

**User Story:** As a quant researcher, I want a rich equity curve with per-bar detail, so that I can analyse portfolio dynamics beyond just total equity.

#### Acceptance Criteria

1. THE `EquityCurvePoint` record SHALL include: `Timestamp`, `TotalEquity`, `CashBalance`, `UnrealisedPnl`, `RealisedPnl`, and `OpenPositionCount`.
2. THE `BacktestResult` SHALL expose `IReadOnlyList<EquityCurvePoint>` containing one point per bar (not just per fill).
3. ALL reporters that reference equity curve data SHALL be updated to handle the enriched `EquityCurvePoint` fields.

---

### Requirement 8: ClosedTrade Exposes EntryPrice, Quantity, and ReturnOnRisk (REQ-V2-04)

**User Story:** As a quant researcher, I want each closed trade to expose entry price, quantity, and return on risk, so that Monte Carlo and other analyses can normalise by position size.

#### Acceptance Criteria

1. THE `ClosedTrade` record SHALL expose `decimal EntryPrice`, `decimal ExitPrice`, `decimal Quantity`, `decimal NetPnl`, and a computed `decimal ReturnOnRisk`.
2. `ReturnOnRisk` SHALL be computed as `NetPnl / (EntryPrice * Quantity)` when `EntryPrice * Quantity > 0`, otherwise 0.
3. THE existing `ClosedTrade` fields (`Symbol`, `EntryTime`, `ExitTime`, `Direction`, `GrossPnl`, `Commission`) SHALL be preserved.

---

### Requirement 9: Rolling SMA Computation — O(1) per Bar (IMP-01)

**User Story:** As a quant researcher, I want SMA calculations to run in O(1) per bar, so that parameter sweeps over large datasets complete in reasonable time.

#### Acceptance Criteria

1. THE `SmaCrossoverStrategy` SHALL replace LINQ `Skip/Take/Average` with a rolling sum accumulator pattern maintaining `_fastSum` and `_slowSum` fields.
2. THE SMA SHALL be computed as `_sum / period` — O(1) per bar after warmup.
3. THE same rolling pattern SHALL be applied to `MeanReversionStrategy`, `RsiStrategy`, and any other strategy computing window statistics on every bar.
4. THE rolling SMA SHALL produce numerically identical results to the LINQ implementation for the same input data.

---

### Requirement 10: ADF Test Caching in StationaryMeanReversionStrategy (IMP-02)

**User Story:** As a quant researcher, I want the ADF stationarity test cached and re-run only periodically, so that parameter sweeps with this strategy are not prohibitively slow.

#### Acceptance Criteria

1. THE `StationaryMeanReversionStrategy` SHALL accept an `adfRecheckInterval` constructor parameter (default 20).
2. THE ADF test SHALL only re-run every `adfRecheckInterval` bars; between re-checks, the cached stationarity flag SHALL be used.
3. THE biased variance estimator `varYl = sumYl2 / m - meanYl * meanYl` SHALL be corrected to use `/ (m - 1)` for unbiased sample variance.
4. THE ADF caching SHALL reduce computation by approximately 95% with negligible impact on signal quality.

---

### Requirement 11: Replace R² Smoothness with K-Ratio (IMP-03)

**User Story:** As a quant researcher, I want the equity curve smoothness metric to distinguish between consistently rising and consistently falling curves, so that the metric is not misleading.

#### Acceptance Criteria

1. THE `MetricsCalculator.ComputeEquityCurveSmoothness` SHALL compute the K-Ratio (Zephyr/Kestner definition): `(OLS slope of log-equity curve) / (standard error of slope * sqrt(n))`.
2. A positive K-Ratio SHALL indicate consistent upward progression; negative SHALL indicate consistent decline.
3. THE `BacktestResult` field and all reporters referencing smoothness SHALL be updated to reflect K-Ratio semantics.
4. THE method name SHALL remain `ComputeEquityCurveSmoothness` but its XML doc comment SHALL describe K-Ratio.

---

### Requirement 12: Bid/Ask-Aware Tick Fills (IMP-04)

**User Story:** As a quant researcher, I want tick-level fills to use bid/ask prices rather than last-trade price, so that spread costs are realistically modelled.

#### Acceptance Criteria

1. THE `TickEvent` SHALL expose `Quote? Bid` and `Quote? Ask` fields, where `Quote` is `public record Quote(decimal Price, decimal Size)`.
2. THE `SimulatedExecutionHandler` SHALL route tick fills as: `Direction.Long` → fill at `tick.Ask.Price`; `Direction.Flat` (close long) → fill at `tick.Bid.Price`.
3. WHEN `Bid` or `Ask` is null on a `TickEvent`, THEN the handler SHALL fall back to `LastTrade.Price`.
4. FOR `BarEvent` fills, the existing slippage model SHALL remain sufficient as a spread proxy.

---

### Requirement 13: Intra-Bar Limit and Stop-Market Fill Logic (IMP-05)

**User Story:** As a quant researcher, I want limit and stop-market orders to fill only when the bar's price range satisfies the order condition, so that order types behave realistically.

#### Acceptance Criteria

1. FOR `BarEvent` fills, a limit buy SHALL only fill if `bar.Low <= order.LimitPrice`; fill price = `order.LimitPrice`.
2. FOR `BarEvent` fills, a limit sell SHALL only fill if `bar.High >= order.LimitPrice`; fill price = `order.LimitPrice`.
3. FOR `BarEvent` fills, a stop-market buy SHALL only fill if `bar.High >= order.StopPrice`; fill price = `order.StopPrice` + slippage.
4. FOR `BarEvent` fills, a stop-market sell SHALL only fill if `bar.Low <= order.StopPrice`; fill price = `order.StopPrice` - slippage.
5. WHEN the fill condition is not met, THEN the order SHALL remain in the pending queue for the next bar.
6. THE `OrderEvent` SHALL expose a `decimal? StopPrice` field and an `int MaxBarsPending` field (default 0 = GTC) for automatic order expiry.
7. THE `OrderType` enum SHALL include `StopLimit` as a V2 scope item.
8. FOR `BarEvent` fills, a stop-limit buy SHALL only fill if `bar.High >= order.StopPrice` (trigger condition) AND `bar.Low <= order.LimitPrice` (fill condition); fill price = `order.LimitPrice`. If the stop triggers but the limit is not reached within the same bar, the order converts to a pending limit order for subsequent bars.
9. FOR `BarEvent` fills, a stop-limit sell SHALL only fill if `bar.Low <= order.StopPrice` (trigger condition) AND `bar.High >= order.LimitPrice` (fill condition); fill price = `order.LimitPrice`. If the stop triggers but the limit is not reached within the same bar, the order converts to a pending limit order for subsequent bars.
10. THE `OrderEvent` SHALL track a `bool StopTriggered` field (default false) to distinguish between a stop-limit order awaiting its stop trigger and one that has been triggered and is now acting as a limit order.

---

### Requirement 14: V2 Regression Unit Tests (REQ-V2-05)

**User Story:** As a developer, I want regression tests for every V2 bug fix, so that these bugs cannot silently reappear.

#### Acceptance Criteria

1. FOR BUG-01: a unit test SHALL assert that a strategy signalling on bar N fills at bar N+1's Open, not bar N's Close.
2. FOR BUG-02: a unit test SHALL assert that Sharpe computed from a flat equity curve is 0 (or null), and a linearly rising curve with known slope produces the expected ratio within tolerance.
3. FOR BUG-03: a unit test SHALL assert that `Portfolio.TotalEquity` updates between fill events when an open position is marked to market.
4. FOR BUG-04: a unit test SHALL assert that a `Direction.Flat` fill on a closed position does not increase cash without a corresponding position entry.
5. FOR BUG-05: a unit test SHALL assert that Monte Carlo paths starting from the same seed are identical regardless of whether position size varies across trades.
6. EACH regression test SHALL be kept permanently and SHALL NOT be removed in future versions.

---

### Requirement 15: Steering Document Updates

**User Story:** As a developer, I want the steering documents updated to reflect V2 changes, so that all future development is guided by accurate documentation.

#### Acceptance Criteria

1. THE `product.md` SHALL include a `## V2 Scope` section describing the engine correctness overhaul, noting V2 is engine-only and V3 addresses UI.
2. THE `domain-boundaries.md` SHALL update the Core owns list to include `EquityCurvePoint` (enriched), `FillMode`, `BarsPerYear` on `ScenarioConfig`, `ReturnOnRisk` on `ClosedTrade`, and note that `Direction.Short` is removed.
3. THE `testing-standards.md` SHALL include a `## Backtest Correctness Tests` section describing the regression test requirement: any fix to engine-level numeric output must be accompanied by a unit test.
4. THE `tech.md` SHALL document that `BarsPerYear` is the canonical source of truth for annualisation and must not be hardcoded elsewhere.

---

## V2.1 Follow-On: Execution Realism, Research Robustness, and Engine Maturity

### Introduction

V2 corrected backtest-invalidating bugs (look-ahead bias, incorrect Sharpe, stale equity curves, direction ambiguity, Monte Carlo normalisation). This follow-on phase evolves the engine from "correct enough to trust" toward "useful for real quantitative research and strategy triage."

V2.1 is still not the UI redesign. UI/UX remains V3. The purpose is to strengthen the research engine so that any later UI is built on trustworthy, extensible, and professionally useful foundations.

### V2.1 Scope Summary

- Execution realism profiles and advanced slippage models (EXR-01, EXR-02)
- Session and market hours awareness (EXR-03)
- Partial fills and execution outcome modelling (EXR-04)
- Walk-forward as an opinionated first-class workflow with composite OOS equity (RSR-01)
- Parameter stability maps and fragility scoring (RSR-02)
- Sensitivity testing to cost and delay (RSR-03)
- Regime segmentation of results (RSR-04)
- Portfolio constraints beyond single-position logic (PRM-01)
- Position sizing policies as first-class components (PRM-02)
- Experiment metadata and reproducibility envelope (RAD-01)
- Optional event trace / trade replay mode (RAD-02)
- Extended trade analytics: MAE, MFE, recovery factor, etc. (RPT-01)
- Strategy comparison under matched assumptions (RPT-02)

### Out of Scope for V2.1

- UI rework (explicitly V3)
- Live or paper trading
- Database persistence
- Named third-party data provider integrations
- Multi-currency portfolio tracking
- Short-selling (V2 is long-only; Direction.Short is removed)

---

### Requirement 16: Execution Realism Profiles (EXR-01)

**User Story:** As a quant researcher, I want to test the same strategy under multiple execution realism levels, so that I can see how performance degrades as assumptions become more conservative.

#### Acceptance Criteria

1. THE `ScenarioConfig` SHALL expose an `ExecutionRealismProfile` enum with values `FastResearch`, `StandardBacktest`, and `BrokerConservative`.
2. EACH profile SHALL configure defaults for: fill mode, slippage model, spread handling, stop fill conservatism, order expiry behaviour, and partial fill behaviour.
3. THE `FastResearch` profile SHALL use `SameBarClose` fill mode, zero slippage, and no partial fills for maximum speed.
4. THE `StandardBacktest` profile SHALL use `NextBarOpen` fill mode, fixed spread slippage, and standard order expiry.
5. THE `BrokerConservative` profile SHALL use `NextBarOpen` fill mode, ATR-scaled slippage, session-aware spread widening, and pessimistic stop fills.
6. THE `ScenarioConfig` SHALL expose an optional `ExecutionOptions` object that allows overriding individual profile defaults.
7. THE Application layer SHALL provide a `RealismSensitivityWorkflow` that runs the same strategy under all three profiles and reports degradation in CAGR, Sharpe, max drawdown, and profit factor.

---

### Requirement 17: Advanced Slippage Models (EXR-02)

**User Story:** As a quant researcher, I want slippage models that scale with volatility, price, and session conditions, so that execution cost assumptions are realistic across assets and regimes.

#### Acceptance Criteria

1. THE Application layer SHALL provide an `AtrScaledSlippageModel` implementing `ISlippageModel` that scales slippage as a configurable fraction of ATR or recent true range.
2. THE Application layer SHALL provide a `PercentOfPriceSlippageModel` implementing `ISlippageModel` that scales slippage as configurable basis points of execution price.
3. THE Application layer SHALL provide a `SessionAwareSlippageModel` implementing `ISlippageModel` that widens slippage during illiquid hours and narrows it during core sessions, using an `ISessionCalendar`.
4. THE Application layer SHALL provide a `VolatilityBucketSlippageModel` implementing `ISlippageModel` that maps recent realised volatility into configurable slippage bands.
5. ALL slippage models SHALL be purely deterministic given the same inputs.
6. THE `ScenarioConfig` SHALL support a `SlippageModelOptions` dictionary for declarative configuration of these models.

---

### Requirement 18: Session and Market Hours Awareness (EXR-03)

**User Story:** As a quant researcher, I want the engine to understand trading sessions, so that strategies, risk layers, and slippage models can respect market hours without embedding timezone logic in strategy code.

#### Acceptance Criteria

1. THE Core layer SHALL define a `TradingSession` value object (or equivalent) representing a named session window with start/end times and timezone.
2. THE Core layer SHALL define an `ISessionCalendar` interface with methods to: (a) determine if a timestamp is tradable, (b) classify a timestamp into a session bucket (e.g. Asia, London, NewYork, Overlap, AfterHours).
3. THE Application or Infrastructure layer SHALL provide default `ISessionCalendar` implementations for common markets (forex 24h sessions, US equity sessions).
4. THE `ScenarioConfig` SHALL expose a `SessionFilter` option that allows restricting trading to specified sessions.
5. WHEN `SessionFilter` is active, THE engine SHALL skip strategy invocation for bars outside allowed sessions but SHALL still update portfolio mark-to-market.
6. STRATEGIES SHALL NOT contain timezone or market-hours logic; session awareness SHALL be handled by the engine and risk layer.

---

### Requirement 19: Partial Fills and Execution Outcomes (EXR-04)

**User Story:** As a quant researcher, I want the execution layer to model partial fills and explicit rejection reasons, so that I can simulate more realistic order lifecycle behaviour.

#### Acceptance Criteria

1. THE engine SHALL define an `ExecutionOutcome` enum with values: `Filled`, `PartiallyFilled`, `Unfilled`, `Rejected`, `Expired`.
2. THE `IExecutionHandler.Execute` method (or a new overload) SHALL return an execution result that can represent partial fills, including filled quantity and remaining quantity.
3. WHEN a partial fill occurs, THE remaining quantity SHALL be carried forward in the pending queue as a new order.
4. WHEN an order exceeds `MaxBarsPending` without filling, THE engine SHALL expire it with `ExecutionOutcome.Expired` and log the expiry.
5. WHEN an order is rejected (session closed, insufficient capital, invalid stop), THE engine SHALL produce an `ExecutionOutcome.Rejected` with a reason string.
6. THE default execution path SHALL remain simple: full fills with no partial-fill complexity unless explicitly enabled via `ExecutionOptions`.

---

### Requirement 20: Walk-Forward as First-Class Opinionated Workflow (RSR-01)

**User Story:** As a quant researcher, I want walk-forward analysis to produce a composite out-of-sample equity curve and parameter drift metrics, so that I can validate strategy robustness over time.

#### Acceptance Criteria

1. THE `WalkForwardWorkflow` SHALL support both rolling windows and anchored windows (already in V1 scope; confirm implementation).
2. THE `WalkForwardWorkflow` SHALL stitch together out-of-sample equity curves from all windows into a `CompositeEquityCurve`.
3. THE `WalkForwardWorkflow` SHALL return a `WalkForwardSummary` containing: `Windows`, `CompositeEquityCurve`, `AverageOutOfSampleSharpe`, `WorstWindowDrawdown`, and `ParameterDriftScore`.
4. `ParameterDriftScore` SHALL measure how much the optimal parameters change across windows (e.g. normalised standard deviation of selected parameter values).
5. EACH window record SHALL include: chosen parameters, out-of-sample CAGR, max drawdown, Sharpe, and number of trades.

---

### Requirement 21: Parameter Stability and Fragility Scoring (RSR-02)

**User Story:** As a quant researcher, I want to know whether a top-performing parameter set is robust or fragile, so that I can avoid overfitting to a narrow parameter island.

#### Acceptance Criteria

1. THE Application layer SHALL provide a `ParameterStabilityWorkflow` that evaluates the neighbourhood around a given parameter set.
2. THE workflow SHALL produce: local median performance, local worst-case performance, proportion of profitable neighbouring parameter combinations, and a `FragilityScore`.
3. A `FragilityScore` near 0 SHALL indicate a robust parameter set (performs well across neighbours); a score near 1 SHALL indicate a fragile set (narrow island of profitability).
4. THE workflow SHALL consume `BacktestResult` outputs from parameter sweeps and SHALL NOT modify Core abstractions.
5. THE neighbourhood SHALL be defined as parameter values within a configurable percentage (default ±10%) of the target values.

---

### Requirement 22: Sensitivity Testing to Cost and Delay (RSR-03)

**User Story:** As a quant researcher, I want to test how a strategy's performance degrades under increased costs and execution delays, so that I can identify strategies that only work under optimistic assumptions.

#### Acceptance Criteria

1. THE Application layer SHALL provide a `SensitivityAnalysisWorkflow` that reruns a strategy under configurable perturbations.
2. THE supported perturbations SHALL include: spread widened by 25%/50%/100%, slippage multiplied by 1.5x/2x, one-bar entry delay, one-bar exit delay, and reduced position sizing.
3. THE workflow SHALL produce a sensitivity matrix showing each metric (CAGR, Sharpe, MaxDrawdown, ProfitFactor) under each perturbation.
4. THE workflow SHALL compute summary scores: `CostSensitivity`, `DelaySensitivity`, and `ExecutionRobustnessScore`.
5. `ExecutionRobustnessScore` SHALL be a composite measure where higher values indicate the strategy is more resilient to execution assumption changes.

---

### Requirement 23: Regime Segmentation of Results (RSR-04)

**User Story:** As a quant researcher, I want to see how a strategy performs across different market regimes, so that I can identify whether an edge is regime-dependent.

#### Acceptance Criteria

1. THE Application layer SHALL provide a regime segmentation capability that classifies trades and equity curve segments into regime buckets.
2. THE supported regime dimensions SHALL include: volatility regime (low/medium/high), trend regime (e.g. moving average slope or ADX proxy), and session regime (from EXR-03).
3. THE output SHALL be a `RegimePerformanceReport` containing per-regime: trade count, win rate, expectancy, average hold time, and max drawdown contribution.
4. THE regime classification SHALL be configurable (e.g. volatility percentile thresholds, trend lookback period).
5. THE regime segmentation SHALL NOT modify `BacktestResult` directly; it SHALL be a separate analysis consuming `BacktestResult` and market data.

---

### Requirement 24: Portfolio Constraints (PRM-01)

**User Story:** As a quant researcher, I want configurable portfolio constraints beyond single-position logic, so that I can model realistic capital allocation and risk management rules.

#### Acceptance Criteria

1. THE risk layer SHALL support configurable constraints: max gross exposure, max capital per symbol, max concurrent open positions, cooldown bars after exit, and max daily loss / max trailing drawdown guardrails.
2. EACH constraint SHALL be optional and composable — enabling one SHALL NOT require enabling others.
3. THE constraints SHALL be configured via `RiskParameters` in `ScenarioConfig`.
4. WHEN a constraint is violated, THE risk layer SHALL reject the order with a `RiskRejection` log entry specifying which constraint was breached.
5. PROP-FIRM constraints SHALL NOT be hardcoded into Core; the existing bounded-context rule (PropFirm as Application-layer consumer) SHALL be preserved.

---

### Requirement 25: Position Sizing Policies (PRM-02)

**User Story:** As a quant researcher, I want position sizing separated from signal generation and risk approval, so that I can independently evaluate the impact of sizing on strategy performance.

#### Acceptance Criteria

1. THE Core or Application layer SHALL define an `IPositionSizingPolicy` interface with a method that computes position size given a signal, portfolio snapshot, and market data.
2. THE Application layer SHALL provide implementations: `FixedQuantitySizingPolicy`, `FixedDollarRiskSizingPolicy`, `PercentEquitySizingPolicy`, and `VolatilityTargetSizingPolicy`.
3. THE `IRiskLayer` SHALL delegate position sizing to the active `IPositionSizingPolicy` rather than embedding sizing logic directly.
4. THE active sizing policy SHALL be configurable via `ScenarioConfig.RiskParameters`.
5. STRATEGIES SHALL NOT contain position sizing logic; they SHALL emit signals with optional strength/conviction, and sizing SHALL be handled by the policy.

---

### Requirement 26: Experiment Metadata and Reproducibility (RAD-01)

**User Story:** As a quant researcher, I want every backtest result to carry full experiment metadata, so that I can reproduce any result exactly.

#### Acceptance Criteria

1. THE `BacktestResult` SHALL carry an `ExperimentMetadata` record containing: strategy name, parameter values, data source identifier, data range, realism profile, slippage model and options, commission model and options, fill mode, bars per year, and random seed(s).
2. THE `ExperimentMetadata` SHALL be serialisable to JSON alongside the `BacktestResult`.
3. THE metadata SHALL be sufficient to reconstruct the exact `ScenarioConfig` used for the run.
4. REPORTERS SHALL include experiment metadata in Markdown and console output.
5. WHEN an engine version or git commit hash is available at the composition root, IT SHALL be included in the metadata.

---

### Requirement 27: Optional Event Trace Mode (RAD-02)

**User Story:** As a quant researcher, I want an opt-in event trace that records the full decision chain for a single run, so that I can debug unexpected strategy behaviour.

#### Acceptance Criteria

1. THE `ScenarioConfig` SHALL expose a `bool EnableEventTrace` field (default false).
2. WHEN `EnableEventTrace` is true, THE engine SHALL record a sequence of `EventTraceRecord` entries capturing: incoming market event, generated signal, risk decision (approved/rejected with reason), order creation, execution decision (filled/unfilled/partial with price), and portfolio state transition.
3. THE trace SHALL be attached to the `BacktestResult` as an optional `IReadOnlyList<EventTraceRecord>`.
4. WHEN `EnableEventTrace` is false, THE engine SHALL NOT allocate trace storage or incur trace overhead.
5. THE trace SHALL make it possible to answer: why was this order placed, why was it rejected, why did it fill at this price, and why did the position size differ from expectation.

---

### Requirement 28: Extended Trade Analytics (RPT-01)

**User Story:** As a quant researcher, I want additional trade-level analytics, so that I can evaluate strategy quality beyond basic win rate and profit factor.

#### Acceptance Criteria

1. THE `MetricsCalculator` (or a dedicated analytics component) SHALL compute: MAE (maximum adverse excursion per trade), MFE (maximum favourable excursion per trade), average bars in trade, longest flat period (bars between trades), and recovery factor (net profit / max drawdown).
2. MAE and MFE SHALL require per-bar tracking of open trade P&L; THE `ClosedTrade` record SHALL be extended with `decimal MAE` and `decimal MFE` fields, or a separate `TradeAnalytics` record SHALL be produced.
3. THE `BacktestResult` SHALL expose recovery factor as a top-level metric.
4. REPORTERS SHALL include the extended analytics in output when available.

---

### Requirement 29: Strategy Comparison Under Matched Assumptions (RPT-02)

**User Story:** As a quant researcher, I want to compare multiple strategies under identical execution assumptions, so that I can determine which edge survives after costs and realism.

#### Acceptance Criteria

1. THE Application layer SHALL provide a `StrategyComparisonWorkflow` that accepts multiple `BacktestResult` instances and validates that they share the same: data interval, date range, fill mode, slippage model, commission model, and bars per year.
2. WHEN assumptions do not match, THE workflow SHALL return a validation error listing the mismatched fields.
3. THE workflow SHALL produce a comparison table highlighting: CAGR, Sharpe, Sortino, MaxDrawdown, ProfitFactor, Expectancy, and RecoveryFactor for each strategy.
4. THE output SHALL indicate which strategy's edge survives best after costs and realism changes.

---

### Requirement 30: V2.1 Steering Document Updates

**User Story:** As a developer, I want the steering documents updated to reflect V2.1 changes, so that all future development is guided by accurate documentation.

#### Acceptance Criteria

1. THE `product.md` SHALL include a `## V2.1 Scope` section focused on execution realism, robustness analysis, and reproducibility, reiterating that UI rework is V3.
2. THE `domain-boundaries.md` SHALL clarify ownership of: `ExecutionRealismProfile`, `ExperimentMetadata`, `ISessionCalendar`, `IPositionSizingPolicy`, `EventTraceRecord`, and `ExecutionOutcome`. PropFirm SHALL remain a consumer of `BacktestResult`.
3. THE `testing-standards.md` SHALL add guidance for: realism profile regression tests, sensitivity workflow determinism, session calendar edge cases, trace mode correctness, and reproducibility of stochastic workflows given fixed seeds.
4. THE `tech.md` SHALL document that all stochastic workflows must accept explicit seeds and produce deterministic outputs when the same seed and inputs are supplied.
5. THE `strategy-registry.md` SHALL clarify that strategies must remain focused on signal generation and must not embed market-hours logic, execution cost modelling, or report-generation concerns.
