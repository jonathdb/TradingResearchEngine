# Kiro Follow-Up Prompt: TradingResearchEngine V2.1 / V2 Follow-On — Execution Realism, Research Robustness, and Engine Maturity

## When to apply this prompt

Apply this prompt **after** Kiro has reviewed and incorporated the first V2 prompt focused on engine correctness bugs and regression fixes. This follow-up is not a replacement for the first prompt. It extends V2 into a more mature quantitative research platform by improving execution realism, statistical robustness, experiment reproducibility, and engine ergonomics.

This phase is still **not** the UI redesign. UI/UX remains V3. The purpose of this prompt is to strengthen the research engine underneath so that any later UI is built on trustworthy, extensible, and professionally useful foundations.

Use this prompt to update:
- `.kiro/specs/trading-research-engine/requirements.md`
- `.kiro/specs/trading-research-engine/design.md`
- `.kiro/specs/trading-research-engine/tasks.md`
- relevant files under `.kiro/steering/`

Do not remove the V1 or V2 content. Add this as a clearly labeled follow-on phase, for example `## V2.1 Scope` or `## V2 Follow-On Scope`.

---

## Objective

The first V2 prompt corrected backtest-invalidating bugs such as look-ahead bias, incorrect Sharpe computation, stale equity curves, portfolio direction ambiguity, and Monte Carlo normalisation. This follow-up prompt asks Kiro to evolve the engine from “correct enough to trust” toward “useful for real quantitative research and strategy triage.”

The key goals are:
1. Improve execution realism beyond simple next-bar fills
2. Improve research robustness beyond point-estimate backtest metrics
3. Improve engine extensibility for future strategy and portfolio expansion
4. Improve reproducibility and debugging of research runs
5. Preserve domain boundaries already established in `.kiro/steering/domain-boundaries.md`

---

## Follow-On Improvement Themes

### Theme 1 — Execution Realism

The current execution layer already includes `SimulatedExecutionHandler`, pluggable slippage models, and commission models [cite:102]. That is a good base, but for serious strategy research the engine needs additional realism so that strategy rankings do not collapse when execution assumptions are made more conservative.

#### EXR-01 — Separate Research Fill Modes from Broker-Realistic Fill Modes

**Problem:**  
Even after the V2 bug fixes, a single fill mode is too blunt. Some workflows need fast,
coarse testing; others need more conservative simulation.

**Required enhancement:**  
Add an execution realism profile concept to `ScenarioConfig`:
- `ExecutionRealismProfile.FastResearch`
- `ExecutionRealismProfile.StandardBacktest`
- `ExecutionRealismProfile.BrokerConservative`

These profiles should configure defaults for:
- fill mode (`NextBarOpen`, intra-bar, tick-based)
- slippage model
- spread handling
- stop fill conservatism
- order expiry behaviour
- partial fill behaviour (if enabled)

This allows the same strategy to be tested across realism levels without changing strategy code.

Add a `RealismSensitivityWorkflow` that runs the same strategy under all three profiles and
reports the degradation in CAGR, Sharpe, max drawdown, and profit factor.

---

#### EXR-02 — Slippage Models by Asset Class and Regime

**Problem:**  
The current slippage options include zero slippage and fixed spread slippage [cite:102]. Fixed slippage is useful for smoke tests, but it is not sufficient for comparing strategies across assets, volatility regimes, or session conditions.

**Required enhancement:**  
Add additional pluggable slippage models in `TradingResearchEngine.Application/Execution`:
- `AtrScaledSlippageModel`
- `PercentOfPriceSlippageModel`
- `SessionAwareSlippageModel`
- `VolatilityBucketSlippageModel`

The models should all implement the existing `ISlippageModel` contract and remain purely deterministic given the same inputs.

Design notes:
- `AtrScaledSlippageModel` should scale slippage as a fraction of ATR or recent true range
- `PercentOfPriceSlippageModel` should scale slippage as bps of execution price
- `SessionAwareSlippageModel` should widen slippage during illiquid hours and narrow it during core sessions
- `VolatilityBucketSlippageModel` should map recent realised volatility into slippage bands

Add `SlippageModelOptions` to `ScenarioConfig` so workflows can configure these models declaratively.

---

#### EXR-03 — Session and Market Hours Awareness

**Problem:**  
A research engine that ignores session boundaries can overstate the feasibility of breakout,
mean-reversion, and macro-rotation systems. Many forex and intraday edge profiles are
session-dependent.

**Required enhancement:**  
Introduce market session support as a Core-level concept:
- `TradingSession` value object or equivalent
- `ISessionCalendar` interface in Core
- default implementations in Application or Infrastructure for simple sessions

