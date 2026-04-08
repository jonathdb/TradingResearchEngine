# Implementation Plan — TradingResearchEngine V2 + V2.1

## Overview

This plan implements the engine correctness overhaul (V2) and the execution realism / research robustness follow-on (V2.1). V2 tasks are critical priority and must be completed first. V2.1 tasks build on the corrected engine.

All tasks are coding tasks executable by a coding agent. No deployment, user testing, or manual verification tasks.

---

## V2 — Engine Correctness (Critical)

- [x] 1. Add `FillMode` enum and `BarsPerYear` to `ScenarioConfig`



- [x] 1.1 Create `FillMode.cs` in `Core/Configuration/` with `NextBarOpen` and `SameBarClose` values


  - Add XML doc comments explaining each mode
  - _Requirements: 1.4, 6.1, 6.2, 6.3, 6.4_
- [x] 1.2 Add `FillMode` and `BarsPerYear` fields to `ScenarioConfig` record


  - `FillMode FillMode = FillMode.NextBarOpen`
  - `int BarsPerYear = 252`
  - Update validation in `RunScenarioUseCase` to reject `BarsPerYear <= 0`
  - _Requirements: 1.4, 6.1, 6.4_

- [x] 2. Remove `Direction.Short` and update to long-only semantics (BUG-04)





- [x] 2.1 Update `Direction` enum in `Core/Events/Enums.cs` to `{ Long, Flat }`


  - Add XML doc comment stating V2 is long-only, short-selling out of scope
  - _Requirements: 4.1, 4.3_


- [ ] 2.2 Update all strategy implementations to use `Direction.Flat` instead of `Direction.Short`
  - Scan `DefaultRiskLayer.ConvertSignal` — change `Direction.Short` to `Direction.Flat`


  - Scan all strategies in `Application/Strategies/` for any `Direction.Short` references
  - _Requirements: 4.2, 4.5_
- [ ] 2.3 Update `Portfolio.Update` to handle `Direction.Flat` correctly
  - When `Direction.Flat` fill arrives with no matching position, do not modify cash, log warning
  - Remove the `Direction.Short` branch from the `Update` method
  - _Requirements: 4.4, 4.5_
- [ ] 2.4 Update `IStrategy` interface XML doc to state long-only scope
  - _Requirements: 4.3_

- [x] 3. Enrich `EquityCurvePoint` and `ClosedTrade` (BUG-03 + REQ-V2-04)








- [-] 3.1 Update `EquityCurvePoint` record to include `CashBalance`, `UnrealisedPnl`, `RealisedPnl`, `OpenPositionCount`


  - _Requirements: 3.3, 7.1_
- [x] 3.2 Add `ReturnOnRisk` computed property to `ClosedTrade`


  - `NetPnl / (EntryPrice * Quantity)` when denominator > 0, else 0





  - _Requirements: 5.3, 5.4, 8.1, 8.2_
- [ ] 3.3 Update all code that constructs `EquityCurvePoint` to pass the new fields
  - Update `Portfolio.MarkToMarket` and `Portfolio.Update`


  - _Requirements: 3.3, 7.1_

- [ ] 4. Implement continuous mark-to-market in `Portfolio` (BUG-03)
- [ ] 4.1 Add public `MarkToMarket(string symbol, decimal price, DateTimeOffset timestamp)` method to `Portfolio`
  - Update unrealised P&L for the symbol's open position
  - Recalculate total equity
  - Append enriched `EquityCurvePoint`
  - _Requirements: 3.1, 3.2, 3.4, 3.5_
- [ ] 4.2 Remove equity curve append from `Portfolio.Update(FillEvent)` — mark-to-market now owns this
  - _Requirements: 3.1_

- [x] 5. Implement pending-order queue and corrected engine loop (BUG-01)



- [x] 5.1 Add `PendingOrders` list and `FillMode` to `RunState` in `BacktestEngine`

  - _Requirements: 1.1, 1.4_
- [ ] 5.2 Implement `ProcessPendingOrders` method that fills pending orders using new bar's Open price
  - For `Market` orders: fill at `bar.Open` + slippage

  - Handle order expiry via `MaxBarsPending`
  - _Requirements: 1.2, 1.3, 1.6_
