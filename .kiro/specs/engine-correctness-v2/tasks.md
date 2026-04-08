# Implementation Plan — TradingResearchEngine V2 + V2.1

## Overview

This plan implements the engine correctness overhaul (V2) and the execution realism / research robustness follow-on (V2.1). V2 tasks are critical priority and must be completed first. V2.1 tasks build on the corrected engine.

All tasks are coding tasks executable by a coding agent. No deployment, user testing, or manual verification tasks.

---

## V2 — Engine Correctness (Critical) ✅ COMPLETE

- [x] 1. Add `FillMode` enum and `BarsPerYear` to `ScenarioConfig`
- [x] 1.1 Create `FillMode.cs` in `Core/Configuration/` with `NextBarOpen` and `SameBarClose` values
  - _Requirements: 1.4, 6.1, 6.2, 6.3, 6.4_
- [x] 1.2 Add `FillMode` and `BarsPerYear` fields to `ScenarioConfig` record
  - _Requirements: 1.4, 6.1, 6.4_

- [x] 2. Remove `Direction.Short` and update to long-only semantics (BUG-04)
- [x] 2.1 Update `Direction` enum in `Core/Events/Enums.cs` to `{ Long, Flat }`
  - _Requirements: 4.1, 4.3_
- [x] 2.2 Update all strategy implementations to use `Direction.Flat` instead of `Direction.Short`
  - _Requirements: 4.2, 4.5_
- [x] 2.3 Update `Portfolio.Update` to handle `Direction.Flat` correctly
  - _Requirements: 4.4, 4.5_
- [x] 2.4 Update `IStrategy` interface XML doc to state long-only scope
  - _Requirements: 4.3_

- [x] 3. Enrich `EquityCurvePoint` and `ClosedTrade` (BUG-03 + REQ-V2-04)
- [x] 3.1 Update `EquityCurvePoint` record to include `CashBalance`, `UnrealisedPnl`, `RealisedPnl`, `OpenPositionCount`
  - _Requirements: 3.3, 7.1_
- [x] 3.2 Add `ReturnOnRisk` computed property to `ClosedTrade`
  - _Requirements: 5.3, 5.4, 8.1, 8.2_
- [x] 3.3 Update all code that constructs `EquityCurvePoint` to pass the new fields
  - _Requirements: 3.3, 7.1_

- [x] 4. Implement continuous mark-to-market in `Portfolio` (BUG-03)
- [x] 4.1 Add public `MarkToMarket(string symbol, decimal price, DateTimeOffset timestamp)` method to `Portfolio`
  - _Requirements: 3.1, 3.2, 3.4, 3.5_
- [x] 4.2 Remove equity curve append from `Portfolio.Update(FillEvent)` — mark-to-market now owns this
  - _Requirements: 3.1_

- [x] 5. Implement pending-order queue and corrected engine loop (BUG-01)
- [x] 5.1 Add `PendingOrders` list and `FillMode` to `RunState` in `BacktestEngine`
  - _Requirements: 1.1, 1.4_
- [x] 5.2 Implement `ProcessPendingOrders` method that fills pending orders using new bar's Open price
  - _Requirements: 1.2, 1.3, 1.6_
- [x] 5.3 Restructure `Dispatch` / main loop to follow the 4-step per-bar order
  - _Requirements: 1.3, 1.5, 1.6_
- [x] 5.4 Implement `RouteApprovedOrder` that sends to pending queue (NextBarOpen) or immediate dispatch (SameBarClose)
  - _Requirements: 1.1, 1.5_

- [x] 6. Add `StopPrice`, `MaxBarsPending`, `StopTriggered` to `OrderEvent` and `StopLimit` to `OrderType` (IMP-05)
- [x] 6.1 Update `OrderEvent` record with `decimal? StopPrice`, `int MaxBarsPending = 0`, `bool StopTriggered = false`
  - _Requirements: 13.6, 13.10_
- [x] 6.2 Add `StopLimit` to `OrderType` enum
  - _Requirements: 13.7_

- [x] 7. Implement intra-bar fill logic for Limit, StopMarket, StopLimit orders (IMP-05)
- [x] 7.1 Implement `TryFillLimit` in engine
  - _Requirements: 13.1, 13.2_
- [x] 7.2 Implement `TryFillStopMarket`
  - _Requirements: 13.3, 13.4_
- [x] 7.3 Implement `TryFillStopLimit`
  - _Requirements: 13.8, 13.9, 13.10_
- [x] 7.4 Integrate fill logic into `ProcessPendingOrders` with unfilled orders remaining in queue
  - _Requirements: 13.5_

