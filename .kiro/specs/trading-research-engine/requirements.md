# Requirements Document

## Introduction

TradingResearchEngine is a .NET 8 / C# 12 solution whose primary product is an event-driven backtesting engine inspired by the QuantStart event-driven architecture. The outer heartbeat loop drives the system; an inner event-queue loop dispatches typed events (MarketData, Signal, Order, Fill) through a pipeline of loosely coupled components: DataHandler → Strategy → RiskLayer → ExecutionHandler → Analytics/Reporter.

Research and validation workflows (parameter sweeps, Monte Carlo simulation, walk-forward analysis) are first-class capabilities built directly on the engine. A prop-firm evaluation suite is a bounded module layered on top of the engine and research layer; it does not extend or modify core engine abstractions.

The solution targets a clean-architecture layout:

```
TradingResearchEngine.sln
src/TradingResearchEngine.Core          # domain abstractions, event types, interfaces
src/TradingResearchEngine.Application   # use cases, orchestration, research workflows
src/TradingResearchEngine.Infrastructure # data providers, persistence, execution adapters
src/TradingResearchEngine.Cli           # argument-driven + interactive CLI host
src/TradingResearchEngine.Api           # ASP.NET Core minimal API host
tests/TradingResearchEngine.UnitTests
tests/TradingResearchEngine.IntegrationTests
```

All public APIs in Core and Application carry XML doc comments. No magic numbers appear in engine logic; all thresholds and defaults are named constants or IOptions<T>-bound configuration fields. Nullable reference types and implicit usings are enabled solution-wide.

---

## Glossary

- **Engine**: The TradingResearchEngine event-driven backtesting runtime.
- **EventQueue**: The in-memory FIFO queue that holds typed events during a simulation run.
- **MarketDataEvent**: An event carrying either a bar (OHLCV) or a full order-book tick (bid/ask depth + last trade).
- **BarEvent**: A MarketDataEvent subtype carrying Open, High, Low, Close, Volume, and Timestamp for a single instrument and interval.
- **TickEvent**: A MarketDataEvent subtype carrying bid/ask depth levels and last-trade (price, volume, timestamp) for a single instrument.
- **SignalEvent**: An event produced by a Strategy indicating a directional view (long/short/flat) on an instrument.
- **OrderEvent**: An event representing a concrete order (instrument, direction, quantity, order type) to be sent to the ExecutionHandler.
- **FillEvent**: An event representing a confirmed execution (instrument, direction, quantity, fill price, commission, slippage, timestamp).
- **DataHandler**: The component that reads market data from a DataProvider and publishes MarketDataEvents to the EventQueue.
- **IDataProvider**: The abstracted interface for market data sources; concrete V1 implementations are CsvDataProvider and HttpRestDataProvider.
- **Strategy**: The component that consumes MarketDataEvents and produces SignalEvents or OrderEvents.
- **RiskLayer**: The mandatory pipeline component that intercepts all orders (from Signal conversion or direct Strategy generation) and applies position sizing, exposure limits, and rule enforcement before forwarding an OrderEvent to the ExecutionHandler.
- **ExecutionHandler**: The component that receives OrderEvents, applies slippage and commission models, and publishes FillEvents.
- **SlippageModel**: A pluggable abstraction (ISlippageModel) that computes fill-price adjustment given an order and market state.
- **CommissionModel**: A pluggable abstraction (ICommissionModel) that computes commission cost given a fill.
- **Portfolio**: The component that tracks open positions, realised/unrealised P&L, cash balance, and equity curve by consuming FillEvents.
- **EquityCurve**: The time-ordered series of portfolio equity values produced during a simulation run.
- **BacktestResult**: The structured output of a completed backtest run, including EquityCurve, trade list, and performance metrics.
- **ResearchWorkflow**: An Application-layer orchestration that runs one or more Engine instances to produce comparative or statistical results.
- **ParameterSweep**: A ResearchWorkflow that executes the Engine across a grid of strategy/risk parameter combinations.
- **MonteCarloSimulation**: A ResearchWorkflow that runs the Engine N times with randomised trade-sequence perturbations to produce a distribution of outcomes.
- **WalkForwardAnalysis**: A ResearchWorkflow that partitions historical data into sequential in-sample/out-of-sample windows and runs the Engine on each.
- **PropFirmModule**: The bounded Application-layer module that models prop-firm challenge and funding account economics; it consumes BacktestResults and ResearchWorkflow outputs but does not modify Core engine abstractions.
- **FirmRuleSet**: A JSON-configurable set of prop-firm-specific rules (drawdown limits, payout caps, minimum trading days, consistency rules) consumed by the PropFirmModule.
- **ScenarioConfig**: A JSON file that fully describes a single simulation run (data source, strategy parameters, risk parameters, research workflow type, and optional PropFirmModule config).
- **VariancePreset**: One of three named scenario variants — Conservative, Base, or Strong — or a user-defined variant, used by both the ResearchWorkflow and PropFirmModule.
- **Repository**: The abstracted persistence interface (IRepository<T>) whose V1 implementation writes and reads JSON files; designed for later substitution with a database adapter.
- **CLI**: The TradingResearchEngine.Cli host; supports argument-driven mode (--scenario <file>) and interactive prompt mode (when no arguments are supplied).
- **API**: The TradingResearchEngine.Api ASP.NET Core minimal API host.