- [ ] 5.3 Restructure `Dispatch` / main loop to follow the 4-step per-bar order
  - Step 1: fill pending orders at new bar's Open
  - Step 2: mark-to-market at new bar's Close

  - Step 3: pass bar to strategy
  - Step 4: new signals → risk layer → pending queue
  - _Requirements: 1.3, 1.5, 1.6_
- [ ] 5.4 Implement `RouteApprovedOrder` that sends to pending queue (NextBarOpen) or immediate dispatch (SameBarClose)
  - _Requirements: 1.1, 1.5_

- [x] 6. Add `StopPrice`, `MaxBarsPending`, `StopTriggered` to `OrderEvent` and `StopLimit` to `OrderType` (IMP-05)


- [x] 6.1 Update `OrderEvent` record with `decimal? StopPrice`, `int MaxBarsPending = 0`, `bool StopTriggered = false`




  - _Requirements: 13.6, 13.10_

- [x] 6.2 Add `StopLimit` to `OrderType` enum







  - _Requirements: 13.7_


- [ ] 7. Implement intra-bar fill logic for Limit, StopMarket, StopLimit orders (IMP-05)
- [x] 7.1 Implement `TryFillLimit` in engine: limit buy fills if `bar.Low <= LimitPrice`, limit sell fills if `bar.High >= LimitPrice`

  - _Requirements: 13.1, 13.2_
- [x] 7.2 Implement `TryFillStopMarket`: stop buy fills if `bar.High >= StopPrice` at StopPrice + slippage, stop sell fills if `bar.Low <= StopPrice` at StopPrice - slippage

  - _Requirements: 13.3, 13.4_



- [ ] 7.3 Implement `TryFillStopLimit`: trigger check then limit fill, convert to pending limit if triggered but not filled
  - _Requirements: 13.8, 13.9, 13.10_

- [x] 7.4 Integrate fill logic into `ProcessPendingOrders` with unfilled orders remaining in queue



  - _Requirements: 13.5_





- [ ] 8. Fix Sharpe and Sortino to use equity curve period returns (BUG-02)
- [x] 8.1 Add `GetPeriodReturns(IReadOnlyList<EquityCurvePoint>)` helper to `MetricsCalculator`

  - _Requirements: 2.1_

- [x] 8.2 Rewrite `ComputeSharpeRatio` to accept `IReadOnlyList<EquityCurvePoint>` and `int barsPerYear`



  - Remove old `IReadOnlyList<ClosedTrade>` overload

  - Remove `GetNetReturns` helper

  - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6_

- [x] 8.3 Rewrite `ComputeSortinoRatio` to accept equity curve and `barsPerYear`



  - Downside deviation from period returns below risk-free rate
  - _Requirements: 2.2, 2.3_
- [x] 8.4 Update `BacktestEngine.BuildResult` to pass `config.BarsPerYear` and equity curve to new metric signatures

  - _Requirements: 2.3_




- [ ] 9. Replace R² smoothness with K-Ratio (IMP-03)
- [x] 9.1 Rewrite `ComputeEquityCurveSmoothness` to compute K-Ratio: `slope of log-equity OLS / (SE of slope * sqrt(n))`

  - Update XML doc comment to describe K-Ratio semantics
  - _Requirements: 11.1, 11.2, 11.4_

- [ ] 9.2 Update reporters that display smoothness to reflect K-Ratio interpretation
  - _Requirements: 11.3_


- [x] 10. Fix Monte Carlo to resample normalised returns (BUG-05)

- [ ] 10.1 Update `MonteCarloWorkflow.RunSimulation` to use `ClosedTrade.ReturnOnRisk` instead of `NetPnl`
  - _Requirements: 5.1_

- [ ] 10.2 Change equity path reconstruction from additive to multiplicative: `equity *= (1 + sampledReturn)`
  - _Requirements: 5.2_

- [ ] 11. Add bid/ask `Quote` fields to `TickEvent` and implement bid/ask-aware fills (IMP-04)
- [x] 11.1 Add `Quote` record to `Core/Events/ValueTypes.cs`



  - `public record Quote(decimal Price, decimal Size)`
  - _Requirements: 12.1_
- [ ] 11.2 Add `Quote? Bid` and `Quote? Ask` to `TickEvent` record
  - _Requirements: 12.1_
