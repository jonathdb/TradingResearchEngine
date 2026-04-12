# Implementation Plan: V5.1 Engine Fixes

## Overview

Six targeted fixes for V5.0 issues, implemented in dependency order: Issue 1 (typed request wrappers) → Issue 6 (block bootstrap) → Issue 4 (parameter key validation) → Issue 3 (draft validation) → Issue 2 (job worker) → Issue 5 (SSE streaming). Each task references specific requirements and design sections. Property-based tests use FsCheck.Xunit; unit tests use xUnit + Moq. UnitTests reference Core and Application only.

## Tasks

- [x] 1. Implement typed request wrappers and update scenario endpoints (Issue 1)
  - [x] 1.1 Create `WorkflowRequests.cs` with `SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest` records
    - Create `src/TradingResearchEngine.Api/Dtos/WorkflowRequests.cs`
    - Each record wraps `ScenarioConfig Config` and optional workflow options (`SweepOptions?`, `MonteCarloOptions?`, `WalkForwardOptions?`)
    - _Requirements: 1.1, 1.2, 1.3, 1.5_

  - [x] 1.2 Update `ScenarioEndpoints.cs` to bind request wrappers with backward-compatible bare-body fallback
    - Modify `src/TradingResearchEngine.Api/Endpoints/ScenarioEndpoints.cs`
    - For `/scenarios/sweep`, `/scenarios/montecarlo`, `/scenarios/walkforward`: read body as `JsonDocument`, check for `"Config"` property, deserialize as wrapper or bare `ScenarioConfig` with `X-Deprecation` header
    - Pass `request.Options ?? new XxxOptions()` to each workflow
    - Add `WalkForwardOptions` zero-TimeSpan validation (return 400 for `InSampleLength`, `OutOfSampleLength`, or `StepSize` equal to `TimeSpan.Zero`)
    - Leave `/scenarios/run` unchanged
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8_

  - [ ]* 1.3 Write unit tests for scenario endpoint wrapper binding
    - Test: MC defaults applied when Options is null (Req 1.2)
    - Test: WalkForward zero TimeSpan returns 400 (Req 1.4)
    - Test: `/scenarios/run` unchanged (Req 1.6)
    - Test: Bare ScenarioConfig body → X-Deprecation header (Req 1.8)
    - _Requirements: 1.2, 1.4, 1.6, 1.8_

- [x] 2. Checkpoint — Verify Issue 1 compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 3. Implement block bootstrap for Monte Carlo simulation (Issue 6)
  - [x] 3.1 Add `BlockSize` property to `MonteCarloOptions`
    - Modify `src/TradingResearchEngine.Application/Configuration/OptionClasses.cs`
    - Add `public int BlockSize { get; set; } = 1;` with XML doc comment
    - _Requirements: 6.1_

  - [x] 3.2 Implement block bootstrap sampling in `MonteCarloWorkflow.RunSimulation`
    - Modify `src/TradingResearchEngine.Application/Research/MonteCarloWorkflow.cs`
    - Clamp `BlockSize` to `tradeCount` when it exceeds the number of trades
    - When `BlockSize <= 1`: existing IID path (`rng.Next(tradeCount)`)
    - When `BlockSize > 1`: pick a new random block start every `effectiveBlockSize` trades, sample contiguous indices wrapping circularly
    - Preserve identical RNG call sequence when `BlockSize = 1` for V5.0 backward compatibility
    - _Requirements: 6.1, 6.2, 6.4, 6.5, 6.7_

  - [x] 3.3 Add `BlockSize < 1` validation in `PreflightValidator`
    - Modify `src/TradingResearchEngine.Application/Engine/PreflightValidator.cs`
    - Add `ValidateMonteCarloOptions` method emitting `RANGE_VIOLATION` error when `BlockSize < 1`
    - _Requirements: 6.3_

  - [ ]* 3.4 Write property test: Monte Carlo path count equals SimulationCount (Property 1)
    - **Property 1: Monte Carlo path count equals SimulationCount**
    - Test class: `MonteCarloWorkflowProperties` in `src/TradingResearchEngine.UnitTests/`
    - For any valid `MonteCarloOptions` with `SimulationCount` in [1, 200] and any `BacktestResult` with ≥ 1 trade, `MonteCarloResult.SampledPaths.Count == SimulationCount`
    - **Validates: Requirements 1.1**

  - [ ]* 3.5 Write property test: Block bootstrap produces contiguous blocks (Property 8)
    - **Property 8: Block bootstrap produces contiguous blocks**
    - Test class: `MonteCarloWorkflowProperties`
    - For any return sequence of length ≥ 2 and `BlockSize` in [2, sequence length], verify sampled indices form contiguous blocks of exactly `BlockSize` consecutive indices (wrapping circularly)
    - **Validates: Requirements 6.2**

  - [ ]* 3.6 Write property test: BlockSize clamped when exceeding trade count (Property 9)
    - **Property 9: BlockSize clamped when exceeding trade count**
    - Test class: `MonteCarloWorkflowProperties`
    - For any `MonteCarloOptions` with `BlockSize > tradeCount`, simulation completes without error and effective block size equals trade count
    - **Validates: Requirements 6.4**

  - [x] 3.7 Write property test: Monte Carlo determinism with same seed and BlockSize (Property 10)
    - **Property 10: Monte Carlo determinism with same seed and BlockSize**
    - Test class: `MonteCarloWorkflowProperties`
    - For any seed, any `BlockSize ≥ 1`, and any valid `BacktestResult` with trades, two runs with identical inputs produce bit-for-bit identical results
    - Non-optional: cannot ship Issue 6 without determinism guarantee
    - **Validates: Requirements 6.7**

  - [ ]* 3.8 Write unit test for BlockSize default value
    - Test: `BlockSize` default is 1 (Req 6.1)
    - _Requirements: 6.1_

