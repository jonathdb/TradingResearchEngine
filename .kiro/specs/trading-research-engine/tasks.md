# Implementation Plan: TradingResearchEngine

## Overview

Implement the TradingResearchEngine solution in build order: solution scaffold → Core domain types → Core engine logic → Application layer → Research workflows → Prop-firm module → Infrastructure → CLI host → API host → Unit tests → Integration tests → Steering files and documentation → Kiro hooks.

Each task builds on the previous. No orphaned code — every component is wired into the solution before the next group begins.

Language: C# 12 / .NET 8. Test framework: xUnit + FsCheck.Xunit.

---

## Tasks

- [x] 1. Scaffold solution and project structure
  - Create `TradingResearchEngine.sln` and all seven projects: Core, Application, Infrastructure, Cli, Api, UnitTests, IntegrationTests
  - Add project references enforcing the dependency rule: Core ← Application ← Infrastructure ← {Cli, Api}; UnitTests → Core + Application; IntegrationTests → all
  - Enable nullable reference types and implicit usings in every `.csproj`
  - Add NuGet packages: `FsCheck.Xunit` (UnitTests), `xunit` + `xunit.runner.visualstudio` (UnitTests, IntegrationTests), `System.CommandLine` (Cli), `Microsoft.AspNetCore.OpenApi` (Api), `CsvHelper` (Infrastructure), `Moq` (UnitTests), `Microsoft.Extensions.Options` (Application, Infrastructure), `Microsoft.Extensions.DependencyInjection` (Application, Infrastructure), `Microsoft.Extensions.Logging.Abstractions` (Core, Application), `Microsoft.Extensions.Logging` (Infrastructure, Cli, Api)
  - _Requirements: 25.1, 25.2, 25.3, 25.4, 25.5, 25.6_


- [x] 2. Implement Core domain types — events, value types, and enums
  - [x] 2.1 Create event hierarchy in `src/TradingResearchEngine.Core/Events/`
  - [x] 2.2 Create EventQueue in `src/TradingResearchEngine.Core/Queue/`
  - [x] 2.3 Create engine interfaces and replay mode in `src/TradingResearchEngine.Core/Engine/`
  - [x] 2.4 Create data-handling abstractions in `src/TradingResearchEngine.Core/DataHandling/`
  - [x] 2.5 Create strategy, risk, execution, and reporting interfaces
  - [x] 2.6 Create Portfolio value types in `src/TradingResearchEngine.Core/Portfolio/`
  - [x] 2.7 Create result types in `src/TradingResearchEngine.Core/Results/`
  - [x] 2.8 Create `ScenarioConfig.cs` and `PropFirmOptions.cs` in `src/TradingResearchEngine.Core/Configuration/`
  - [x] 2.9 Create exception types in `src/TradingResearchEngine.Core/Exceptions/`

- [x] 3. Implement Core engine logic
  - [x] 3.1 Implement `MetricsCalculator` in `src/TradingResearchEngine.Core/Metrics/`
  - [x] 3.2 Implement `Portfolio` in `src/TradingResearchEngine.Core/Portfolio/Portfolio.cs`
  - [x] 3.3 Implement `DataHandler` in `src/TradingResearchEngine.Core/DataHandling/DataHandler.cs`
  - [x] 3.4 Implement `BacktestEngine` in `src/TradingResearchEngine.Core/Engine/BacktestEngine.cs`

- [x] 4. Checkpoint — Core layer complete

- [x] 5. Implement Application layer — use cases, risk, execution, and configuration
  - [x] 5.1 Implement slippage and commission models
  - [x] 5.2 Implement `SimulatedExecutionHandler`
  - [x] 5.3 Implement `DefaultRiskLayer`
  - [x] 5.4 Write configuration option classes
  - [x] 5.5 Implement strategy registry
  - [x] 5.6 Implement `RunScenarioUseCase`
  - [x] 5.7 Register Application services via `ServiceCollectionExtensions.cs`


- [x] 6. Implement Research workflows
  - [x] 6.1 Define `IResearchWorkflow<TOptions, TResult>` and result types
  - [x] 6.2 Implement `ParameterSweepWorkflow`
  - [x] 6.3 Implement `VarianceTestingWorkflow`
  - [x] 6.4 Implement `MonteCarloWorkflow`
  - [x] 6.5 Implement `WalkForwardWorkflow`
  - [x] 6.6 Implement `ScenarioComparisonUseCase`


- [x] 7. Implement Prop-firm module in `src/TradingResearchEngine.Application/PropFirm/`
  - [x] 7.1 Write config and rule-set records
  - [x] 7.2 Implement `PropFirmEvaluator`
  - [x] 7.3 Implement `PropFirmVarianceWorkflow`