- [x] 8. Fix Sharpe and Sortino to use equity curve period returns (BUG-02)
- [x] 8.1 Add `GetPeriodReturns(IReadOnlyList<EquityCurvePoint>)` helper to `MetricsCalculator`
  - _Requirements: 2.1_
- [x] 8.2 Rewrite `ComputeSharpeRatio` to accept `IReadOnlyList<EquityCurvePoint>` and `int barsPerYear`
  - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6_
- [x] 8.3 Rewrite `ComputeSortinoRatio` to accept equity curve and `barsPerYear`
  - _Requirements: 2.2, 2.3_
- [x] 8.4 Update `BacktestEngine.BuildResult` to pass `config.BarsPerYear` and equity curve to new metric signatures
  - _Requirements: 2.3_

- [x] 9. Replace R² smoothness with K-Ratio (IMP-03)
- [x] 9.1 Rewrite `ComputeEquityCurveSmoothness` to compute K-Ratio
  - _Requirements: 11.1, 11.2, 11.4_
- [x] 9.2 Update reporters that display smoothness to reflect K-Ratio interpretation
  - _Requirements: 11.3_

- [x] 10. Fix Monte Carlo to resample normalised returns (BUG-05)
- [x] 10.1 Update `MonteCarloWorkflow.RunSimulation` to use `ClosedTrade.ReturnOnRisk` instead of `NetPnl`
  - _Requirements: 5.1_
- [x] 10.2 Change equity path reconstruction from additive to multiplicative
  - _Requirements: 5.2_

- [x] 11. Add bid/ask `Quote` fields to `TickEvent` and implement bid/ask-aware fills (IMP-04)
- [x] 11.1 Add `Quote` record to `Core/Events/ValueTypes.cs`
  - _Requirements: 12.1_
- [x] 11.2 Add `Quote? Bid` and `Quote? Ask` to `TickEvent` record
  - _Requirements: 12.1_
- [x] 11.3 Update `SimulatedExecutionHandler.Execute` for bid/ask-aware tick fills
  - _Requirements: 12.2, 12.3, 12.4_

- [x] 12. Replace O(n) SMA with rolling sum in strategies (IMP-01)
- [x] 12.1 Refactor `SmaCrossoverStrategy` to use rolling accumulators
  - _Requirements: 9.1, 9.2_
- [x] 12.2 Refactor `MeanReversionStrategy` to use rolling SMA + rolling variance
  - _Requirements: 9.3_
- [x] 12.3 Refactor `RsiStrategy` to use Wilder smoothing accumulators
  - _Requirements: 9.3_
- [x] 12.4 Refactor `BollingerBandsStrategy` to use rolling SMA + rolling sum-of-squares
  - _Requirements: 9.3_

- [x] 13. Add ADF recheck interval cache and fix biased variance (IMP-02)
- [x] 13.1 Add `adfRecheckInterval` constructor parameter (default 20) to `StationaryMeanReversionStrategy`
  - _Requirements: 10.1, 10.2, 10.4_
- [x] 13.2 Fix biased variance: change `sumYl2 / m` to `sumYl2 / (m - 1)` in `IsStationary`
  - _Requirements: 10.3_

- [x] 14. Write V2 regression unit tests (REQ-V2-05)
- [x] 14.1 BUG-02: Flat equity curve → Sharpe null; linear rising curve → positive Sharpe
  - _Requirements: 14.2_
- [x] 14.2 BUG-03: MarkToMarket updates TotalEquity between fills
  - _Requirements: 14.3_
- [x] 14.3 BUG-04: Flat fill with no position → cash unchanged
  - _Requirements: 14.4_
- [x] 14.4 BUG-05: ReturnOnRisk normalisation verified; zero denominator returns 0
  - _Requirements: 14.5_

- [x] 15. Update `BacktestEngine.BuildResult` to use all corrected metric signatures
  - _Requirements: 2.3, 6.1, 8.1, 11.3_

- [x] 16. Update reporters to handle enriched `EquityCurvePoint` and K-Ratio
  - _Requirements: 7.3, 9.2, 11.3_

---

## V2.1 — Execution Realism and Research Robustness (High/Medium)

- [x] 17. Add `ExecutionRealismProfile`, `ExecutionOptions`, and `ExecutionOutcome` to Core



- [x] 17.1 Create `ExecutionRealismProfile.cs` enum in `Core/Configuration/`


  - Values: `FastResearch`, `StandardBacktest`, `BrokerConservative`
  - _Requirements: 16.1_
- [x] 17.2 Create `ExecutionOptions.cs` record in `Core/Configuration/`


  - Fields: `FillModeOverride`, `SlippageModelOverride`, `EnablePartialFills`, `DefaultMaxBarsPending`
  - _Requirements: 16.6_
