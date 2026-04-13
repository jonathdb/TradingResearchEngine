# Requirements Document

## Introduction

This document defines the V5.1 milestone requirements for TradingResearchEngine. V5.1 addresses six confirmed issues discovered during a full code review of the V5.0 codebase: workflow options silently ignored by scenario endpoints, the job queue lacking a background executor, missing server-side draft step validation, unguarded strategy parameter dictionary keys, the absence of progress streaming for long-running jobs, and the Monte Carlo IID bootstrap not preserving serial correlation. Each requirement maps to one issue and includes EARS-compliant acceptance criteria.

## Glossary

- **Scenario_Endpoint**: An ASP.NET Core minimal API endpoint under `/scenarios/` that accepts a `ScenarioConfig` and dispatches to a research workflow (`ParameterSweepWorkflow`, `MonteCarloWorkflow`, or `WalkForwardWorkflow`).
- **Workflow_Options**: Typed option classes (`SweepOptions`, `MonteCarloOptions`, `WalkForwardOptions`) that parameterise research workflow execution.
- **Request_Wrapper**: A typed record (`SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest`) that bundles a `ScenarioConfig` with optional `Workflow_Options` for endpoint model binding.
- **Job_Worker**: A `BackgroundService` (`JobWorkerService`) that polls for queued `BacktestJob` records and dispatches them to the appropriate workflow or use case.
- **BacktestJob**: A persisted record representing an asynchronous execution unit, stored via `IRepository<BacktestJob>`.
- **JobExecutor**: A singleton Application-layer service managing job lifecycle state transitions and cancellation tokens.
- **ConfigDraft**: An in-progress strategy configuration record assembled in the builder, persisted via `IRepository<ConfigDraft>` on every step transition.
- **ConfigDraft_Validator**: A static validation class (`ConfigDraftValidator`) enforcing step-by-step completeness rules on `ConfigDraft` before persistence.
- **Strategy_Schema**: The set of `StrategyParameterSchema` records returned by `IStrategySchemaProvider.GetSchema` for a given strategy type, describing valid parameter names, types, ranges, and required status.
- **PreflightValidator**: An Application-layer validator that checks `ScenarioConfig` for structural and semantic errors before any engine execution begins.
- **SSE_Stream**: A Server-Sent Events endpoint (`GET /jobs/{jobId}/progress/stream`) that pushes job progress updates to connected clients.
- **Block_Bootstrap**: A resampling method that draws contiguous blocks of trade returns (of length `BlockSize`) to preserve serial autocorrelation, as opposed to IID sampling.
- **MonteCarloOptions**: Configuration class for Monte Carlo simulation, including `SimulationCount`, `Seed`, `RuinThresholdPercent`, and `BlockSize`.
- **ProgressSnapshot**: A record persisted on `BacktestJob` representing the latest progress state of a running job.

## Requirements

### Requirement 1: Scenario Endpoints Accept Caller-Supplied Workflow Options

**User Story:** As an API consumer, I want to supply workflow-specific options (simulation count, seed, parallelism, window lengths) when calling scenario endpoints, so that I can control research workflow behaviour without relying on hardcoded defaults.

#### Acceptance Criteria

1. WHEN a caller sends a POST to `/scenarios/montecarlo` with an `Options` object containing `SimulationCount` and `Seed`, THE Scenario_Endpoint SHALL pass those values to `MonteCarloWorkflow.RunAsync` and produce a result with the specified number of paths.
2. WHEN a caller sends a POST to `/scenarios/montecarlo` without an `Options` field, THE Scenario_Endpoint SHALL use `MonteCarloDefaults.DefaultSimulationCount` (1000) and default values for all other `MonteCarloOptions` fields.
3. WHEN a caller sends a POST to `/scenarios/walkforward` with `Options.InSampleLength` set to a valid non-zero `TimeSpan`, THE Scenario_Endpoint SHALL use that in-sample length for the walk-forward analysis.
4. WHEN a caller sends a POST to `/scenarios/walkforward` with `Options.InSampleLength` set to `"00:00:00"`, THE Scenario_Endpoint SHALL return HTTP 400 with a structured error identifying the invalid field.
5. WHEN a caller sends a POST to `/scenarios/sweep` with `Options.MaxDegreeOfParallelism` set to a positive integer, THE Scenario_Endpoint SHALL limit sweep parallelism to that value.
6. THE Scenario_Endpoint for `POST /scenarios/run` SHALL remain unchanged and continue to accept a bare `ScenarioConfig` body.
7. WHEN the API generates its OpenAPI specification, THE Scenario_Endpoint definitions for `/scenarios/sweep`, `/scenarios/montecarlo`, and `/scenarios/walkforward` SHALL reflect the new Request_Wrapper schemas (`SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest`).
8. WHEN a caller sends a bare `ScenarioConfig` body (not wrapped in a `MonteCarloRequest`) to `/scenarios/montecarlo`, THE Scenario_Endpoint SHALL accept the request and return an `X-Deprecation` response header.

