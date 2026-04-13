# Product Goals and Module Boundaries

## Purpose

TradingResearchEngine is an event-driven backtesting engine for quantitative strategy research.
Its primary output is a structured `BacktestResult` that feeds into research workflows and a prop-firm evaluation suite.

## V1 Scope

- Bar-level and tick-level replay via a heartbeat loop
- Pluggable strategy, risk, slippage, and commission components
- Research workflows: parameter sweep, variance testing, Monte Carlo, walk-forward
- Prop-firm challenge and instant-funding economics modelling
- CLI host (argument-driven + interactive) and ASP.NET Core minimal API host
- CSV and HTTP REST data providers
- JSON file persistence
- Console and Markdown reporting

## Module Boundaries

### Core Engine
Owns the event hierarchy, EventQueue, heartbeat loop, dispatch table, Portfolio, and MetricsCalculator.
No business logic from Application or Infrastructure may leak into Core.

### Research Workflows
First-class Application-layer orchestrations. They consume `BacktestResult` and run engine instances.
They do not modify Core abstractions.

### PropFirmModule
A bounded Application-layer module. It consumes `BacktestResult` and research outputs.
It does not extend or modify Core engine abstractions.
It operates in USD only.

### Infrastructure
Concrete implementations of Core/Application interfaces only.
No domain logic. No direct references to engine internals beyond the interfaces defined in Core.

### Hosts (Cli, Api)
Composition roots only. Wire DI, parse input, invoke use cases, render output.
No business logic in hosts.

## Out of Scope for V1

- Live trading or paper trading
- Database persistence (designed for substitution; V1 is JSON files)
- Named third-party data provider integrations (Alpaca, Polygon) beyond the HTTP stub
- Multi-currency portfolio tracking

## V2 Scope — Engine Correctness Overhaul

V2 is engine-only. UI rework is V3.

- Eliminated look-ahead bias: pending-order queue with 4-step per-bar processing (BUG-01)
- Sharpe/Sortino computed from equity curve period returns with configurable BarsPerYear (BUG-02)
- Continuous mark-to-market on every bar with enriched EquityCurvePoint (BUG-03)
- Direction.Short removed; long-only V2 scope (BUG-04) — V5 re-adds `Direction.Short` for exhaustive switch coverage with `LongOnlyGuard` runtime guard; short execution deferred to V6
- Monte Carlo resamples normalised ReturnOnRisk, multiplicative path reconstruction (BUG-05)
- O(1) rolling SMA in all strategies (IMP-01)
- ADF stationarity test cached with recheck interval (IMP-02)
- K-Ratio replaces R² smoothness (IMP-03)
- Bid/ask-aware tick fills (IMP-04)
- Intra-bar limit, stop-market, and stop-limit fill logic (IMP-05)
- FillMode (NextBarOpen default) and BarsPerYear (252 default) on ScenarioConfig
- ClosedTrade.ReturnOnRisk computed property
- V2 regression unit tests for all bug fixes

## V2.1 Scope — Execution Realism, Research Robustness, and Engine Maturity

V2.1 is still engine-only. UI rework remains V3.

- Execution realism profiles (FastResearch, StandardBacktest, BrokerConservative)
- ExecutionResult as canonical IExecutionHandler return type with partial fill support
- Advanced slippage models: ATR-scaled, percent-of-price, session-aware, volatility-bucket
- Session calendar support (ISessionCalendar, ForexSessionCalendar, UsEquitySessionCalendar)
- IPositionSizingPolicy with 4 implementations; DefaultRiskLayer delegates sizing
- Configurable portfolio constraints (max positions, max capital per symbol, max gross exposure)
- Walk-forward upgrade: composite OOS equity curve, parameter drift score
- Parameter stability workflow with fragility scoring
- Sensitivity analysis workflow (cost and delay perturbations)
- Realism sensitivity workflow (same strategy across 3 profiles)
- Regime segmentation (volatility, trend, session)
- ExperimentMetadata on BacktestResult for reproducibility
- Optional event trace mode (zero overhead when disabled)
- Extended analytics: recovery factor, longest flat period
- Strategy comparison workflow under matched assumptions

## V3 Scope — Product & UX

V3 transforms the engine into a user-facing research product. Single-user, local/single-tenant.

- Strategy identity model: StrategyIdentity, StrategyVersion, StudyRecord as persistent Application-layer concepts
- Strategy templates: 6 pre-built templates for all built-in strategies
- Guided strategy builder: 5-step wizard (Template → Market → Rules → Execution → Save) with advanced mode toggle
- Strategy library: browse, version, and manage strategies with linked runs and studies
- Research explorer: browse and launch studies from strategy context
- Prop firm rule packs: PropFirmRulePack with multi-phase ChallengePhase support
- Pre-built firm packs: FTMO 100k, MyFundedFX 200k, TopStep 100k, The5ers 60k
- Phase-by-phase evaluation with pass/near-breach/fail status and margin display
- Robustness warnings: automatic badges for suspicious metrics (Sharpe > 3, trades < 30, K-Ratio < 0, etc.)
- Multi-format export: Markdown report, CSV trade log, JSON result
- Failed/cancelled run banners with Edit & Retry action
- JSON-based persistence: JsonStrategyRepository, JsonStudyRepository, SettingsService
- DataFileService for CSV discovery, validation, and preview

## Out of Scope for V3

- Multi-user / authentication / team features
- Database persistence (JSON files only)
- Live or paper trading
- Short-selling