---

## Requirements

---

### Requirement 1: Event Abstractions and EventQueue

**User Story:** As an engine developer, I want a strongly-typed event hierarchy and an in-memory event queue, so that all engine components communicate exclusively through typed events without direct coupling.

#### Acceptance Criteria

1. THE Engine SHALL define a sealed `EngineEvent` base record from which `MarketDataEvent`, `SignalEvent`, `OrderEvent`, and `FillEvent` derive.
2. THE EventQueue SHALL expose `Enqueue(EngineEvent)` and `TryDequeue(out EngineEvent)` operations backed by a thread-safe in-memory queue.
3. WHEN the EventQueue is empty, THE Engine SHALL exit the inner event-dispatch loop and advance the heartbeat loop to the next data step.
4. THE Engine SHALL process events in FIFO order within a single heartbeat step.
5. IF an unrecognised event type is dequeued, THEN THE Engine SHALL log a warning and discard the event without throwing an exception.

---

### Requirement 2: MarketDataEvent Hierarchy — Bar and Tick

**User Story:** As a strategy developer, I want both bar (OHLCV) and full order-book tick events, so that I can implement strategies that operate on either data granularity without changing the engine pipeline.

#### Acceptance Criteria

1. THE Engine SHALL define `BarEvent` as a `MarketDataEvent` subtype carrying `Symbol`, `Interval`, `Open`, `High`, `Low`, `Close`, `Volume`, and `Timestamp` fields, all non-nullable.
2. THE Engine SHALL define `TickEvent` as a `MarketDataEvent` subtype carrying `Symbol`, `Timestamp`, `BidLevels` (ordered list of price/size pairs), `AskLevels` (ordered list of price/size pairs), and `LastTrade` (price, volume, timestamp).
3. WHEN a `BarEvent` is published, THE DataHandler SHALL populate all OHLCV fields with values sourced from the active IDataProvider.
4. WHEN a `TickEvent` is published, THE DataHandler SHALL populate at least one bid level, at least one ask level, and a non-null `LastTrade`.
5. IF a data record from the IDataProvider is missing a required field, THEN THE DataHandler SHALL skip that record, increment a `MalformedRecordCount` counter, and continue processing.

---

### Requirement 3: Heartbeat Loop and Bar-by-Bar Replay

**User Story:** As a backtester, I want a bar-by-bar replay loop driven by an outer heartbeat, so that I can simulate a strategy's behaviour across a historical bar series in deterministic order.

#### Acceptance Criteria

1. THE Engine SHALL implement an outer heartbeat loop that advances one data step per iteration until the IDataProvider signals end-of-data.
2. WHEN the heartbeat loop advances, THE DataHandler SHALL fetch the next bar from the IDataProvider and enqueue a `BarEvent`.
3. THE Engine SHALL process all events in the EventQueue before advancing to the next heartbeat step.
4. WHEN the IDataProvider signals end-of-data, THE Engine SHALL drain the EventQueue, finalise the Portfolio, and publish a `BacktestResult`.
5. THE Engine SHALL support deterministic replay: given the same ScenarioConfig and data source, two runs SHALL produce identical `BacktestResult` values.

---

### Requirement 4: Tick-Level Replay Loop

**User Story:** As a strategy developer, I want a tick-level replay loop, so that I can backtest strategies that require intra-bar bid/ask and last-trade data.

#### Acceptance Criteria

1. THE Engine SHALL implement a tick-level replay mode that enqueues a `TickEvent` for each tick record from the IDataProvider.
2. WHEN tick-level replay mode is active, THE Engine SHALL use the same heartbeat-loop and event-dispatch architecture as bar-level replay.
3. THE ScenarioConfig SHALL include a `ReplayMode` field accepting values `Bar` or `Tick`; THE Engine SHALL select the corresponding replay loop based on this field.
4. WHEN `ReplayMode` is `Tick`, THE DataHandler SHALL reject IDataProvider sources that do not supply tick-level data and return a descriptive configuration error.
5. THE Engine SHALL produce a `BacktestResult` with equivalent structure regardless of whether `ReplayMode` is `Bar` or `Tick`.

---

### Requirement 5: Strategy Interface

**User Story:** As a strategy developer, I want a well-defined Strategy interface, so that I can implement custom strategies that plug into the engine pipeline without modifying engine internals.

#### Acceptance Criteria