- [ ] 11.3 Update `SimulatedExecutionHandler.Execute` to use `Ask.Price` for Long fills and `Bid.Price` for Flat fills on tick data, falling back to `LastTrade.Price`
  - _Requirements: 12.2, 12.3, 12.4_

- [ ] 12. Replace O(n) SMA with rolling sum in strategies (IMP-01)
- [ ] 12.1 Refactor `SmaCrossoverStrategy` to use `_fastSum` / `_slowSum` rolling accumulators
  - _Requirements: 9.1, 9.2_
- [ ] 12.2 Refactor `MeanReversionStrategy` to use rolling SMA + rolling variance
  - _Requirements: 9.3_
- [ ] 12.3 Refactor `RsiStrategy` to use Wilder smoothing accumulators instead of per-bar loop
  - _Requirements: 9.3_
- [ ] 12.4 Refactor `BollingerBandsStrategy` to use rolling SMA + rolling sum-of-squares
  - _Requirements: 9.3_

- [ ] 13. Add ADF recheck interval cache and fix biased variance (IMP-02)
- [ ] 13.1 Add `adfRecheckInterval` constructor parameter (default 20) to `StationaryMeanReversionStrategy`
  - Add `_barsSinceAdfCheck` counter and `_cachedStationarity` flag
  - Only re-run ADF every `adfRecheckInterval` bars
  - _Requirements: 10.1, 10.2, 10.4_
- [ ] 13.2 Fix biased variance: change `sumYl2 / m` to `sumYl2 / (m - 1)` in `IsStationary`
  - _Requirements: 10.3_

- [ ] 14. Write V2 regression unit tests (REQ-V2-05)
- [ ] 14.1 Write `BacktestEngineV2Tests.NextBarOpen_FillsAtNextBarOpen` — signal on bar N fills at bar N+1's Open
  - Use in-memory data provider with known bar sequence
  - Assert fill price equals bar N+1's Open, not bar N's Close
  - _Requirements: 14.1_
- [ ] 14.2 Write `MetricsCalculatorV2Tests.FlatEquityCurve_SharpeIsNull` and `LinearRisingCurve_SharpeWithinTolerance`
  - Construct known equity curves, assert expected Sharpe values
  - _Requirements: 14.2_
- [ ] 14.3 Write `PortfolioV2Tests.MarkToMarket_UpdatesBetweenFills`
  - Open a position, call MarkToMarket with different prices, assert TotalEquity changes
  - _Requirements: 14.3_
- [ ] 14.4 Write `PortfolioV2Tests.FlatFillNoPosition_CashUnchanged`
  - Send a Flat fill with no open position, assert CashBalance unchanged
  - _Requirements: 14.4_
- [ ] 14.5 Write `MonteCarloV2Tests.NormalisedReturns_SeedReproducible`
  - Create trades with varying position sizes, run MC with same seed twice, assert identical paths
  - _Requirements: 14.5_

- [ ] 15. Update `BacktestEngine.BuildResult` to use all corrected metric signatures
  - Pass equity curve + barsPerYear to Sharpe/Sortino
  - Pass equity curve to K-Ratio smoothness
  - Ensure all new ClosedTrade fields (ReturnOnRisk) are populated
  - _Requirements: 2.3, 6.1, 8.1, 11.3_

- [ ] 16. Update reporters to handle enriched `EquityCurvePoint` and K-Ratio
  - Update `ConsoleReporter` and `MarkdownReporter` to display new equity curve fields
  - Update smoothness display to show K-Ratio interpretation
  - _Requirements: 7.3, 9.2, 11.3_

---

## V2.1 — Execution Realism and Research Robustness (High/Medium)

- [ ] 17. Add `ExecutionRealismProfile`, `ExecutionOptions`, and `ExecutionOutcome` to Core
- [ ] 17.1 Create `ExecutionRealismProfile.cs` enum in `Core/Configuration/`
  - Values: `FastResearch`, `StandardBacktest`, `BrokerConservative`
  - _Requirements: 16.1_
- [ ] 17.2 Create `ExecutionOptions.cs` record in `Core/Configuration/`
  - Fields: `FillModeOverride`, `SlippageModelOverride`, `EnablePartialFills`, `DefaultMaxBarsPending`
  - _Requirements: 16.6_