- [x] 4. Checkpoint — Verify Issue 6 compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 5. Implement parameter key validation against schema (Issue 4)
  - [x] 5.1 Add `ValidateUnknownKeys` method to `PreflightValidator`
    - Modify `src/TradingResearchEngine.Application/Engine/PreflightValidator.cs`
    - Resolve strategy schema via `IStrategySchemaProvider.GetSchema`
    - Build `HashSet<string>` of known names with `StringComparer.OrdinalIgnoreCase`
    - Emit `UNKNOWN_PARAM` error for any key not in the schema
    - Call `ValidateUnknownKeys` from `Validate()` after `ValidateRequiredFields` and before `ValidateMissingParams`
    - _Requirements: 4.1, 4.5, 4.7_

  - [x] 5.2 Adjust `ValidateMissingParams` severity based on `DefaultValue` presence
    - Modify `src/TradingResearchEngine.Application/Engine/PreflightValidator.cs`
    - Missing required parameter with non-null `DefaultValue` → `PreflightSeverity.Warning` (non-blocking)
    - Missing required parameter with null `DefaultValue` → `PreflightSeverity.Error` (blocking)
    - _Requirements: 4.2, 4.6_

  - [ ]* 5.3 Write property test: Unknown parameter keys produce UNKNOWN_PARAM errors (Property 4)
    - **Property 4: Unknown parameter keys produce UNKNOWN_PARAM errors (case-insensitive)**
    - Test class: `PreflightValidatorProperties` in `src/TradingResearchEngine.UnitTests/`
    - Mock `IStrategySchemaProvider`; for any config with unknown keys, verify `UNKNOWN_PARAM` findings; for matching keys (case-insensitive), verify no `UNKNOWN_PARAM` findings
    - **Validates: Requirements 4.1, 4.7**

  - [ ]* 5.4 Write property test: Missing required parameters produce severity based on DefaultValue (Property 5)
    - **Property 5: Missing required parameters produce severity based on DefaultValue presence**
    - Test class: `PreflightValidatorProperties`
    - Mock `IStrategySchemaProvider`; for any missing required parameter, verify Warning when `DefaultValue` is non-null, Error when `DefaultValue` is null
    - **Validates: Requirements 4.2, 4.6**

  - [ ]* 5.5 Write property test: Out-of-range parameter values produce RANGE_VIOLATION errors (Property 6)
    - **Property 6: Out-of-range parameter values produce RANGE_VIOLATION errors**
    - Test class: `PreflightValidatorProperties`
    - For any numeric parameter below `Min` or above `Max`, verify `RANGE_VIOLATION` error finding
    - **Validates: Requirements 4.3**

  - [ ]* 5.6 Write property test: Valid parameters produce no parameter-related error findings (Property 7)
    - **Property 7: Valid parameters produce no parameter-related error findings**
    - Test class: `PreflightValidatorProperties`
    - For any config with only valid keys within declared ranges, verify zero `UNKNOWN_PARAM` or `RANGE_VIOLATION` findings
    - **Validates: Requirements 4.4**