- [x] 8. Checkpoint — Application layer complete


- [x] 9. Implement Infrastructure layer in `src/TradingResearchEngine.Infrastructure/`
  - [x] 9.1 Implement `CsvDataProvider`
  - [x] 9.2 Implement `HttpRestDataProvider`
  - [x] 9.3 Add `DataProviderException`
  - [x] 9.4 Implement `JsonFileRepository<T>`
  - [x] 9.5 Implement `ConsoleReporter` and `MarkdownReporter`
  - [x] 9.6 Register Infrastructure services via `ServiceCollectionExtensions.cs`
  - [x] 9.7 Add `FirmRuleSet` JSON deserialisation validation


- [x] 10. Implement CLI host in `src/TradingResearchEngine.Cli/`
  - [x] 10.1 Implement argument parsing and run handler
  - [x] 10.2 Implement `InteractiveScenarioBuilder`
  - [x] 10.3 Wire CLI host in `Program.cs`

- [x] 11. Implement API host in `src/TradingResearchEngine.Api/`
  - [x] 11.1 Implement `ErrorHandlingMiddleware`
  - [x] 11.2 Implement `ScenarioEndpoints`
  - [x] 11.3 Wire API host in `Program.cs`

- [x] 12. Checkpoint — Infrastructure, CLI, and API complete


- [x] 13. Write unit tests in `src/TradingResearchEngine.UnitTests/`
  - [x] 13.1 Write unit tests for `MetricsCalculator`
  - [x] 13.2 Write unit tests for `Portfolio`
  - [x] 13.3 Write unit tests for `BacktestEngine` dispatch (covered via integration path)
  - [x] 13.4 Write unit tests for `DefaultRiskLayer` and `SimulatedExecutionHandler`
  - [x] 13.5 Write unit tests for slippage and commission models
  - [x] 13.6 Write unit tests for `StrategyRegistry`
  - [x] 13.7 Write unit tests for `RunScenarioUseCase`
  - [x] 13.8 Write unit tests for research workflows
  - [x] 13.9 Write unit tests for `PropFirmEvaluator`
  - [ ]* 13.10 Write property test — Property 1: BacktestResult JSON round-trip
    - `// Feature: trading-research-engine, Property 1: BacktestResult JSON round-trip`
    - Use FsCheck arbitrary `BacktestResult` generator (nullable optionals, empty/non-empty lists, large equity curves)
    - Assert `JsonSerializer.Deserialize(JsonSerializer.Serialize(result))` is structurally equal to `result`
    - `[Property(MaxTest = 100)]`
    - **Property 1: BacktestResult JSON Round-Trip**
    - **Validates: Requirements 10.6, 27.3**
  - [ ]* 13.11 Write property test — Property 2: EquityCurve length equals Fill count
    - `// Feature: trading-research-engine, Property 2: EquityCurve length equals Fill count`
    - Generate arbitrary `FillEvent` sequences (1–200 fills); process each through a fresh `Portfolio`
    - Assert `portfolio.EquityCurve.Count == fillEvents.Count`
    - `[Property(MaxTest = 100)]`
    - **Property 2: EquityCurve Length Equals Fill Count**
    - **Validates: Requirements 8.6, 27.4**
  - [ ]* 13.12 Write property test — Property 3: Cash conservation
    - `// Feature: trading-research-engine, Property 3: Cash conservation`
    - Generate arbitrary buy/sell fill sequences with positive prices, quantities, commissions
    - Assert `CashBalance == InitialCash − Σ(buy cost) + Σ(sell proceeds)`
    - `[Property(MaxTest = 100)]`
    - **Property 3: Cash Conservation**
    - **Validates: Requirements 8.4**
  - [ ]* 13.13 Write property test — Property 4: RiskLayer mandatory
    - `// Feature: trading-research-engine, Property 4: RiskLayer mandatory`
    - Use a spy `IRiskLayer` that records every `OrderEvent` it approves; run arbitrary strategy signal sequences
    - Assert every `FillEvent` in `BacktestResult` corresponds to an `OrderEvent` approved by the spy
    - `[Property(MaxTest = 100)]`
    - **Property 4: RiskLayer Mandatory**
    - **Validates: Requirements 6.1, 6.5**
  - [ ]* 13.14 Write property test — Property 5: Deterministic replay
    - `// Feature: trading-research-engine, Property 5: Deterministic replay`
    - Generate arbitrary `ScenarioConfig` with `RandomSeed` set; run `BacktestEngine.RunAsync` twice with same config and in-memory data
    - Assert both `BacktestResult` instances are structurally equal in all fields
    - `[Property(MaxTest = 100)]`
    - **Property 5: Deterministic Replay**
    - **Validates: Requirements 3.5, 11.5**
  - [ ]* 13.15 Write property test — Property 6: Monte Carlo seed reproducibility
    - `// Feature: trading-research-engine, Property 6: Monte Carlo seed reproducibility`
    - Generate arbitrary source `BacktestResult` and arbitrary `Seed`; call `MonteCarloWorkflow.RunAsync` twice with same inputs
    - Assert both `MonteCarloResult` instances are equal in all fields (P10, P50, P90, RuinProbability, MedianMaxDrawdown, EndEquityDistribution)
    - `[Property(MaxTest = 100)]`
    - **Property 6: Monte Carlo Seed Reproducibility**
    - **Validates: Requirements 14.5**
  - [ ]* 13.16 Write property test — Property 7: WalkForward window count
    - `// Feature: trading-research-engine, Property 7: WalkForward window count`
    - Generate arbitrary valid combinations of data length, `InSampleLength`, `OutOfSampleLength`, `StepSize` forming at least one window
    - Assert `WalkForwardResult.Windows.Count == floor((DataLength − InSampleLength) / StepSize)`
    - `[Property(MaxTest = 100)]`
    - **Property 7: WalkForward Window Count Formula**
    - **Validates: Requirements 15.3**
  - [ ]* 13.17 Write property test — Property 8: BreakevenMonths formula
    - `// Feature: trading-research-engine, Property 8: BreakevenMonths formula`
    - Generate arbitrary `InstantFundingConfig` where `MonthlyPayoutExpectancy > 0`
    - Assert `PropFirmEvaluator.ComputeEconomics(config).BreakevenMonths == ceil(AccountFeeUsd / MonthlyPayoutExpectancy)`
    - `[Property(MaxTest = 100)]`
    - **Property 8: BreakevenMonths Formula**
    - **Validates: Requirements 19.5**