Capabilities:
- detect whether a timestamp is tradable
- classify bar/tick into session buckets (Asia, London, New York, overlap, after-hours, etc.)
- allow strategies or risk layers to reject trades outside allowed sessions
- allow slippage models to use session-aware widening

Add a `SessionFilter` option to `ScenarioConfig` and ensure this does not leak provider-specific timezone logic into strategies.

---

#### EXR-04 — Partial Fills and Fill Outcomes

**Problem:**  
Right now the engine appears to assume binary outcomes: order filled or not filled. That is acceptable early on, but for realistic portfolio and execution research it becomes limiting.

**Required enhancement:**  
Introduce an explicit execution outcome type, for example:
- `ExecutionOutcome.Filled`
- `ExecutionOutcome.PartiallyFilled`
- `ExecutionOutcome.Unfilled`
- `ExecutionOutcome.Rejected`
- `ExecutionOutcome.Expired`

Either evolve `FillEvent` or add an `OrderExecutionResult` model that can represent partial fills and order state transitions.

This does **not** need to become a microstructure simulator. The goal is simply to support:
- partial quantity fills
- remaining quantity carried forward
- order expiry after `MaxBarsPending`
- rejection reasons (session closed, invalid stop, insufficient capital, etc.)

Keep the implementation modular so the default path remains simple.

---

### Theme 2 — Statistical Robustness and Research Quality

The engine already supports Monte Carlo, parameter sweep, and walk-forward in V1 scope [cite:97]. The next step is to make those workflows better at detecting fragile edges rather than simply surfacing the best backtest.

#### RSR-01 — Walk-Forward as a First-Class, Opinionated Workflow

**Problem:**  
Walk-forward testing exists as a concept in scope, but it should become a first-class validation standard rather than a nice-to-have [cite:97].

**Required enhancement:**  
Upgrade the walk-forward workflow to support:
- rolling windows and anchored windows
- separate optimisation and validation segments
- stitch together out-of-sample equity curves into a composite report
- report per-window chosen parameters, out-of-sample CAGR, max drawdown, Sharpe, and number of trades

Add a `WalkForwardSummary` output object with:
- `Windows`
- `CompositeEquityCurve`
- `AverageOutOfSampleSharpe`
- `WorstWindowDrawdown`
- `ParameterDriftScore`

---

#### RSR-02 — Parameter Stability Maps and Fragility Scoring

**Problem:**  
A parameter sweep that only returns the top-performing parameter set encourages overfitting.

**Required enhancement:**  
Add a `ParameterStabilityWorkflow` that evaluates the neighbourhood around the best parameter set and produces:
- local median performance
- local worst-case performance
- proportion of profitable neighbouring parameter combinations
- `FragilityScore`

Definition guidance:
- A robust parameter set performs reasonably across nearby configurations
- A fragile parameter set performs well only in a narrow island

This workflow should consume `BacktestResult` outputs and not modify Core abstractions.

---

#### RSR-03 — Sensitivity Testing to Cost and Delay

**Problem:**  
Many strategies only work because execution assumptions are optimistic.

**Required enhancement:**  
Add a `SensitivityAnalysisWorkflow` that reruns a strategy under perturbations such as:
- spread widened by 25%, 50%, 100%
- slippage multiplied by 1.5x and 2x
- one-bar entry delay
- one-bar exit delay
- reduced position sizing

The workflow should produce a sensitivity matrix and summary metrics:
- `CostSensitivity`
- `DelaySensitivity`
- `ExecutionRobustnessScore`

This is one of the highest-value additions for strategy triage.

---

#### RSR-04 — Regime Segmentation of Results

**Problem:**  
Aggregate backtest metrics hide whether a strategy only works in specific volatility,
trend, or session regimes.

**Required enhancement:**  
Add regime segmentation to reporting and research workflows. At minimum support:
- volatility regime buckets (low/medium/high)
- trend regime buckets (e.g. moving average slope or ADX proxy)
- session regime buckets (from EXR-03)

`BacktestResult` does not need to embed all segment reports directly, but workflows should be able to generate a `RegimePerformanceReport` with:
- trades per regime
- win rate per regime
- expectancy per regime
- average hold time per regime
- max drawdown contribution per regime

---

### Theme 3 — Portfolio and Risk Maturity

#### PRM-01 — Portfolio Constraints Beyond Single-Position Logic

**Problem:**  
The current engine behaves like a single-strategy single-portfolio simulator. That is appropriate for V1, but the next maturity step is to support constraints that matter in real research.

**Required enhancement:**  
Add configurable portfolio constraints to the risk layer and/or portfolio rules:
- max gross exposure
- max capital allocated per symbol
- max concurrent open positions
- cooldown bars after exit
- max daily loss / max trailing drawdown guardrails