- [x] 17.3 Create `ExecutionOutcome.cs` enum in `Core/Events/`


  - Values: `Filled`, `PartiallyFilled`, `Unfilled`, `Rejected`, `Expired`
  - _Requirements: 19.1_
- [x] 17.4 Update `FillEvent` with `ExecutionOutcome`, `RemainingQuantity`, `RejectionReason` fields


  - _Requirements: 19.2_
- [x] 17.5 Add `RealismProfile`, `ExecutionOptions`, `SlippageModelOptions`, `SessionCalendarType`, `SessionFilterOptions`, `EnableEventTrace` to `ScenarioConfig`


  - _Requirements: 16.1, 16.6, 17.6, 18.4, 27.1_

- [x] 18. Implement advanced slippage models (EXR-02)



- [x] 18.1 Implement `AtrScaledSlippageModel` in `Application/Execution/`

  - _Requirements: 17.1_

- [x] 18.2 Implement `PercentOfPriceSlippageModel`

  - _Requirements: 17.2_

- [ ] 18.3 Implement `SessionAwareSlippageModel`
  - _Requirements: 17.3_
- [ ] 18.4 Implement `VolatilityBucketSlippageModel`
  - _Requirements: 17.4_
- [-] 18.5 Write unit tests for all four slippage models





  - _Requirements: 17.5_

- [x] 19. Implement session calendar support (EXR-03)


- [x] 19.1 Create `TradingSession` value object and `ISessionCalendar` interface in `Core/Sessions/`

  - _Requirements: 18.1, 18.2_





















- [x] 19.2 Implement `ForexSessionCalendar` in `Application/Sessions/`

  - _Requirements: 18.3_

- [x] 19.3 Implement `UsEquitySessionCalendar` in `Application/Sessions/`

  - _Requirements: 18.3_



- [x] 19.4 Integrate session filtering into engine loop

  - _Requirements: 18.4, 18.5, 18.6_

- [x] 19.5 Write unit tests for session calendars

  - _Requirements: 18.2_





- [x] 20. Implement partial fills and execution outcomes (EXR-04)

- [x] 20.1 Update `IExecutionHandler` or add overload for partial fill support

  - _Requirements: 19.2_

- [x] 20.2 Implement partial fill logic in `SimulatedExecutionHandler`



  - _Requirements: 19.3_

- [x] 20.3 Implement order expiry with `ExecutionOutcome.Expired`

  - _Requirements: 19.4_

- [x] 20.4 Implement rejection reasons



  - _Requirements: 19.5_

- [x] 20.5 Ensure default path remains simple full fills

  - _Requirements: 19.6_





- [x] 21. Implement `IPositionSizingPolicy` and sizing implementations (PRM-02)

- [x] 21.1 Create `IPositionSizingPolicy` interface in `Core/Risk/`

  - _Requirements: 25.1_



- [x] 21.2 Implement `FixedQuantitySizingPolicy` in `Application/Risk/`

  - _Requirements: 25.2_

- [x] 21.3 Implement `FixedDollarRiskSizingPolicy`

  - _Requirements: 25.2_

- [x] 21.4 Implement `PercentEquitySizingPolicy`



  - _Requirements: 25.2_

- [x] 21.5 Implement `VolatilityTargetSizingPolicy`

  - _Requirements: 25.2_

- [-] 21.6 Refactor `DefaultRiskLayer` to delegate sizing to active `IPositionSizingPolicy`

  - _Requirements: 25.3_
- [ ] 21.7 Write unit tests for each sizing policy
  - _Requirements: 25.4, 25.5_

- [ ] 22. Add configurable portfolio constraints (PRM-01)
- [ ] 22.1 Create `PortfolioConstraints` configuration class
  - _Requirements: 24.1, 24.2_
- [ ] 22.2 Integrate constraints into `DefaultRiskLayer.EvaluateOrder`
  - _Requirements: 24.3, 24.4_
- [ ] 22.3 Wire constraints from `ScenarioConfig.RiskParameters`
  - _Requirements: 24.3_
- [ ] 22.4 Write unit tests for each constraint
  - _Requirements: 24.4, 24.5_

- [ ] 23. Upgrade walk-forward workflow with composite OOS equity and parameter drift (RSR-01)
- [ ] 23.1 Add `WalkForwardSummary` result type
  - _Requirements: 20.3_
- [ ] 23.2 Implement OOS equity curve stitching
  - _Requirements: 20.2_
- [ ] 23.3 Implement `ParameterDriftScore`
  - _Requirements: 20.4_