1. THE Engine SHALL define an `IStrategy` interface with a `OnMarketData(MarketDataEvent)` method that returns `IReadOnlyList<EngineEvent>` (zero or more `SignalEvent` or `OrderEvent` instances).
2. THE Engine SHALL invoke `IStrategy.OnMarketData` for every `MarketDataEvent` dequeued during the inner event-dispatch loop.
3. WHEN `IStrategy.OnMarketData` returns one or more `SignalEvent` instances, THE Engine SHALL enqueue each `SignalEvent` for downstream processing.
4. WHEN `IStrategy.OnMarketData` returns one or more `OrderEvent` instances directly, THE Engine SHALL route each `OrderEvent` through the RiskLayer before forwarding to the ExecutionHandler.
5. IF `IStrategy.OnMarketData` throws an unhandled exception, THEN THE Engine SHALL catch the exception, log it with the current heartbeat step and symbol, and halt the run with a `BacktestResult` marked `Status = Failed`.

---

### Requirement 6: Risk Layer (Mandatory Pipeline Component)

**User Story:** As a risk manager, I want a mandatory risk layer in the event pipeline, so that no order reaches the execution handler without passing position-sizing and exposure checks.

#### Acceptance Criteria

1. THE Engine SHALL route every `OrderEvent` — whether produced by Signal conversion or by direct Strategy generation — through the RiskLayer before the OrderEvent reaches the ExecutionHandler.
2. THE RiskLayer SHALL apply position sizing to each `OrderEvent` using a configurable sizing algorithm referenced in the ScenarioConfig.
3. WHILE the Portfolio's open exposure exceeds the `MaxExposurePercent` limit defined in the ScenarioConfig, THE RiskLayer SHALL reduce the order quantity to bring total exposure within the limit.
4. IF the RiskLayer reduces an order quantity to zero, THEN THE RiskLayer SHALL discard the `OrderEvent` and log a `RiskRejection` entry with the reason.
5. THE Engine SHALL not provide any code path that allows an `OrderEvent` to bypass the RiskLayer and reach the ExecutionHandler directly.
6. THE RiskLayer interface (IRiskLayer) SHALL be defined in Core so that alternative risk implementations can be substituted via dependency injection.

---

### Requirement 7: ExecutionHandler and Simulated Fills

**User Story:** As a backtester, I want a simulated execution handler, so that orders are converted to fills using realistic slippage and commission models during backtesting.

#### Acceptance Criteria

1. THE ExecutionHandler SHALL consume each `OrderEvent` from the EventQueue and produce a corresponding `FillEvent`.
2. THE ExecutionHandler SHALL apply the active ISlippageModel to compute the fill price before publishing the `FillEvent`.
3. THE ExecutionHandler SHALL apply the active ICommissionModel to compute the commission cost before publishing the `FillEvent`.
4. THE `FillEvent` SHALL carry `Symbol`, `Direction`, `Quantity`, `FillPrice`, `Commission`, `SlippageAmount`, and `Timestamp` fields, all non-nullable.
5. THE ISlippageModel interface SHALL be defined in Core and accept an `OrderEvent` and the current `MarketDataEvent` as inputs, returning a `decimal` price adjustment.
6. THE ICommissionModel interface SHALL be defined in Core and accept a `FillEvent` (pre-commission) as input, returning a `decimal` commission amount.
7. IF no ISlippageModel is registered in the DI container, THEN THE Engine SHALL use a `ZeroSlippageModel` that returns a zero price adjustment.
8. IF no ICommissionModel is registered in the DI container, THEN THE Engine SHALL use a `ZeroCommissionModel` that returns zero commission.

---

### Requirement 8: Portfolio State Tracking

**User Story:** As a backtester, I want the portfolio to track positions, cash, and equity in real time, so that I can evaluate strategy performance throughout and at the end of a simulation run.

#### Acceptance Criteria

1. THE Portfolio SHALL update its state by consuming each `FillEvent` published to the EventQueue.
2. THE Portfolio SHALL maintain a `Positions` dictionary keyed by `Symbol`, each entry holding `Quantity`, `AverageEntryPrice`, `UnrealisedPnl`, and `RealisedPnl`.
3. WHEN a `FillEvent` closes or reduces a position, THE Portfolio SHALL compute and record `RealisedPnl` for that trade.
4. THE Portfolio SHALL maintain a `CashBalance` that decreases by `(FillPrice × Quantity) + Commission` on a buy fill and increases by `(FillPrice × Quantity) − Commission` on a sell fill.
5. THE Portfolio SHALL compute `TotalEquity` as `CashBalance` plus the sum of `UnrealisedPnl` across all open positions after each `FillEvent`.
6. THE Portfolio SHALL append a timestamped `TotalEquity` snapshot to the EquityCurve after each `FillEvent`.
7. IF a `FillEvent` would reduce `CashBalance` below zero, THEN THE Portfolio SHALL record the fill, set `CashBalance` to zero, and log a `MarginBreachWarning`.

---

### Requirement 9: Abstracted Data Provider Interface

**User Story:** As an infrastructure developer, I want an abstracted IDataProvider interface, so that data sources can be swapped or extended without changing engine logic.

#### Acceptance Criteria

