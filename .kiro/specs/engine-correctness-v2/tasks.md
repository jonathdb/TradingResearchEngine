# Implementation Plan — TradingResearchEngine V2 + V2.1

## Overview

Engine correctness overhaul (V2) and execution realism / research robustness follow-on (V2.1). All tasks complete.

---

## V2 — Engine Correctness ✅ COMPLETE

- [x] 1. Add `FillMode` enum and `BarsPerYear` to `ScenarioConfig`
- [x] 2. Remove `Direction.Short` and update to long-only semantics (BUG-04)
- [x] 3. Enrich `EquityCurvePoint` and `ClosedTrade` (BUG-03 + REQ-V2-04)
- [x] 4. Implement continuous mark-to-market in `Portfolio` (BUG-03)
- [x] 5. Implement pending-order queue and corrected engine loop (BUG-01)
- [x] 6. Add `StopPrice`, `MaxBarsPending`, `StopTriggered` to `OrderEvent` and `StopLimit` to `OrderType` (IMP-05)
- [x] 7. Implement intra-bar fill logic for Limit, StopMarket, StopLimit orders (IMP-05)
- [x] 8. Fix Sharpe and Sortino to use equity curve period returns (BUG-02)
- [x] 9. Replace R² smoothness with K-Ratio (IMP-03)
- [x] 10. Fix Monte Carlo to resample normalised returns (BUG-05)
- [x] 11. Add bid/ask `Quote` fields to `TickEvent` and implement bid/ask-aware fills (IMP-04)
- [x] 12. Replace O(n) SMA with rolling sum in strategies (IMP-01)
- [x] 13. Add ADF recheck interval cache and fix biased variance (IMP-02)
- [x] 14. Write V2 regression unit tests (REQ-V2-05)
- [x] 15. Update `BacktestEngine.BuildResult` to use all corrected metric signatures
- [x] 16. Update reporters to handle enriched `EquityCurvePoint` and K-Ratio

---

## V2.1 — Execution Realism and Research Robustness ✅ COMPLETE

- [x] 17. Add `ExecutionRealismProfile`, `ExecutionOptions`, and `ExecutionOutcome` to Core
- [x] 18. Implement advanced slippage models (EXR-02)
- [x] 19. Implement session calendar support (EXR-03)
- [x] 20. Implement partial fills and execution outcomes (EXR-04)
- [x] 21. Implement `IPositionSizingPolicy` and sizing implementations (PRM-02)
- [x] 22. Add configurable portfolio constraints (PRM-01)
- [x] 23. Upgrade walk-forward workflow with composite OOS equity and parameter drift (RSR-01)
- [x] 24. Implement `ParameterStabilityWorkflow` and fragility scoring (RSR-02)
- [x] 25. Implement `SensitivityAnalysisWorkflow` (RSR-03)
- [x] 26. Implement `RealismSensitivityWorkflow` (EXR-01)
- [x] 27. Implement regime segmentation (RSR-04)
- [x] 28. Add `ExperimentMetadata` to `BacktestResult` (RAD-01)
- [x] 29. Implement optional event trace mode (RAD-02)
- [x] 30. Extend trade analytics: recovery factor, longest flat period (RPT-01)
- [x] 31. Implement `StrategyComparisonWorkflow` (RPT-02)
- [x] 32. Update steering documents for V2 + V2.1