### Requirement 2: Background Job Executor Processes Queued Jobs

**User Story:** As an API consumer, I want submitted jobs to be automatically picked up and executed in the background, so that `POST /jobs` results in actual backtest execution without manual intervention.

#### Acceptance Criteria

1. WHEN a caller submits a job via `POST /jobs` with a valid `ScenarioConfig`, THE JobExecutor SHALL persist the `ScenarioConfig` on the BacktestJob record.
2. WHEN a BacktestJob exists in `Queued` status, THE Job_Worker SHALL pick up the job, transition it to `Running`, and dispatch it to the appropriate workflow based on `JobType`.
3. WHEN the dispatched workflow completes successfully, THE Job_Worker SHALL persist the result and transition the BacktestJob to `Completed` with a valid `ResultId`.
4. WHEN the dispatched workflow throws an exception, THE Job_Worker SHALL transition the BacktestJob to `Failed` with a user-friendly `ErrorMessage` containing no stack traces.
5. WHEN a caller sends `DELETE /jobs/{jobId}` on a running job, THE JobExecutor SHALL trigger the per-job `CancellationToken`, and THE Job_Worker SHALL transition the BacktestJob to `Cancelled`.
6. WHEN the application process restarts, THE JobExecutor SHALL mark all orphaned `Queued` and `Running` jobs as `Failed` with the message `"Process restarted; job was not completed."`.
7. WHEN a sweep job is dispatched, THE Job_Worker SHALL respect the `MaxDegreeOfParallelism` from `SweepOptions` on the job record to prevent unbounded parallelism.
8. WHEN a BacktestJob has an unrecognised `JobType`, THE Job_Worker SHALL transition the job to `Failed` with the message `"Unsupported job type: {JobType}"`.

### Requirement 3: Server-Side ConfigDraft Step Validation

**User Story:** As a builder UI consumer, I want the server to validate that all required fields for a given step are present before persisting a ConfigDraft, so that invalid drafts cannot reach downstream processing.

#### Acceptance Criteria

1. WHEN a caller submits a ConfigDraft with `CurrentStep >= 2` and `StrategyType` is null or whitespace, THE ConfigDraft_Validator SHALL return an error `"Step 2 requires StrategyType to be set."`.
2. WHEN a caller submits a ConfigDraft with `CurrentStep >= 2` and `DataConfig` is null, THE ConfigDraft_Validator SHALL return an error `"Step 2 requires DataConfig."`.
3. WHEN a caller submits a ConfigDraft with `CurrentStep >= 3` and `StrategyParameters` is null or empty, THE ConfigDraft_Validator SHALL return an error `"Step 3 requires at least one StrategyParameter."`.
4. WHEN a caller submits a ConfigDraft with `CurrentStep >= 4` and `ExecutionConfig` is null, THE ConfigDraft_Validator SHALL return an error `"Step 4 requires ExecutionConfig."`.
5. WHEN a caller submits a ConfigDraft with `CurrentStep >= 4` and `RiskConfig` is null, THE ConfigDraft_Validator SHALL return an error `"Step 4 requires RiskConfig."`.
6. WHEN a caller submits a ConfigDraft with `CurrentStep = 2` and valid `DataConfig` and `StrategyType`, THE ConfigDraft_Validator SHALL return no errors and the draft SHALL be persisted.
7. THE ConfigDraft_Validator SHALL execute server-side on every draft save request regardless of client-side validation state.
8. WHEN a caller submits a ConfigDraft with `CurrentStep = 5` and any prior step's required fields are missing, THE ConfigDraft_Validator SHALL return errors for all missing fields.

