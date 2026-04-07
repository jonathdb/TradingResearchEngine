# Domain Boundaries

## Dependency Rule (enforced)

```
Core ← Application ← Infrastructure ← { Cli, Api }
```

No upward references. No circular references. Violations are caught by the `architecture-check` hook.

## Core

- Owns: event hierarchy, `IEventQueue`, `EventQueue`, `IBacktestEngine`, `BacktestEngine`,
  `DataHandler`, `IDataProvider`, `IStrategy`, `IRiskLayer`, `IExecutionHandler`,
  `ISlippageModel`, `ICommissionModel`, `Portfolio`, `MetricsCalculator`,
  `BacktestResult`, `ScenarioConfig`, `PropFirmOptions`, `IReporter`, `IRepository<T>`, `IHasId`,
  `ConfigurationException` (in `Exceptions/`)
- Must not reference Application, Infrastructure, Cli, or Api
- Must not contain concrete I/O, HTTP, file system, or DI registration code
- All public types carry XML doc comments

## Application

- Owns: use cases (`RunScenarioUseCase`), research workflows, `DefaultRiskLayer`,
  `SimulatedExecutionHandler`, slippage/commission models, option classes,
  `PropFirmModule`, `ServiceCollectionExtensions`, `StrategyNotFoundException` (in `Strategy/`)
- References Core only
- Must not contain file I/O, HTTP clients, CSV parsing, or console output
- No direct `new` of Infrastructure types — depend on Core interfaces

## Infrastructure

- Owns: `CsvDataProvider`, `HttpRestDataProvider`, `JsonFileRepository<T>`,
  `ConsoleReporter`, `MarkdownReporter`, `ServiceCollectionExtensions`,
  `DataProviderException` (in `Exceptions/`)
- Implements interfaces defined in Core
- Contains no domain logic — mapping, I/O, and serialisation only
- `HttpRestDataProvider` is designed for subclassing; URL construction and response
  mapping are `protected virtual` methods

## Cli and Api (Composition Roots)

- Wire DI via `AddTradingResearchEngine` + `AddTradingResearchEngineInfrastructure`
- Parse input and invoke Application use cases
- Render output via `IReporter`
- Contain zero business logic, zero domain types defined locally

## PropFirmModule Bounded Context

- Namespace: `TradingResearchEngine.Application.PropFirm`
- Consumes `BacktestResult` and research workflow outputs
- Does not modify or extend any Core interface or abstract type
- USD-only: returns a validation error for non-USD inputs
- Economics formulas (`ChallengeProbability`, `MonthlyPayoutExpectancy`, `LifetimeEV`,
  `BreakevenMonths`) are pure functions on `PropFirmEvaluator` — no side effects

## Strategy Registration

Strategies are discovered via a named registry, not hard-coded DI registrations.

- Every `IStrategy` implementation is decorated with `[StrategyName("name")]`
- `StrategyRegistry` (Application layer) scans assemblies registered via
  `AddStrategyAssembly(Assembly)` at startup and builds a name → type map
- `RunScenarioUseCase` resolves the strategy type from `ScenarioConfig.StrategyType`
  using `StrategyRegistry` before constructing the engine
- This pattern is the V1 standard; it is designed to be replaced by an
  `AssemblyLoadContext`-based plugin loader without changing the `IStrategy` contract

## EventQueue Ownership

- One `EventQueue` instance is created per engine run — never shared across runs
- The engine owns the queue for the lifetime of a single `RunAsync` call
- Research workflows that run multiple engine instances create one queue per instance