1. THE Engine SHALL define an `IDataProvider` interface in Core with methods `GetBars(symbol, interval, from, to)` returning `IAsyncEnumerable<BarRecord>` and `GetTicks(symbol, from, to)` returning `IAsyncEnumerable<TickRecord>`.
2. THE Infrastructure layer SHALL provide a `CsvDataProvider` that implements `IDataProvider` by reading bar or tick records from a local CSV file whose path is specified in the ScenarioConfig.
3. THE Infrastructure layer SHALL provide an `HttpRestDataProvider` stub that implements `IDataProvider` by issuing HTTP GET requests to a configurable base URL and deserialising JSON responses into `BarRecord` or `TickRecord` sequences.
4. WHEN the `CsvDataProvider` encounters a row with an unparseable field, THE CsvDataProvider SHALL skip that row, increment `MalformedRecordCount`, and continue.
5. WHEN the `HttpRestDataProvider` receives a non-2xx HTTP response, THE HttpRestDataProvider SHALL throw a `DataProviderException` with the status code and response body.
6. THE IDataProvider interface SHALL be designed so that named providers (Alpaca, Polygon, etc.) can be added in Infrastructure without modifying Core or Application.

---

### Requirement 10: BacktestResult and Performance Metrics

**User Story:** As a researcher, I want a structured BacktestResult with standard performance metrics, so that I can compare runs and feed results into research workflows.

#### Acceptance Criteria

1. THE Engine SHALL produce a `BacktestResult` record at the end of every simulation run containing: `RunId`, `ScenarioConfig`, `Status`, `EquityCurve`, `Trades` (list of closed trade records), `StartEquity`, `EndEquity`, `MaxDrawdown`, `SharpeRatio`, `SortinoRatio`, `TotalTrades`, `WinRate`, `ProfitFactor`, `AverageWin`, `AverageLoss`, and `RunDurationMs`.
2. THE Engine SHALL compute `MaxDrawdown` as the maximum peak-to-trough decline in `TotalEquity` expressed as a decimal fraction.
3. THE Engine SHALL compute `SharpeRatio` using the annualised excess return over the risk-free rate divided by the annualised standard deviation of returns; the risk-free rate SHALL be sourced from the ScenarioConfig field `AnnualRiskFreeRate`.
4. THE Engine SHALL compute `SortinoRatio` using the annualised excess return divided by the annualised downside deviation of returns.
5. WHEN `TotalTrades` is zero, THE Engine SHALL set `SharpeRatio`, `SortinoRatio`, `WinRate`, `ProfitFactor`, `AverageWin`, and `AverageLoss` to `null` in the `BacktestResult`.
6. THE `BacktestResult` SHALL be serialisable to and deserialisable from JSON without data loss (round-trip property).

---

### Requirement 11: Parameterised Scenarios and ScenarioConfig

**User Story:** As a researcher, I want all simulation parameters captured in a single ScenarioConfig file, so that runs are fully reproducible and shareable.

#### Acceptance Criteria

1. THE Engine SHALL accept a `ScenarioConfig` as the sole input required to initialise and execute a simulation run.
2. THE ScenarioConfig SHALL include fields for: `ScenarioId`, `Description`, `ReplayMode` (Bar/Tick), `DataProviderType`, `DataProviderOptions`, `StrategyType`, `StrategyParameters`, `RiskParameters`, `SlippageModelType`, `CommissionModelType`, `InitialCash`, `AnnualRiskFreeRate`, `ResearchWorkflowType` (optional), `ResearchWorkflowOptions` (optional), and `PropFirmOptions` (optional).
3. THE Application layer SHALL deserialise a `ScenarioConfig` from a JSON file whose path is supplied via the CLI `--scenario` argument or the API request body.
4. IF a required ScenarioConfig field is missing or invalid, THEN THE Application layer SHALL return a structured validation error listing each invalid field before starting any simulation.
5. THE ScenarioConfig SHALL support a `RandomSeed` field; WHEN `RandomSeed` is set, THE Engine SHALL initialise all random-number generators with that seed to enable deterministic replay.

---

### Requirement 12: Parameter Sweep

**User Story:** As a researcher, I want to run a parameter sweep across a grid of strategy and risk parameter values, so that I can identify which parameter combinations produce the best risk-adjusted returns.

#### Acceptance Criteria

1. THE Application layer SHALL provide a `ParameterSweepWorkflow` that accepts a base `ScenarioConfig` and a `ParameterGrid` (a dictionary mapping parameter names to lists of candidate values).
2. THE ParameterSweepWorkflow SHALL execute one Engine run per unique parameter combination in the Cartesian product of the ParameterGrid.
3. THE ParameterSweepWorkflow SHALL collect all `BacktestResult` instances and return a `SweepResult` containing the full result set and a ranked summary sorted by `SharpeRatio` descending.
4. WHEN the ParameterGrid contains zero entries, THE ParameterSweepWorkflow SHALL execute a single run using the base ScenarioConfig and return a `SweepResult` with one entry.
5. THE ParameterSweepWorkflow SHALL support parallel execution of independent runs; the degree of parallelism SHALL be configurable via `SweepOptions.MaxDegreeOfParallelism`.