### Requirement 4: Strategy Parameter Keys Validated Against Schema

**User Story:** As an API consumer, I want the engine to reject misspelled or out-of-range strategy parameter keys before execution, so that typos produce clear errors instead of silently falling back to defaults.

#### Acceptance Criteria

1. WHEN `ScenarioConfig.StrategyParameters` contains a key that does not appear in the Strategy_Schema for the resolved strategy type, THE PreflightValidator SHALL emit a `PreflightSeverity.Error` finding identifying the unknown key.
2. WHEN a required parameter (where `IsRequired = true` in the Strategy_Schema) is absent from `ScenarioConfig.StrategyParameters`, THE PreflightValidator SHALL emit a `PreflightSeverity.Warning` finding identifying the missing parameter.
3. WHEN a numeric parameter value falls outside the `Min` or `Max` range declared in the Strategy_Schema, THE PreflightValidator SHALL emit a `PreflightSeverity.Error` finding identifying the out-of-range value.
4. WHEN `ScenarioConfig.StrategyParameters` contains only valid keys within declared ranges, THE PreflightValidator SHALL emit no parameter-related error findings.
5. THE PreflightValidator SHALL execute parameter key and range validation before any engine execution attempt.
6. WHEN all strategy parameters have defaults and `ScenarioConfig.StrategyParameters` is empty or null, THE PreflightValidator SHALL emit warnings for missing required parameters but SHALL NOT block execution.
7. THE PreflightValidator SHALL perform key matching case-insensitively, consistent with ASP.NET Core's default JSON deserialisation behaviour.

### Requirement 5: SSE Progress Streaming for Long-Running Jobs

**User Story:** As an API consumer, I want to receive real-time progress updates for long-running jobs via Server-Sent Events, so that I do not need to poll `GET /jobs/{jobId}` repeatedly.

#### Acceptance Criteria

1. WHEN a caller connects to `GET /jobs/{jobId}/progress/stream`, THE SSE_Stream SHALL respond with `Content-Type: text/event-stream`.
2. WHILE a BacktestJob is in `Running` status, THE SSE_Stream SHALL emit a JSON event containing `status` and `progress` fields at least once per second.
3. WHEN a BacktestJob reaches a terminal status (`Completed`, `Failed`, or `Cancelled`), THE SSE_Stream SHALL emit a final event with the terminal status and then close the connection.
4. WHEN the client disconnects (cancelled `CancellationToken`), THE SSE_Stream SHALL close the server-side stream without logging an error.
5. WHEN a caller connects to `GET /jobs/{jobId}/progress/stream` for a non-existent job, THE SSE_Stream SHALL return HTTP 404.
6. THE SSE_Stream response SHALL include `Cache-Control: no-cache` and `Connection: keep-alive` headers.

### Requirement 6: Block Bootstrap for Monte Carlo Simulation

**User Story:** As a quantitative researcher, I want to run Monte Carlo simulations using block bootstrap resampling, so that serial autocorrelation in trade return sequences (common in trend-following strategies) is preserved in the simulated paths.

#### Acceptance Criteria

1. THE MonteCarloOptions SHALL include a `BlockSize` property with a default value of `1`, preserving existing IID bootstrap behaviour.
2. WHEN `BlockSize` is greater than 1, THE `MonteCarloWorkflow` SHALL sample contiguous blocks of trade returns of the specified length instead of individual IID samples.
3. WHEN `BlockSize` is less than 1, THE PreflightValidator SHALL return an HTTP 400 error identifying the invalid `BlockSize` value.
4. WHEN `BlockSize` exceeds the number of trades in the return sequence, THE `MonteCarloWorkflow` SHALL clamp `BlockSize` to the trade count and emit a warning.
5. WHEN `BlockSize` equals 1 and the same `Seed` is supplied, THE MonteCarloWorkflow SHALL produce a `SimulatedPaths` collection with the same equity curve values as V5.0 for identical inputs, verifiable by a regression test.
6. WHEN a caller sends a POST to `/scenarios/montecarlo` with `Options.BlockSize` set, THE Scenario_Endpoint SHALL pass the `BlockSize` value through to `MonteCarloWorkflow` via `MonteCarloOptions`.
7. WHEN the same seed and `BlockSize` are supplied, THE `MonteCarloWorkflow` SHALL produce deterministic, reproducible results.