- [ ] 23.4 Update `WalkForwardWorkflow` to return `WalkForwardSummary`
  - _Requirements: 20.1, 20.5_
- [ ] 23.5 Write unit tests for composite equity stitching and parameter drift
  - _Requirements: 20.2, 20.4_

- [ ] 24. Implement `ParameterStabilityWorkflow` and fragility scoring (RSR-02)
- [ ] 24.1 Create `ParameterStabilityOptions`
  - _Requirements: 21.5_
- [ ] 24.2 Implement `ParameterStabilityWorkflow`
  - _Requirements: 21.1, 21.2_
- [ ] 24.3 Compute `FragilityScore`
  - _Requirements: 21.3_
- [ ] 24.4 Write unit tests
  - _Requirements: 21.4_

- [ ] 25. Implement `SensitivityAnalysisWorkflow` (RSR-03)
- [ ] 25.1 Create `SensitivityOptions`
  - _Requirements: 22.2_
- [ ] 25.2 Implement workflow with perturbations
  - _Requirements: 22.1, 22.2_
- [ ] 25.3 Compute `CostSensitivity`, `DelaySensitivity`, `ExecutionRobustnessScore`
  - _Requirements: 22.4, 22.5_
- [ ] 25.4 Write unit tests
  - _Requirements: 22.3_

- [ ] 26. Implement `RealismSensitivityWorkflow` (EXR-01)
- [ ] 26.1 Implement workflow across three realism profiles
  - _Requirements: 16.7_
- [ ] 26.2 Create `RealismSensitivityResult`
  - _Requirements: 16.7_
- [ ] 26.3 Write unit tests
  - _Requirements: 16.2, 16.3, 16.4, 16.5_

- [ ] 27. Implement regime segmentation (RSR-04)
- [ ] 27.1 Create `RegimePerformanceReport` and `RegimeSegment` result types
  - _Requirements: 23.3_
- [ ] 27.2 Implement volatility regime classification
  - _Requirements: 23.2_
- [ ] 27.3 Implement trend regime classification
  - _Requirements: 23.2_
- [ ] 27.4 Implement session regime classification
  - _Requirements: 23.2_
- [ ] 27.5 Write unit tests
  - _Requirements: 23.4, 23.5_

- [ ] 28. Add `ExperimentMetadata` to `BacktestResult` (RAD-01)
- [ ] 28.1 Create `ExperimentMetadata` record in `Core/Results/`
  - _Requirements: 26.1_
- [ ] 28.2 Populate metadata in `RunScenarioUseCase`
  - _Requirements: 26.3_
- [ ] 28.3 Update reporters to include metadata
  - _Requirements: 26.4_
- [ ] 28.4 Write unit test for metadata JSON round-trip
  - _Requirements: 26.2_

- [ ] 29. Implement optional event trace mode (RAD-02)
- [x] 29.1 Create `EventTraceRecord` record in `Core/Results/`

  - _Requirements: 27.2_

- [ ] 29.2 Add trace recording to `BacktestEngine` dispatch
  - _Requirements: 27.2, 27.5_

- [ ] 29.3 Attach trace to `BacktestResult`
  - _Requirements: 27.3_
- [x] 29.4 Ensure zero allocation overhead when disabled

  - _Requirements: 27.4_

- [ ] 29.5 Write unit tests
  - _Requirements: 27.4_

- [-] 30. Extend trade analytics: MAE, MFE, recovery factor, average bars, longest flat period (RPT-01)

- [ ] 30.1 Add MAE/MFE tracking to engine loop
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
- [ ] 30.7 Write unit tests
  - _Requirements: 28.1_

- [ ] 31. Implement `StrategyComparisonWorkflow` (RPT-02)
- [ ] 31.1 Implement workflow with assumption validation
  - _Requirements: 29.1, 29.2_
- [ ] 31.2 Produce comparison table
  - _Requirements: 29.3_
- [ ] 31.3 Write unit tests
  - _Requirements: 29.2, 29.4_

- [-] 32. Update steering documents for V2 + V2.1

- [ ] 32.1 Add `## V2 Scope` section to `product.md`
  - _Requirements: 15.1_
- [ ] 32.2 Add `## V2.1 Scope` section to `product.md`
  - _Requirements: 30.1_
- [ ] 32.3 Update `domain-boundaries.md`
  - _Requirements: 15.2, 30.2_
- [ ] 32.4 Add `## Backtest Correctness Tests` section to `testing-standards.md`
  - _Requirements: 15.3, 30.3_
- [ ] 32.5 Update `tech.md`
  - _Requirements: 15.4, 30.4_
- [ ] 32.6 Update `strategy-registry.md`
  - _Requirements: 30.5_