---

### Requirement 13: Variance Testing (Conservative / Base / Strong Presets)

**User Story:** As a researcher, I want to run Conservative, Base, and Strong variance presets alongside user-defined variants, so that I can stress-test a strategy across a range of market assumptions.

#### Acceptance Criteria

1. THE Application layer SHALL define three named `VariancePreset` configurations — `Conservative`, `Base`, and `Strong` — each specifying a distinct set of overrides for slippage multiplier, commission multiplier, win-rate adjustment, and return-distribution parameters.
2. THE Application layer SHALL accept a user-defined `VariancePreset` supplied as a JSON object in the ScenarioConfig `ResearchWorkflowOptions`.
3. WHEN a `VariancePreset` is applied, THE ResearchWorkflow SHALL merge the preset overrides into the base ScenarioConfig before executing the Engine run, leaving all non-overridden fields unchanged.
4. THE Application layer SHALL produce a `VarianceResult` containing one `BacktestResult` per preset, labelled with the preset name.
5. WHEN all four variants (Conservative, Base, Strong, user-defined) are requested, THE Application layer SHALL execute all four and include all four in the `VarianceResult`.

---

### Requirement 14: Monte Carlo Simulation

**User Story:** As a researcher, I want a Monte Carlo simulation that reruns the engine with randomised trade-sequence perturbations, so that I can estimate the distribution of outcomes and ruin probability.

#### Acceptance Criteria

1. THE Application layer SHALL provide a `MonteCarloWorkflow` that accepts a `BacktestResult` (or a base ScenarioConfig) and a `MonteCarloOptions` object.
2. THE `MonteCarloOptions` SHALL include `SimulationCount` (default `MonteCarloDefaults.DefaultSimulationCount`, minimum 1), `Seed` (optional integer for reproducibility), and `RuinThresholdPercent` (the equity drawdown fraction at which a path is classified as ruin).
3. THE MonteCarloWorkflow SHALL execute `SimulationCount` simulation paths by randomly resampling the trade return sequence from the source `BacktestResult`.
4. THE MonteCarloWorkflow SHALL compute and return a `MonteCarloResult` containing: `P10EndEquity`, `P50EndEquity`, `P90EndEquity`, `RuinProbability`, `MedianMaxDrawdown`, and the full distribution of `EndEquity` values.
5. WHEN `Seed` is set in `MonteCarloOptions`, THE MonteCarloWorkflow SHALL produce identical `MonteCarloResult` values across repeated calls with the same inputs.
6. WHEN `SimulationCount` is less than 1, THE MonteCarloWorkflow SHALL return a validation error without executing any simulation paths.

---

### Requirement 15: Walk-Forward Analysis

**User Story:** As a researcher, I want walk-forward analysis as a first-class workflow, so that I can validate that a strategy's in-sample parameters generalise to out-of-sample data.

#### Acceptance Criteria

1. THE Application layer SHALL provide a `WalkForwardWorkflow` that accepts a `ScenarioConfig` and a `WalkForwardOptions` object.
2. THE `WalkForwardOptions` SHALL include `InSampleLength` (timespan or bar count), `OutOfSampleLength` (timespan or bar count), `StepSize` (timespan or bar count), and `AnchoredWindow` (boolean; when true, the in-sample window always starts at the data origin).
3. THE WalkForwardWorkflow SHALL partition the full data range into sequential windows according to `WalkForwardOptions`, each window having a defined in-sample segment and an out-of-sample segment.
4. FOR EACH window, THE WalkForwardWorkflow SHALL run a ParameterSweepWorkflow on the in-sample segment to select the best parameters, then run the Engine on the out-of-sample segment using those parameters.
5. THE WalkForwardWorkflow SHALL return a `WalkForwardResult` containing one `WalkForwardWindow` record per window, each carrying `InSampleResult`, `OutOfSampleResult`, `SelectedParameters`, and `WindowIndex`.
6. WHEN the data range is too short to form at least one complete window, THE WalkForwardWorkflow SHALL return a validation error describing the minimum required data length.

---

### Requirement 16: Cross-Scenario Comparison

**User Story:** As a researcher, I want to compare multiple BacktestResults side by side, so that I can select the best-performing scenario configuration.

#### Acceptance Criteria

1. THE Application layer SHALL provide a `ScenarioComparison` use case that accepts a list of two or more `BacktestResult` instances and returns a `ComparisonReport`.
2. THE `ComparisonReport` SHALL include, for each result: `ScenarioId`, `SharpeRatio`, `SortinoRatio`, `MaxDrawdown`, `WinRate`, `ProfitFactor`, `TotalTrades`, and `EndEquity`.
3. THE `ComparisonReport` SHALL identify the result with the highest `SharpeRatio` as `BestBySharpe` and the result with the lowest `MaxDrawdown` as `BestByDrawdown`.
4. WHEN fewer than two `BacktestResult` instances are supplied, THE ScenarioComparison use case SHALL return a validation error.
5. THE `ComparisonReport` SHALL be serialisable to JSON and renderable as a Markdown table by the Reporter component.