- [ ] 17.3 Create `ExecutionOutcome.cs` enum in `Core/Events/`
  - Values: `Filled`, `PartiallyFilled`, `Unfilled`, `Rejected`, `Expired`
  - _Requirements: 19.1_
- [ ] 17.4 Update `FillEvent` with `ExecutionOutcome`, `RemainingQuantity`, `RejectionReason` fields
  - _Requirements: 19.2_
- [ ] 17.5 Add `RealismProfile`, `ExecutionOptions`, `SlippageModelOptions`, `SessionCalendarType`, `SessionFilterOptions`, `EnableEventTrace` to `ScenarioConfig`
  - _Requirements: 16.1, 16.6, 17.6, 18.4, 27.1_

- [ ] 18. Implement advanced slippage models (EXR-02)
- [ ] 18.1 Implement `AtrScaledSlippageModel` in `Application/Execution/`
  - Maintain rolling ATR, slippage = ATR * configurable fraction
  - _Requirements: 17.1_
- [ ] 18.2 Implement `PercentOfPriceSlippageModel`
  - Slippage = basePrice * basisPoints / 10000
  - _Requirements: 17.2_
- [ ] 18.3 Implement `SessionAwareSlippageModel`
  - Uses `ISessionCalendar` to widen slippage during illiquid sessions
  - _Requirements: 17.3_
- [ ] 18.4 Implement `VolatilityBucketSlippageModel`
  - Maps recent realised volatility into configurable slippage bands
  - _Requirements: 17.4_
- [ ] 18.5 Write unit tests for all four slippage models — deterministic given same inputs
  - _Requirements: 17.5_

- [ ] 19. Implement session calendar support (EXR-03)
- [ ] 19.1 Create `TradingSession` value object and `ISessionCalendar` interface in `Core/Sessions/`
  - _Requirements: 18.1, 18.2_
- [ ] 19.2 Implement `ForexSessionCalendar` in `Application/Sessions/`
  - Asia, London, NewYork, Overlap session classification
  - _Requirements: 18.3_
- [ ] 19.3 Implement `UsEquitySessionCalendar` in `Application/Sessions/`
  - Pre-market, Regular, After-hours classification
  - _Requirements: 18.3_
- [ ] 19.4 Integrate session filtering into engine loop — skip strategy invocation for bars outside allowed sessions, still run mark-to-market
  - _Requirements: 18.4, 18.5, 18.6_
- [ ] 19.5 Write unit tests for session calendars — edge cases around session boundaries and timezone handling
  - _Requirements: 18.2_

- [ ] 20. Implement partial fills and execution outcomes (EXR-04)
- [ ] 20.1 Update `IExecutionHandler` or add overload returning execution result with partial fill support
  - _Requirements: 19.2_
- [ ] 20.2 Implement partial fill logic in `SimulatedExecutionHandler` — configurable fill ratio, remaining quantity carried forward
  - _Requirements: 19.3_
- [ ] 20.3 Implement order expiry in `ProcessPendingOrders` — `ExecutionOutcome.Expired` when `MaxBarsPending` exceeded
  - _Requirements: 19.4_
- [ ] 20.4 Implement rejection reasons — session closed, insufficient capital, invalid stop
  - _Requirements: 19.5_
- [ ] 20.5 Ensure default path remains simple full fills when partial fills not enabled
  - _Requirements: 19.6_

- [ ] 21. Implement `IPositionSizingPolicy` and sizing implementations (PRM-02)
- [ ] 21.1 Create `IPositionSizingPolicy` interface in `Core/Risk/`
  - _Requirements: 25.1_
- [ ] 21.2 Implement `FixedQuantitySizingPolicy` in `Application/Risk/`
  - _Requirements: 25.2_
- [ ] 21.3 Implement `FixedDollarRiskSizingPolicy`
  - _Requirements: 25.2_
- [ ] 21.4 Implement `PercentEquitySizingPolicy`
  - _Requirements: 25.2_
- [ ] 21.5 Implement `VolatilityTargetSizingPolicy`
  - _Requirements: 25.2_
- [ ] 21.6 Refactor `DefaultRiskLayer` to delegate sizing to active `IPositionSizingPolicy`
  - _Requirements: 25.3_
- [ ] 21.7 Write unit tests for each sizing policy
  - _Requirements: 25.4, 25.5_