These constraints should be optional and composable. Do not hardcode prop-firm constraints into Core; preserve the existing bounded-context rule that PropFirm remains an Application-layer consumer of results [cite:97][cite:98].

---

#### PRM-02 — Position Sizing Policies as First-Class Components

**Problem:**  
Many strategy results are dominated by position sizing rather than signal quality, but sizing is often embedded indirectly through `RiskLayer` logic.

**Required enhancement:**  
Introduce `IPositionSizingPolicy` in Core or Application (depending on current ownership fit) with implementations such as:
- `FixedQuantitySizingPolicy`
- `FixedDollarRiskSizingPolicy`
- `PercentEquitySizingPolicy`
- `VolatilityTargetSizingPolicy`

This keeps signal generation, risk approval, and sizing cleanly separated.

---

### Theme 4 — Reproducibility, Auditability, and Debugging

#### RAD-01 — Experiment Metadata and Reproducibility Envelope

**Problem:**  
A research result is much less useful if it cannot be reproduced exactly.

**Required enhancement:**  
Add an `ExperimentMetadata` model linked to `BacktestResult`, including:
- strategy name
- parameter values
- data source identifier
- data range
- realism profile
- slippage model and options
- commission model and options
- fill mode
- bars per year
- random seed(s) used in Monte Carlo or bootstrap workflows
- engine version / git commit hash if available at composition root

This metadata should be serialisable with the result and usable by reporters.

---

#### RAD-02 — Trade Replay / Event Trace Mode

**Problem:**  
When a strategy behaves unexpectedly, it is hard to diagnose without a deterministic replay of the event chain.

**Required enhancement:**  
Add an optional event-trace mode that records a compact sequence of:
- incoming market event
- generated signal
- risk decision
- order creation
- execution decision
- portfolio state transition

This can be a `RunTrace` or `EventTraceRecord` sequence emitted only when enabled. It must not become mandatory overhead for all runs. This is specifically for debugging and strategy validation.

A trace should make it possible to answer:
- why was this order placed?
- why was it rejected?
- why did it fill at this price?
- why did the position size differ from expectation?

---

### Theme 5 — Reporting and Analytics Improvements

#### RPT-01 — Better Trade Analytics

**Required enhancement:**  
Add additional trade-level analytics to `MetricsCalculator` or a dedicated analytics component:
- expectancy per trade
- average win / average loss
- profit factor by regime
- MAE (maximum adverse excursion)
- MFE (maximum favourable excursion)
- average bars in trade
- longest flat period
- recovery factor

Where data requirements exceed the current `ClosedTrade` shape, extend the model accordingly.

---

#### RPT-02 — Compare Strategies on the Same Dataset and Assumptions

**Problem:**  
Research is much more useful when multiple strategies can be compared under identical assumptions.

**Required enhancement:**  
Add a `StrategyComparisonWorkflow` that consumes multiple `BacktestResult`s and produces a comparable summary table using the same:
- data interval
- date range
- fill mode
- slippage model
- commission model
- bars per year

The output should highlight whether a strategy's apparent edge survives after costs and realism changes.

---

## New Follow-On Requirements

### REQ-V2X-01 — Realism Profile Configuration

`ScenarioConfig` must expose an `ExecutionRealismProfile` enum and optional `ExecutionOptions` object that governs default fill, slippage, spread, and order-behaviour settings.

### REQ-V2X-02 — Experiment Metadata on Every Research Result

Every `BacktestResult` produced by hosts and workflows must carry an `ExperimentMetadata` payload sufficient to rerun the same scenario deterministically.

### REQ-V2X-03 — Strategy Robustness Outputs

Research workflows must be able to produce robustness-oriented outputs, not just point-estimate performance. At minimum this includes sensitivity analysis, walk-forward summary, and parameter fragility metrics.

### REQ-V2X-04 — Optional Event Trace Mode

The engine must support an opt-in trace mode that records event flow and state transitions for a single run without contaminating the default execution path.

### REQ-V2X-05 — Session Awareness Without Strategy Pollution

Session filtering, session labels, and session-aware execution behaviour must be implemented without forcing strategies to own timezone or provider-specific market-hours logic.

---

## Steering Document Updates Required

**`.kiro/steering/product.md`**  
Add a `## V2 Follow-On Scope` or `## V2.1 Scope` section focused on execution realism, robustness analysis, and reproducibility. Reiterate that UI rework is V3.

**`.kiro/steering/domain-boundaries.md`**  
Update module boundaries to clarify ownership of:
- `ExecutionRealismProfile`
- `ExperimentMetadata`
- `ISessionCalendar`
- `IPositionSizingPolicy`
- optional trace records

