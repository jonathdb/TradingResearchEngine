# API Standards

## Style

ASP.NET Core minimal APIs only. No MVC controllers.
All endpoints are registered in `ScenarioEndpoints.cs` via extension methods on `IEndpointRouteBuilder`.

## Endpoints

| Method | Path | Handler | Success response |
|---|---|---|---|
| POST | `/scenarios/run` | `RunScenarioUseCase` | 200 `BacktestResult` |
| POST | `/scenarios/sweep` | `ParameterSweepWorkflow` | 200 `SweepResult` |
| POST | `/scenarios/montecarlo` | `MonteCarloWorkflow` | 200 `MonteCarloResult` |
| POST | `/scenarios/walkforward` | `WalkForwardWorkflow` | 200 `WalkForwardResult` |

## HTTP Status Codes

- 200 — successful result
- 400 — request body validation failure; body is a JSON array of `{ field, message }` objects
- 500 — internal error; body is `{ correlationId, message }` — no stack traces, no exception details

## Error Response Shapes

```json
// 400
{ "errors": [ { "field": "SimulationCount", "message": "Must be >= 1" } ] }

// 500
{ "correlationId": "abc-123", "message": "An unexpected error occurred." }
```

## Middleware

`ErrorHandlingMiddleware` wraps all requests:
- Catches all unhandled exceptions
- Generates a `CorrelationId` (new `Guid`) per request
- Logs the full exception server-side (never in the response)
- Returns HTTP 500 with the shape above

## CORS

Configured via `IOptions<CorsOptions>` bound from `appsettings.json`.
No hard-coded origins anywhere in code.

## Serialisation

`System.Text.Json` with default options unless a specific override is required.
`BacktestResult` and all result types must round-trip without data loss (Property 1).

## OpenAPI

`Microsoft.AspNetCore.OpenApi` is registered.
Every endpoint has a `.WithName()` and `.WithTags()` call.