- [ ] 22. Add configurable portfolio constraints (PRM-01)
- [ ] 22.1 Create `PortfolioConstraints` configuration class with: max gross exposure, max capital per symbol, max concurrent positions, cooldown bars, max daily loss, max trailing drawdown
  - _Requirements: 24.1, 24.2_
- [ ] 22.2 Integrate constraints into `DefaultRiskLayer.EvaluateOrder` — reject orders that violate any active constraint
  - _Requirements: 24.3, 24.4_
- [ ] 22.3 Wire constraints from `ScenarioConfig.RiskParameters`
  - _Requirements: 24.3_
- [ ] 22.4 Write unit tests for each constraint — verify rejection with `RiskRejection` log
  - _Requirements: 24.4, 24.5_

- [ ] 23. Upgrade walk-forward workflow with composite OOS equity and parameter drift (RSR-01)
- [ ] 23.1 Add `WalkForwardSummary` result type with `CompositeEquityCurve`, `AverageOutOfSampleSharpe`, `WorstWindowDrawdown`, `ParameterDriftScore`
  - _Requirements: 20.3_
- [ ] 23.2 Implement OOS equity curve stitching — chain end equity of window N as start of window N+1
  - _Requirements: 20.2_
- [ ] 23.3 Implement `ParameterDriftScore` — normalised std dev of selected parameter values across windows
  - _Requirements: 20.4_
- [ ] 23.4 Update `WalkForwardWorkflow` to return `WalkForwardSummary`
  - _Requirements: 20.1, 20.5_
- [ ] 23.5 Write unit tests for composite equity stitching and parameter drift calculation
  - _Requirements: 20.2, 20.4_

- [ ] 24. Implement `ParameterStabilityWorkflow` and fragility scoring (RSR-02)
- [ ] 24.1 Create `ParameterStabilityOptions` with `NeighbourhoodPercent` (default 10%)
  - _Requirements: 21.5_
- [ ] 24.2 Implement `ParameterStabilityWorkflow` — evaluate parameter neighbourhood from sweep results
  - Compute local median Sharpe, worst Sharpe, profitable neighbour proportion
  - _Requirements: 21.1, 21.2_
- [ ] 24.3 Compute `FragilityScore = 1 - ProfitableNeighbourProportion`
  - _Requirements: 21.3_
- [ ] 24.4 Write unit tests — fragile island (high score) vs robust plateau (low score)
  - _Requirements: 21.4_

- [ ] 25. Implement `SensitivityAnalysisWorkflow` (RSR-03)
- [ ] 25.1 Create `SensitivityOptions` with configurable perturbation levels
  - _Requirements: 22.2_
- [ ] 25.2 Implement workflow — rerun strategy under spread, slippage, delay, and sizing perturbations
  - _Requirements: 22.1, 22.2_
- [ ] 25.3 Compute `CostSensitivity`, `DelaySensitivity`, `ExecutionRobustnessScore`
  - _Requirements: 22.4, 22.5_
- [ ] 25.4 Write unit tests — verify Sharpe degrades under widened spread
  - _Requirements: 22.3_

- [ ] 26. Implement `RealismSensitivityWorkflow` (EXR-01)
- [ ] 26.1 Implement workflow that runs strategy under all three realism profiles
  - _Requirements: 16.7_
- [ ] 26.2 Create `RealismSensitivityResult` with per-profile metrics and degradation percentages
  - _Requirements: 16.7_
- [ ] 26.3 Write unit tests — verify three profiles produce different results
  - _Requirements: 16.2, 16.3, 16.4, 16.5_

- [ ] 27. Implement regime segmentation (RSR-04)
- [ ] 27.1 Create `RegimePerformanceReport` and `RegimeSegment` result types
  - _Requirements: 23.3_
- [ ] 27.2 Implement volatility regime classification (low/medium/high based on configurable percentile thresholds)
  - _Requirements: 23.2_
- [ ] 27.3 Implement trend regime classification (moving average slope or ADX proxy)
  - _Requirements: 23.2_
- [ ] 27.4 Implement session regime classification using `ISessionCalendar`
  - _Requirements: 23.2_
- [ ] 27.5 Write unit tests — verify trades are classified into correct regime buckets
  - _Requirements: 23.4, 23.5_