Preserve the rule that PropFirm remains a consumer of `BacktestResult` and does not leak its rules into Core [cite:97][cite:98].

**`.kiro/steering/testing-standards.md`**  
Add test guidance for:
- realism profile regression tests
- sensitivity workflow determinism
- session calendar edge cases
- trace mode correctness
- reproducibility of Monte Carlo and bootstrap workflows given fixed seeds

**`.kiro/steering/tech.md`**  
Document that all stochastic workflows must accept explicit seeds and produce deterministic outputs when the same seed and inputs are supplied.

**`.kiro/steering/strategy-registry.md`**  
Clarify that strategies should remain focused on signal generation and must not embed market-hours logic, execution cost modelling, or report-generation concerns.

---

## Suggested Task Breakdown for tasks.md

| ID | Task | Depends on | Priority |
|----|------|------------|----------|
| V2X-T01 | Add `ExecutionRealismProfile` and `ExecutionOptions` to `ScenarioConfig` | V2 core fixes complete | High |
| V2X-T02 | Implement `RealismSensitivityWorkflow` across realism profiles | V2X-T01 | High |
| V2X-T03 | Add `AtrScaledSlippageModel`, `PercentOfPriceSlippageModel`, `SessionAwareSlippageModel`, and `VolatilityBucketSlippageModel` | V2X-T01 | High |
| V2X-T04 | Add `ISessionCalendar`, `TradingSession`, and session filtering support | — | High |
| V2X-T05 | Add partial-fill / execution outcome modelling | V2 execution updates complete | High |
| V2X-T06 | Upgrade walk-forward workflow with stitched OOS equity and parameter drift metrics | Existing walk-forward workflow | High |
| V2X-T07 | Implement `ParameterStabilityWorkflow` and `FragilityScore` | Parameter sweep workflow | High |
| V2X-T08 | Implement `SensitivityAnalysisWorkflow` for cost and delay perturbations | V2X-T01 | High |
| V2X-T09 | Add regime segmentation reporting by volatility, trend, and session | V2X-T04 | Medium |
| V2X-T10 | Add configurable portfolio constraints (max exposure, cooldown, max open positions, drawdown guardrails) | Risk layer | Medium |
| V2X-T11 | Introduce `IPositionSizingPolicy` and basic sizing policy implementations | Risk layer | Medium |
| V2X-T12 | Add `ExperimentMetadata` to `BacktestResult` and repository/reporting paths | — | Medium |
| V2X-T13 | Add optional event trace mode and trace records | Engine event loop stable | Medium |
| V2X-T14 | Extend analytics with MAE, MFE, expectancy, average bars in trade, longest flat period, recovery factor | Metrics layer | Medium |
| V2X-T15 | Implement `StrategyComparisonWorkflow` for same-assumption comparison | BacktestResult and reporting stable | Medium |
| V2X-T16 | Update `.kiro/steering/` docs for follow-on scope | — | Medium |
| V2X-T17 | Update `requirements.md`, `design.md`, and `tasks.md` to include this follow-on phase | — | Medium |

---

## Acceptance Criteria for the Follow-On Phase

1. The same strategy can be run under at least three realism profiles and the output explicitly reports performance degradation across those profiles.
2. Walk-forward output includes stitched out-of-sample equity and parameter drift information.
3. Parameter stability analysis can identify whether a top parameter set is robust or fragile.
4. Session filtering can disable trading outside configured windows without requiring strategy-specific timezone logic.
5. All stochastic workflows are reproducible given the same seed and inputs.
6. An event trace can explain why an order was generated, approved/rejected, filled/unfilled, and how the portfolio changed.
7. Reporting includes at least expectancy, MAE, MFE, and average bars in trade.
8. Strategy comparison can compare multiple backtests under matched execution assumptions.

---

## Design Guardrails

- Do not let strategies absorb execution realism logic. Keep strategy code focused on signal generation.
- Do not let provider-specific session calendars leak into Core strategy code.
- Do not hardcode prop-firm constraints into Core; preserve the bounded-context rules already documented in steering.
- Keep defaults simple. Advanced realism features must be opt-in.
- Preserve deterministic behaviour whenever a seed is supplied.
- Prefer additive extension over breaking rewrites unless the current abstraction is fundamentally blocking the new capability.

---

## Final Instruction to Kiro

Treat this prompt as a **follow-on after the first V2 correctness prompt has been reviewed**. First ensure the engine is correct and numerically trustworthy. Then use this prompt to make it more realistic, robust, and research-grade. Update the spec, design, tasks, and steering docs accordingly, while keeping the UI redesign explicitly deferred to V3.