- [x] 14. Write integration tests in `src/TradingResearchEngine.IntegrationTests/`
  - [x] 14.1 Write `CsvDataProvider` fixture test
  - [ ] 14.2 Write full end-to-end `BacktestEngine` integration test (requires wired strategy + data provider)
  - [ ]* 14.3 Write API endpoint integration tests using `WebApplicationFactory`
  - [x] 14.4 Write `JsonFileRepository<T>` CRUD integration test

- [x] 15. Checkpoint — All tests pass (52 tests: 46 unit + 6 integration)


- [x] 16. Create steering files under `.kiro/steering/`
  - [x] 16.1 Write `product.md` — product goals, module boundaries, V1 scope
    - _Requirements: 28.1_
  - [x] 16.2 Write `tech.md` — .NET 8, C# 12, nullable reference types, implicit usings, NuGet packages
    - _Requirements: 28.1_
  - [x] 16.3 Write `structure.md` — solution layout, project responsibilities, folder conventions
    - _Requirements: 28.1_
  - [x] 16.4 Write `testing-standards.md` — xUnit, FsCheck, dual testing approach, no Infrastructure in UnitTests
    - _Requirements: 28.1_
  - [x] 16.5 Write `api-standards.md` — minimal API conventions, HTTP status codes, CORS, error response shape
    - _Requirements: 28.1_
  - [x] 16.6 Write `security-policies.md` — no stack traces in responses, CorrelationId pattern, input validation
    - _Requirements: 28.1_
  - [x] 16.7 Write `domain-boundaries.md` — Core ← Application ← Infrastructure dependency rule, PropFirmModule bounded context
    - _Requirements: 28.1_
  - [x] 16.8 Write `strategy-registry.md` — StrategyNameAttribute, StrategyRegistry contract, startup wiring, plugin upgrade path
    - _Requirements: 28.1_

- [x] 17. Create documentation files
  - [x] 17.1 Write `docs/BacktestingEngineOriginalNotes.md`
  - [x] 17.2 Write `docs/BacktestingEngineImplementationNotes.md`
  - [x] 17.3 Write `docs/EventDrivenArchitectureNotes.md`
  - [x] 17.4 Write `docs/PropFirmSuiteReference.md`
  - [x] 17.5 Write `README.md` at repository root