---

### Requirement 17: Markdown and Console Reporting

**User Story:** As a user, I want simulation results rendered as both console output and Markdown reports, so that I can review results in the terminal and share them as documents.

#### Acceptance Criteria

1. THE Application layer SHALL define an `IReporter` interface with methods `RenderToConsole(BacktestResult)`, `RenderToMarkdown(BacktestResult)`, `RenderToConsole(ComparisonReport)`, and `RenderToMarkdown(ComparisonReport)`.
2. THE Infrastructure layer SHALL provide a `ConsoleReporter` that writes formatted output to standard output using the `IReporter` interface.
3. THE Infrastructure layer SHALL provide a `MarkdownReporter` that writes a Markdown file to a path specified in the ScenarioConfig or CLI argument.
4. WHEN `RenderToMarkdown` is called, THE MarkdownReporter SHALL produce a file containing: scenario metadata, performance metrics table, equity curve summary, and (if present) research workflow results.
5. THE Reporter SHALL render all monetary values in USD with two decimal places and all percentage values with two decimal places.
6. WHEN a `MonteCarloResult` is present in the `BacktestResult`, THE Reporter SHALL include a P10/P50/P90 equity table and ruin probability in the rendered output.

---

## Prop-Firm Evaluation Suite (Bounded Module)

> All requirements in this section belong to the `PropFirmModule`. They consume `BacktestResult` and `ResearchWorkflow` outputs. They do not modify Core engine abstractions.

---

### Requirement 18: Challenge Account Modelling

**User Story:** As a prop-firm researcher, I want to model the pass-rate and pass-to-first-payout conversion for a challenge account, so that I can estimate the probability of reaching a funded account.

#### Acceptance Criteria

1. THE PropFirmModule SHALL accept a `ChallengeConfig` containing `PassRatePercent`, `PassToFundedConversionPercent`, `AccountFeeUsd`, `NotionalSizeUsd`, `MaxDailyDrawdownPercent`, `MaxTotalDrawdownPercent`, and a reference to a `FirmRuleSet`.
2. THE PropFirmModule SHALL compute `ChallengeProbability` as `PassRatePercent × PassToFundedConversionPercent / 10000` (both inputs expressed as percentages).
3. WHEN a `BacktestResult` is supplied, THE PropFirmModule SHALL evaluate the result against the `FirmRuleSet` drawdown and consistency rules and return a `RuleEvaluationReport` indicating pass or fail for each rule.
4. IF the `BacktestResult` violates any rule in the `FirmRuleSet`, THEN THE PropFirmModule SHALL mark `ChallengeOutcome` as `Failed` and list each violated rule in the `RuleEvaluationReport`.
5. THE PropFirmModule SHALL operate in USD only; IF a `BacktestResult` carries a non-USD currency marker, THEN THE PropFirmModule SHALL return a validation error.

---

### Requirement 19: Instant Funding Modelling

**User Story:** As a prop-firm researcher, I want to model instant-funding accounts, so that I can compare the economics of challenge-based and direct-funding paths.

#### Acceptance Criteria

1. THE PropFirmModule SHALL accept an `InstantFundingConfig` containing `DirectFundedProbabilityPercent`, `AccountFeeUsd`, `NotionalSizeUsd`, `GrossMonthlyReturnPercent`, `PayoutSplitPercent`, `PayoutFrictionFactor`, and a reference to a `FirmRuleSet`.
2. THE PropFirmModule SHALL compute `InstantFundingFirstPayoutProbability` as `DirectFundedProbabilityPercent / 100`.
3. WHEN an `InstantFundingConfig` is provided, THE PropFirmModule SHALL compute `MonthlyPayoutExpectancy` as `NotionalSizeUsd × (GrossMonthlyReturnPercent / 100) × (PayoutSplitPercent / 100) × PayoutFrictionFactor`.
4. THE PropFirmModule SHALL compute `LifetimeEV` as `(MonthlyPayoutExpectancy × ExpectedPayoutMonths) − AccountFeeUsd`.
5. THE PropFirmModule SHALL compute `BreakevenMonths` as the smallest integer M such that `MonthlyPayoutExpectancy × M ≥ AccountFeeUsd`; WHEN `MonthlyPayoutExpectancy` is zero or negative, THE PropFirmModule SHALL set `BreakevenMonths` to `null` and log a warning.

---

### Requirement 20: Firm Rule Set (JSON-Configurable)

**User Story:** As a prop-firm researcher, I want to configure firm-specific rules via JSON, so that I can model any prop firm's constraints without code changes.

#### Acceptance Criteria