- [x] 6. Checkpoint — Verify Issue 4 compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 7. Implement ConfigDraft step validation (Issue 3)
  - [x] 7.1 Create `ConfigDraftValidator.cs` with step-completeness validation
    - Create `src/TradingResearchEngine.Application/Strategy/ConfigDraftValidator.cs`
    - Static class with `ValidateStep(ConfigDraft draft)` returning `IReadOnlyList<string>`
    - Cumulative rules: Step ≥ 1 requires `StrategyName`; Step ≥ 2 requires `StrategyType` + `DataConfig`; Step ≥ 3 requires `StrategyParameters` with ≥ 1 entry; Step ≥ 4 requires `ExecutionConfig` + `RiskConfig`; Step 5 is cumulative
    - Pure function, no dependencies
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.7, 3.8_

  - [x] 7.2 Wire `ConfigDraftValidator` into draft save handler to return 400 on validation failure
    - Call `ConfigDraftValidator.ValidateStep` before persisting any draft
    - Return HTTP 400 with error list if validation fails; do not persist invalid drafts
    - _Requirements: 3.6, 3.7_

  - [ ]* 7.3 Write property test: ConfigDraft step validation produces correct errors for missing fields (Property 2)
    - **Property 2: ConfigDraft step validation produces correct errors for missing fields**
    - Test class: `ConfigDraftValidatorProperties` in `src/TradingResearchEngine.UnitTests/`
    - For any `ConfigDraft` with `CurrentStep` in [2, 5] and any combination of null/missing required fields, verify `ValidateStep` returns exactly the expected error messages
    - **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5, 3.8**

  - [ ]* 7.4 Write property test: ConfigDraft with all required fields produces no errors (Property 3)
    - **Property 3: ConfigDraft with all required fields for its step produces no errors**
    - Test class: `ConfigDraftValidatorProperties`
    - For any `ConfigDraft` with `CurrentStep` in [1, 5] where all required fields are populated, verify `ValidateStep` returns an empty list
    - **Validates: Requirements 3.6**

- [x] 8. Checkpoint — Verify Issue 3 compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 9. Implement background job worker (Issue 2)
  - [x] 9.1 Add `ScenarioConfig? Config` to `BacktestJob` record
    - Modify `src/TradingResearchEngine.Application/Research/BacktestJob.cs`
    - Add `ScenarioConfig? Config = null` as a default parameter to preserve backward compatibility
    - _Requirements: 2.1_

  - [x] 9.2 Update `JobExecutor.SubmitAsync` to accept and persist `ScenarioConfig`
    - Modify `src/TradingResearchEngine.Application/Research/JobExecutor.cs`
    - Change signature to `SubmitAsync(JobType jobType, ScenarioConfig config, CancellationToken ct = default)`
    - Persist config on the `BacktestJob` record
    - _Requirements: 2.1_

  - [x] 9.3 Update `JobEndpoints.SubmitJob` to pass config to `JobExecutor.SubmitAsync`
    - Modify `src/TradingResearchEngine.Api/Endpoints/JobEndpoints.cs`
    - Pass `request.Config!` to `executor.SubmitAsync`
    - _Requirements: 2.1_

  - [x] 9.4 Create `JobWorkerOptions.cs` configuration class
    - Create `src/TradingResearchEngine.Application/Configuration/JobWorkerOptions.cs`
    - `PollInterval` (default 2s) and `MaxConcurrentJobs` (default 1)
    - _Requirements: 2.2, 2.7_

  - [x] 9.5 Create `JobWorkerService.cs` as a `BackgroundService`
    - Create `src/TradingResearchEngine.Application/Research/JobWorkerService.cs`
    - Constructor-inject `IServiceScopeFactory`, `JobExecutor`, `IOptions<JobWorkerOptions>`, `ILogger<JobWorkerService>`
    - Poll `JobExecutor.ListJobsAsync()` for `Queued` jobs on configurable interval
    - For each job dispatch: create a new `IServiceScope` via `IServiceScopeFactory.CreateScope()` and resolve scoped services (`RunScenarioUseCase`, `ParameterSweepWorkflow`, `MonteCarloWorkflow`, `WalkForwardWorkflow`) from the scope — do NOT inject them directly via constructor (they depend on scoped repositories)
    - Dispatch by `JobType`: `SingleRun` → `RunScenarioUseCase`, `ParameterSweep` → `ParameterSweepWorkflow`, `MonteCarlo` → `MonteCarloWorkflow`, `WalkForward` → `WalkForwardWorkflow`
    - Link per-job `CancellationToken` with host `stoppingToken` via `CreateLinkedTokenSource`
    - On success: persist result, call `MarkCompletedAsync`
    - On failure: call `MarkFailedAsync(jobId, ex.Message)` — never persist stack traces
    - Unknown `JobType` → `MarkFailedAsync(jobId, $"Unsupported job type: {jobType}")`
    - _Requirements: 2.2, 2.3, 2.4, 2.5, 2.7, 2.8_

  - [x] 9.6 Register `JobWorkerService` and `JobWorkerOptions` in `Program.cs`
    - Modify `src/TradingResearchEngine.Api/Program.cs`
    - `builder.Services.AddHostedService<JobWorkerService>();`
    - `builder.Services.Configure<JobWorkerOptions>(builder.Configuration.GetSection("JobWorker"));`
    - _Requirements: 2.2_

  - [ ]* 9.7 Write property test: Config persisted on BacktestJob round-trip (Property 11)
    - **Property 11: Config persisted on BacktestJob round-trip**
    - Test class: `JobExecutorProperties` in `src/TradingResearchEngine.UnitTests/`
    - For any valid `ScenarioConfig`, submit via `JobExecutor.SubmitAsync` then retrieve via `GetJobAsync`; verify `Config` is equivalent to the original
    - Use in-memory `IRepository<BacktestJob>` fake
    - **Validates: Requirements 2.1**

  - [ ]* 9.8 Write property test: Failed job ErrorMessage contains no stack traces (Property 12)
    - **Property 12: Failed job ErrorMessage contains no stack traces**
    - Test class: `JobWorkerServiceProperties` in `src/TradingResearchEngine.UnitTests/`
    - For any exception thrown during job execution, verify `ErrorMessage` does not contain `"   at "` or `".cs:line"` patterns
    - **Validates: Requirements 2.4**

  - [ ]* 9.9 Write property test: Unknown JobType produces formatted error message (Property 13)
    - **Property 13: Unknown JobType produces formatted error message**
    - Test class: `JobWorkerServiceProperties`
    - For any `JobType` not in {`SingleRun`, `ParameterSweep`, `MonteCarlo`, `WalkForward`}, verify job transitions to `Failed` with `ErrorMessage` exactly `$"Unsupported job type: {jobType}"`
    - **Validates: Requirements 2.8**

  - [ ]* 9.10 Write unit tests for job lifecycle and orphan recovery
    - Test: Orphaned job recovery marks Queued/Running as Failed (Req 2.6)
    - _Requirements: 2.6_

