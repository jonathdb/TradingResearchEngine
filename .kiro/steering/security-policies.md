# Security Policies

## No Stack Traces in Responses

No exception message, stack trace, or inner exception detail may appear in any HTTP response body.
`ErrorHandlingMiddleware` enforces this for the API host.
The CLI may print a user-friendly error message to stderr but must not print raw exception stack traces.

## CorrelationId Pattern

Every API 500 response includes a `correlationId` field (a new `Guid` per request).
The full exception is logged server-side tagged with the same `correlationId`.
This allows support diagnosis without exposing internals to callers.

## Input Validation

All `ScenarioConfig` fields are validated before any engine run starts.
Validation returns a structured error listing every invalid field — never a raw exception.
`FirmRuleSet` JSON deserialization validates required fields and returns a structured error on missing fields.
No simulation work begins until validation passes.

## No Secrets in Code

No API keys, connection strings, or credentials appear in source code or committed config files.
External service credentials (future data providers) are sourced from environment variables or a secrets manager.

## Dependency Injection Only

No service locator pattern (`IServiceProvider.GetService` called outside of composition roots).
All dependencies are constructor-injected.
`IServiceProvider` is only accessed in `Program.cs` of Cli and Api.

## Assembly Loading

The `StrategyRegistry` scans only explicitly registered assemblies (`AddStrategyAssembly`).
It does not scan `AppDomain.CurrentDomain.GetAssemblies()` or load arbitrary paths.
When the plugin upgrade path (AssemblyLoadContext) is implemented, loaded assemblies
must be validated against a configurable allow-list before execution.