1. THE PropFirmModule SHALL define a `FirmRuleSet` record containing: `FirmName`, `MaxDailyDrawdownPercent`, `MaxTotalDrawdownPercent`, `MinTradingDays`, `PayoutCapUsd` (nullable), `ConsistencyRulePercent` (nullable; the maximum fraction of total profit that may come from a single day), and `CustomRules` (list of named string-valued rule descriptors for documentation purposes).
2. THE Infrastructure layer SHALL deserialise a `FirmRuleSet` from a JSON file whose path is specified in the `PropFirmOptions` section of the ScenarioConfig.
3. WHEN a `FirmRuleSet` JSON file is missing a required field, THE Infrastructure layer SHALL return a structured validation error listing each missing field.
4. THE PropFirmModule SHALL evaluate `MinTradingDays` by counting distinct calendar days on which at least one trade was opened in the `BacktestResult`.
5. WHEN `ConsistencyRulePercent` is set, THE PropFirmModule SHALL compute the fraction of total profit attributable to the single best trading day and flag a rule violation IF that fraction exceeds `ConsistencyRulePercent / 100`.

---

### Requirement 21: Prop-Firm Variance Presets

**User Story:** As a prop-firm researcher, I want Conservative, Base, and Strong variance presets for prop-firm scenarios, so that I can stress-test firm economics under different market assumptions.

#### Acceptance Criteria

1. THE PropFirmModule SHALL support the same three named `VariancePreset` configurations (Conservative, Base, Strong) defined in Requirement 13, applied to `GrossMonthlyReturnPercent`, `PayoutFrictionFactor`, and `PassRatePercent`.
2. THE PropFirmModule SHALL accept a user-defined `VariancePreset` supplied as a JSON object in `PropFirmOptions`.
3. WHEN a `VariancePreset` is applied, THE PropFirmModule SHALL recompute `MonthlyPayoutExpectancy`, `LifetimeEV`, and `BreakevenMonths` using the overridden values.
4. THE PropFirmModule SHALL return a `PropFirmVarianceResult` containing one `PropFirmScenarioResult` per preset, labelled with the preset name.
5. THE PropFirmModule SHALL operate on a single account per scenario in V1; multi-account aggregation is explicitly out of scope for V1.

---

## Delivery and Integration Requirements

---

### Requirement 22: CLI Host

**User Story:** As a user, I want a CLI that supports both file-driven and interactive modes, so that I can run scenarios from scripts or explore them interactively.

#### Acceptance Criteria

1. THE CLI SHALL accept a `--scenario <path>` argument; WHEN supplied, THE CLI SHALL load the ScenarioConfig from the specified JSON file and execute the corresponding workflow without prompting.
2. WHEN no arguments are supplied, THE CLI SHALL enter interactive prompt mode, guiding the user through scenario selection and parameter entry via console prompts.
3. WHEN a simulation run completes, THE CLI SHALL print a summary of the `BacktestResult` to standard output and, IF `--output <path>` is supplied, write the full Markdown report to that path.
4. IF the `--scenario` file does not exist, THEN THE CLI SHALL print a descriptive error message to standard error and exit with a non-zero exit code.
5. THE CLI SHALL support a `--help` flag that prints usage instructions and exits with exit code zero.

---

### Requirement 23: ASP.NET Core Minimal API Host

**User Story:** As an API consumer, I want a minimal REST API, so that I can trigger simulations and retrieve results programmatically.

#### Acceptance Criteria

1. THE API SHALL expose a `POST /scenarios/run` endpoint that accepts a `ScenarioConfig` JSON body and returns a `BacktestResult` JSON response.
2. THE API SHALL expose a `POST /scenarios/sweep` endpoint that accepts a `ParameterSweepRequest` JSON body and returns a `SweepResult` JSON response.
3. THE API SHALL expose a `POST /scenarios/montecarlo` endpoint that accepts a `MonteCarloRequest` JSON body and returns a `MonteCarloResult` JSON response.
4. THE API SHALL expose a `POST /scenarios/walkforward` endpoint that accepts a `WalkForwardRequest` JSON body and returns a `WalkForwardResult` JSON response.
5. WHEN a request body fails validation, THE API SHALL return HTTP 400 with a JSON body listing each validation error.
6. WHEN an internal error occurs during a simulation run, THE API SHALL return HTTP 500 with a JSON body containing a `CorrelationId` and a non-sensitive error message; the full exception SHALL be logged server-side.
7. THE API architecture SHALL explicitly account for a future Blazor UI by exposing all result types as JSON-serialisable records and by supporting CORS configuration via `IOptions<CorsOptions>`.

---

### Requirement 24: JSON Persistence and Repository Abstraction

**User Story:** As a developer, I want an abstracted repository interface backed by JSON files in V1, so that persistence can be swapped for a database without changing application logic.

#### Acceptance Criteria