- [x] 10. Checkpoint — Verify Issue 2 compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 11. Implement SSE progress streaming (Issue 5)
  - [x] 11.1 Add SSE streaming endpoint to `JobEndpoints`
    - Modify `src/TradingResearchEngine.Api/Endpoints/JobEndpoints.cs`
    - Register `GET /jobs/{jobId}/progress/stream` with `StreamJobProgress` handler
    - Return 404 if job not found
    - Set `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`
    - Poll `JobExecutor.GetJobAsync` every 1 second, write `data: {json}\n\n` per SSE spec
    - On terminal status (`Completed`, `Failed`, `Cancelled`): emit final event and close
    - On client disconnect: exit loop silently, no error logging
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6_

  - [ ]* 11.2 Write unit tests for SSE endpoint
    - Test: SSE 404 for non-existent job (Req 5.5)
    - Test: SSE headers (Content-Type, Cache-Control, Connection) (Req 5.1, 5.6)
    - _Requirements: 5.1, 5.5, 5.6_

- [x] 12. Checkpoint — Verify Issue 5 compiles and tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 13. Integration tests and final wiring
  - [ ]* 13.1 Write integration tests for full job lifecycle
    - Test: Full job lifecycle (Queued → Running → Completed) via `WebApplicationFactory` (Req 2.2, 2.3)
    - Test: Job cancellation flow (Req 2.5)
    - Remove `JobWorkerService` from hosted services in test factory to prevent race conditions
    - _Requirements: 2.2, 2.3, 2.5_

  - [ ]* 13.2 Write integration tests for SSE streaming
    - Test: SSE stream emits events during running job (Req 5.2, 5.3)
    - Test: SSE client disconnect handled gracefully (Req 5.4)
    - _Requirements: 5.2, 5.3, 5.4_

  - [ ]* 13.3 Write integration test for OpenAPI schema
    - Test: OpenAPI spec contains wrapper schemas for `SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest` (Req 1.7)
    - _Requirements: 1.7_

  - [x] 13.4 Write V5.0 regression test for Monte Carlo BlockSize=1
    - `MonteCarloBlockSize1Regression`: Run with `BlockSize=1`, `Seed=42`, known trade sequence, verify output matches captured V5.0 baseline
    - First run the existing V5.0 code path with the test inputs to capture the baseline values, then hardcode those values as expected outputs in the regression test
    - Non-optional: this is the only test that proves the block bootstrap refactor does not alter IID behaviour
    - _Requirements: 6.5_

- [x] 14. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation after each issue
- Property tests validate universal correctness properties from the design document (Properties 1–13)
- Unit tests validate specific examples and edge cases
- UnitTests reference Core and Application only; IntegrationTests may reference all projects
- All mocking uses Moq; property tests use FsCheck.Xunit with `[Property(MaxTest = 100)]`
- Dependency order: Issue 1 → Issue 6 → Issue 4 → Issue 3 → Issue 2 → Issue 5