- [ ] 28. Add `ExperimentMetadata` to `BacktestResult` (RAD-01)
- [ ] 28.1 Create `ExperimentMetadata` record in `Core/Results/`
  - _Requirements: 26.1_
- [ ] 28.2 Populate metadata in `RunScenarioUseCase` from `ScenarioConfig`
  - _Requirements: 26.3_
- [ ] 28.3 Update reporters to include metadata in output
  - _Requirements: 26.4_
- [ ] 28.4 Write unit test — metadata round-trips through JSON serialisation
  - _Requirements: 26.2_

- [ ] 29. Implement optional event trace mode (RAD-02)
- [ ] 29.1 Create `EventTraceRecord` record in `Core/Results/`
  - _Requirements: 27.2_
- [ ] 29.2 Add trace recording to `BacktestEngine` dispatch — only when `EnableEventTrace` is true
  - Record market events, signals, risk decisions, orders, executions, portfolio transitions
  - _Requirements: 27.2, 27.5_
- [ ] 29.3 Attach trace to `BacktestResult` as optional `IReadOnlyList<EventTraceRecord>`
  - _Requirements: 27.3_
- [ ] 29.4 Ensure zero allocation overhead when trace is disabled
  - _Requirements: 27.4_
- [ ] 29.5 Write unit tests — trace enabled records events, trace disabled produces null/empty
  - _Requirements: 27.4_

- [ ] 30. Extend trade analytics: MAE, MFE, recovery factor, average bars, longest flat period (RPT-01)
- [ ] 30.1 Add MAE/MFE tracking to engine loop — track per-bar P&L extremes for open positions
  - _Requirements: 28.2_
- [ ] 30.2 Add `MAE` and `MFE` fields to `ClosedTrade` record
  - _Requirements: 28.2_
- [ ] 30.3 Implement `ComputeRecoveryFactor` in `MetricsCalculator`
  - _Requirements: 28.3_
- [ ] 30.4 Implement `ComputeAverageBarsInTrade` and `ComputeLongestFlatPeriod`
  - _Requirements: 28.1_
- [ ] 30.5 Add `RecoveryFactor` to `BacktestResult`
  - _Requirements: 28.3_
- [ ] 30.6 Update reporters to display extended analytics
  - _Requirements: 28.4_
- [ ] 30.7 Write unit tests for recovery factor, MAE/MFE, average bars, longest flat period
  - _Requirements: 28.1_

- [ ] 31. Implement `StrategyComparisonWorkflow` (RPT-02)
- [ ] 31.1 Implement workflow that validates matching assumptions across results
  - _Requirements: 29.1, 29.2_
- [ ] 31.2 Produce comparison table with CAGR, Sharpe, Sortino, MaxDrawdown, ProfitFactor, Expectancy, RecoveryFactor
  - _Requirements: 29.3_
- [ ] 31.3 Write unit tests — mismatched assumptions produce validation error
  - _Requirements: 29.2, 29.4_

- [ ] 32. Update steering documents for V2 + V2.1
- [ ] 32.1 Add `## V2 Scope` section to `product.md` — engine correctness overhaul, V3 is UI
  - _Requirements: 15.1_
- [ ] 32.2 Add `## V2.1 Scope` section to `product.md` — execution realism, robustness, reproducibility
  - _Requirements: 30.1_
- [ ] 32.3 Update `domain-boundaries.md` — add `FillMode`, `BarsPerYear`, `EquityCurvePoint` (enriched), `ReturnOnRisk`, `ExecutionRealismProfile`, `ExperimentMetadata`, `ISessionCalendar`, `IPositionSizingPolicy`, `EventTraceRecord`, `ExecutionOutcome` to Core owns list; note `Direction.Short` removed
  - _Requirements: 15.2, 30.2_
- [ ] 32.4 Add `## Backtest Correctness Tests` section to `testing-standards.md`
  - _Requirements: 15.3, 30.3_
- [ ] 32.5 Update `tech.md` — `BarsPerYear` is canonical annualisation source; stochastic workflows must accept seeds
  - _Requirements: 15.4, 30.4_
- [ ] 32.6 Update `strategy-registry.md` — strategies must not embed market-hours logic or execution cost modelling
  - _Requirements: 30.5_