1. THE Application layer SHALL define an `IRepository<T>` interface with `SaveAsync(T entity)`, `GetByIdAsync(string id)`, `ListAsync()`, and `DeleteAsync(string id)` methods.
2. THE Infrastructure layer SHALL provide a `JsonFileRepository<T>` that implements `IRepository<T>` by serialising entities to and deserialising them from JSON files in a configurable directory.
3. WHEN `SaveAsync` is called, THE JsonFileRepository SHALL write the entity to a file named `{id}.json` in the configured directory, overwriting any existing file with the same name.
4. WHEN `GetByIdAsync` is called with an id that has no corresponding file, THE JsonFileRepository SHALL return `null`.
5. THE `IRepository<T>` interface SHALL be designed so that a SQL or NoSQL database adapter can be substituted via DI registration without modifying any Application-layer code.

---

### Requirement 25: Solution Structure and Clean Architecture

**User Story:** As a developer, I want the solution to follow clean architecture layer boundaries, so that domain logic remains independent of infrastructure and delivery concerns.

#### Acceptance Criteria

1. THE solution SHALL contain exactly the following projects: `TradingResearchEngine.Core`, `TradingResearchEngine.Application`, `TradingResearchEngine.Infrastructure`, `TradingResearchEngine.Cli`, `TradingResearchEngine.Api`, `TradingResearchEngine.UnitTests`, and `TradingResearchEngine.IntegrationTests`.
2. THE Core project SHALL have no dependencies on Application, Infrastructure, Cli, or Api projects.
3. THE Application project SHALL depend only on Core; it SHALL NOT reference Infrastructure, Cli, or Api.
4. THE Infrastructure project SHALL depend on Core and Application; it SHALL NOT reference Cli or Api.
5. THE Cli and Api projects SHALL depend on Application and Infrastructure for DI wiring only; all business logic SHALL reside in Application or Core.
6. THE solution SHALL target .NET 8 with C# 12 language features, nullable reference types enabled, and implicit usings enabled across all projects.

---

### Requirement 26: Configuration and Dependency Injection

**User Story:** As a developer, I want all configuration bound via IOptions<T> and all dependencies registered via the built-in DI container, so that the system is testable and configurable without code changes.

#### Acceptance Criteria

1. THE Application layer SHALL expose an `AddTradingResearchEngine(IServiceCollection, IConfiguration)` extension method that registers all Core and Application services.
2. THE Infrastructure layer SHALL expose an `AddTradingResearchEngineInfrastructure(IServiceCollection, IConfiguration)` extension method that registers all Infrastructure services.
3. THE Engine SHALL bind all configurable thresholds and defaults (including `MonteCarloDefaults.DefaultSimulationCount`, `RiskDefaults.MaxExposurePercent`, and `ReportingDefaults.DecimalPlaces`) to named `IOptions<T>` configuration classes.
4. WHEN a required service is not registered in the DI container, THE Engine SHALL throw an `InvalidOperationException` at startup with a message identifying the missing service type.
5. THE solution SHALL support `appsettings.json` and environment-variable configuration sources via the standard `IConfiguration` pipeline.

---

### Requirement 27: Testing Standards

**User Story:** As a developer, I want a well-structured test suite, so that engine correctness and regression safety are maintained as the codebase evolves.

#### Acceptance Criteria

1. THE UnitTests project SHALL use xUnit as the test framework and cover all Core domain logic and Application use cases.
2. THE IntegrationTests project SHALL use xUnit and test end-to-end flows including CSV data loading, full simulation runs, and API endpoint responses.
3. THE UnitTests project SHALL include at least one property-based test for the `BacktestResult` JSON round-trip (serialise then deserialise produces an equivalent object).
4. THE UnitTests project SHALL include at least one property-based test verifying that the EquityCurve length equals the number of `FillEvent` instances processed during a simulation run.
5. THE test projects SHALL not reference Infrastructure directly from UnitTests; all Infrastructure dependencies SHALL be replaced with in-memory test doubles or mocks.

---

### Requirement 28: Documentation and Steering Files

**User Story:** As a developer, I want steering files and documentation committed alongside the code, so that architectural decisions and domain boundaries are discoverable and enforced.

#### Acceptance Criteria

1. THE solution SHALL include the following steering files under `.kiro/steering/`: `product.md`, `tech.md`, `structure.md`, `testing-standards.md`, `api-standards.md`, `security-policies.md`, and `domain-boundaries.md`.
2. THE solution SHALL include the following documentation files under `docs/`: `BacktestingEngineOriginalNotes.md`, `BacktestingEngineImplementationNotes.md`, `EventDrivenArchitectureNotes.md`, and `PropFirmSuiteReference.md`.
3. THE solution SHALL include a `README.md` at the repository root containing: architecture overview, module boundaries, product goals, next-step roadmap, and future AWS deployment options (App Runner, ECS, Lambda, S3+Blazor, API Gateway).
4. THE solution SHALL include `.kiro/hooks/` automation hooks for: documentation updates, test synchronisation, architecture boundary checks, complexity checks, domain logic test validation, event-type documentation, and strategy fixture validation.
5. ALL public types and members in `TradingResearchEngine.Core` and `TradingResearchEngine.Application` SHALL carry XML doc comments.
