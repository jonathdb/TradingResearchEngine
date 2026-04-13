# Technology Standards

## Runtime and Language

- .NET 8, C# 12
- Nullable reference types enabled solution-wide (`<Nullable>enable</Nullable>`)
- Implicit usings enabled solution-wide (`<ImplicitUsings>enable</ImplicitUsings>`)
- Treat warnings as errors in CI (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)

## NuGet Packages

| Package | Project |
|---|---|
| `FsCheck.Xunit` | UnitTests |
| `xunit` + `xunit.runner.visualstudio` | UnitTests, IntegrationTests |
| `Moq` | UnitTests |
| `System.CommandLine` | Cli |
| `Microsoft.AspNetCore.OpenApi` | Api |
| `CsvHelper` | Infrastructure |
| `Microsoft.Extensions.Options` | Application, Infrastructure |
| `Microsoft.Extensions.DependencyInjection` | Application, Infrastructure |
| `Microsoft.Extensions.Http` | Infrastructure |
| `Polly` | Infrastructure |
| `Microsoft.Extensions.Logging.Abstractions` | Core, Application |
| `Microsoft.Extensions.Logging` | Infrastructure, Cli, Api |

Do not add packages outside this list without updating this file and the tasks list.

## No Magic Numbers

All thresholds, defaults, and limits are named constants or `IOptions<T>`-bound configuration fields.
Named constant classes: `MonteCarloDefaults`, `RiskDefaults`, `ReportingDefaults`.

## Records and Immutability

- Domain types (events, results, config, value types) are `record` or `readonly record struct`.
- Mutable state is confined to `Portfolio` and `EventQueue` internals.
- Public collections on records are `IReadOnlyList<T>` or `IReadOnlyDictionary<K,V>`.

## Async

- Data provider methods return `IAsyncEnumerable<T>`.
- Use case and workflow entry points are `async Task<T>`.
- Engine loop is `async Task<BacktestResult>`.
- Do not use `.Result` or `.Wait()` anywhere.
- `CancellationToken` is accepted by `IBacktestEngine.RunAsync`, all use case entry points, and all `IDataProvider` methods. It is propagated to every `await` call inside those methods. `IStrategy.OnMarketData` is synchronous and does not receive a token.

## Logging

- All logging uses `Microsoft.Extensions.Logging.ILogger<T>` injected via constructor.
- `ILogger<T>` is available in Core via `Microsoft.Extensions.Logging.Abstractions` (zero external dependencies).
- Named log events: `MarginBreachWarning`, `RiskRejection`, `StrategyException`, `UnrecognisedEvent`, `MalformedRecord`.
- No `Console.WriteLine` for diagnostic output anywhere outside `ConsoleReporter`.

## XML Doc Comments

All `public` types and members in Core and Application carry XML doc comments (`/// <summary>`).
Infrastructure, Cli, and Api public members are documented where non-obvious.

## Annualisation

`ScenarioConfig.BarsPerYear` is the canonical source of truth for Sharpe/Sortino annualisation.
No hardcoded `252` or `Math.Sqrt(252)` may appear in `MetricsCalculator` or any other computation.
Defaults: Daily=252, H4=1512, H1=6048, M15=24192.

## Deterministic Stochastic Workflows

All stochastic workflows (Monte Carlo, bootstrap, perturbation) must accept explicit seeds
and produce deterministic outputs when the same seed and inputs are supplied.