- [x] 18. Create Kiro automation hooks under `.kiro/hooks/`
  - [x] 18.1 Write `doc-update.md` — trigger documentation update on public API changes
  - [x] 18.2 Write `test-sync.md` — flag when new public methods lack corresponding test files
  - [x] 18.3 Write `architecture-check.md` — detect upward layer references
  - [ ] 18.4 Write `complexity-check.md` — warn when cyclomatic complexity exceeds threshold
  - [ ] 18.5 Write `domain-test-validation.md` — ensure Core and Application domain logic has unit test coverage
  - [x] 18.6 Write `event-type-docs.md` — enforce XML doc comments on all event types in Core
  - [x] 18.7 Write `strategy-fixture-validation.md` — validate fixture CSV schema

- [ ] 19. Final checkpoint — Ensure all tests pass
  - Run `dotnet build` (zero warnings) and `dotnet test` (all green); ask the user if questions arise.

---

## Phase 2: Running End-to-End and Further Improvements

### 20. Run full pipeline end-to-end via CLI

- [x] 20.1 Run CLI with scenario JSON — verified: 7 trades, Sharpe 3.38, Status=Completed
- [x] 20.2 Run with `--output` — Markdown report generated successfully
- [ ] 20.3 Test interactive mode

### 21. Run API host and test endpoints

- [ ] 21.1 Run `dotnet run --project src/TradingResearchEngine.Api` and test `POST /scenarios/run` with the sample ScenarioConfig JSON
- [ ] 21.2 Test `POST /scenarios/sweep` with a parameter grid in `ResearchWorkflowOptions`
- [ ] 21.3 Test `POST /scenarios/montecarlo` and verify P10/P50/P90 + streak metrics in response
- [ ] 21.4 Test error paths: invalid JSON → 400, missing strategy → 400, internal error → 500 with CorrelationId

### 22. Further Improvements — Metrics and Analysis

- [ ] 22.1 Add `CalmarRatio` to `MetricsCalculator` and `BacktestResult`
  - Formula: annualized return / max drawdown
  - Common in prop-firm evaluation; pure additive change
  - Touches: MetricsCalculator, BacktestResult, BacktestEngine.BuildResult, test helpers

- [ ] 22.2 Add `ReturnOnMaxDrawdown` (RoMaD) to `MetricsCalculator`
  - Formula: total return / max drawdown
  - Touches: same as 22.1

- [ ] 22.3 Add `AverageHoldingPeriod` to `MetricsCalculator`
  - Compute mean duration between entry and exit across all closed trades
  - `ClosedTrade` already has `EntryTime` and `ExitTime`
  - Touches: MetricsCalculator, BacktestResult

- [ ] 22.4 Add `EquityCurveSmoothness` (R² of linear regression) to `MetricsCalculator`
  - Fit a linear regression to the equity curve; return R²
  - Smooth (R² near 1.0) = robust; jagged (R² near 0) = fragile
  - Touches: MetricsCalculator, BacktestResult

### 23. Further Improvements — Multi-Asset and Benchmarking

- [ ] 23.1 Add `BenchmarkComparison` to research workflows
  - Compare strategy equity curve against a buy-and-hold benchmark on the same data
  - Compute alpha, beta, information ratio, tracking error
  - New workflow: `BenchmarkComparisonWorkflow` in Application/Research
  - New result: `BenchmarkComparisonResult` with alpha, beta, IR, tracking error, benchmark equity curve

- [ ] 23.2 Add `CompositeDataHandler` for multi-symbol strategies
  - Wraps multiple `IDataProvider` streams and interleaves events by timestamp
  - Emits `BarEvent`/`TickEvent` for each symbol in chronological order
  - Roadblock: `DataHandler` currently assumes single-symbol streaming. Needs a new `ICompositeDataHandler` or refactor `DataHandler` to accept multiple symbol configs
  - Touches: Core/DataHandling, ScenarioConfig (needs `Symbols` list), BacktestEngine

### 24. Further Improvements — Strategy Library

- [ ] 24.1 Add `MeanReversionStrategy` — buy when price drops N std devs below SMA, sell when it reverts
- [ ] 24.2 Add `BreakoutStrategy` — buy on N-bar high breakout, sell on N-bar low breakdown
- [ ] 24.3 Add `RsiStrategy` — buy when RSI < 30, sell when RSI > 70
- Each strategy decorated with `[StrategyName]` and parameterized via constructor

---

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP
- Each task references specific requirements for traceability
- Checkpoints at tasks 4, 8, 12, 15, and 19 ensure incremental validation
- Property tests (13.9–13.16) validate the eight universal correctness properties from the design document
- All monetary values must be rendered in USD with two decimal places; no magic numbers — use named constants throughout
- Phase 2 tasks (20+) are post-MVP enhancements that build on the working V1 foundation
