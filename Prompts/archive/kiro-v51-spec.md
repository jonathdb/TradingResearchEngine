# TradingResearchEngine V5.1 — Kiro Spec

## Overview

This spec defines the V5.1 milestone for TradingResearchEngine. It addresses six confirmed issues discovered during a full code review of the V5.0 codebase: workflow options being silently ignored by the API, the job queue having no background executor, missing draft step validation, unguarded parameter dictionary key typos, the absence of progress streaming, and the Monte Carlo IID bootstrap not preserving serial correlation. Each issue is described with its exact location, the fix required, and acceptance criteria.

---

## Issue 1 — Workflow Options Ignored by Scenario Endpoints

### Problem

`POST /scenarios/sweep`, `POST /scenarios/montecarlo`, and `POST /scenarios/walkforward` all instantiate their options with `new SweepOptions()`, `new MonteCarloOptions()`, and `new WalkForwardOptions()` respectively, ignoring any caller-supplied values. A caller cannot set `SimulationCount`, `Seed`, `RuinThresholdPercent`, `MaxDegreeOfParallelism`, `InSampleLength`, `OutOfSampleLength`, or `StepSize` via the API. The underlying workflow classes accept and use these options correctly — only the endpoint binding is missing.

**Affected file:** `src/TradingResearchEngine.Api/Endpoints/ScenarioEndpoints.cs`

### Fix

Introduce typed request wrapper records in the `TradingResearchEngine.Api.Dtos` namespace:

```csharp
// src/TradingResearchEngine.Api/Dtos/WorkflowRequests.cs
public sealed record SweepRequest(ScenarioConfig Config, SweepOptions? Options = null);
public sealed record MonteCarloRequest(ScenarioConfig Config, MonteCarloOptions? Options = null);
public sealed record WalkForwardRequest(ScenarioConfig Config, WalkForwardOptions? Options = null);
```

Update each endpoint to bind these wrappers and pass the options through:

```csharp
// /scenarios/sweep
app.MapPost("/scenarios/sweep", async (
    HttpContext httpContext,
    SweepRequest request,
    PreflightValidator preflightValidator,
    ParameterSweepWorkflow workflow,
    CancellationToken ct) =>
{
    AddDeprecationHeaderIfFlat(httpContext, request.Config);
    var preflight = preflightValidator.Validate(request.Config);
    if (preflight.HasErrors) return /* existing error response */;
    var options = request.Options ?? new SweepOptions();
    var result = await workflow.RunAsync(request.Config, options, ct);
    return Results.Ok(result);
});

// /scenarios/montecarlo  — same pattern, MonteCarloRequest, MonteCarloOptions
// /scenarios/walkforward — same pattern, WalkForwardRequest, WalkForwardOptions
```

The `WalkForwardOptions` wrapper must validate that `InSampleLength`, `OutOfSampleLength`, and `StepSize` are non-zero when provided, and surface these as preflight errors rather than throwing `ArgumentException` inside the workflow.

### Acceptance Criteria

- `POST /scenarios/montecarlo` with `{ "Config": {...}, "Options": { "SimulationCount": 500, "Seed": 42 } }` produces a reproducible result with 500 paths.
- `POST /scenarios/montecarlo` with no `Options` field uses `MonteCarloDefaults.DefaultSimulationCount` (1000).
- `POST /scenarios/walkforward` with `Options.InSampleLength = "365.00:00:00"` uses that IS length.
- `POST /scenarios/walkforward` with `Options.InSampleLength = "00:00:00"` returns a 400 with a structured error.
- `POST /scenarios/sweep` with `Options.MaxDegreeOfParallelism = 2` limits parallelism accordingly.
- Existing `/scenarios/run` endpoint behaviour is unchanged.
- Swagger/OpenAPI reflects the new request shapes.

---

## Issue 2 — Job Queue Has No Background Executor

### Problem

`POST /jobs` creates a `BacktestJob` record in `JobStatus.Queued` and returns a `jobId`, but no component ever picks up the queued job and executes it. `JobExecutor` manages lifecycle state transitions (`MarkRunningAsync`, `MarkCompletedAsync`, `MarkFailedAsync`) but nothing calls them after `SubmitAsync`. The `/jobs/{id}` endpoint will return `Status: Queued` forever.

Additionally, `SubmitJobRequest` validates that `Config` or `Strategy` is present, but `JobExecutor.SubmitAsync` only accepts a `JobType` — the config is never persisted to the job record, so even if a worker existed it would have nothing to execute.

**Affected files:**
- `src/TradingResearchEngine.Application/Research/BacktestJob.cs`
- `src/TradingResearchEngine.Application/Research/JobExecutor.cs`
- `src/TradingResearchEngine.Api/Endpoints/JobEndpoints.cs`

### Fix

**Step 1 — Persist the config on the job record.**

Add `ScenarioConfig? Config` and `JobType JobType` to `BacktestJob` if not already present. Update `SubmitAsync` to accept a `ScenarioConfig` and persist it:

```csharp
public async Task<string> SubmitAsync(
    JobType jobType, ScenarioConfig config, CancellationToken ct = default)
{
    var jobId = Guid.NewGuid().ToString("N");
    var job = new BacktestJob(
        JobId: jobId,
        JobType: jobType,
        Config: config,
        Status: JobStatus.Queued,
        SubmittedAt: DateTimeOffset.UtcNow);
    await _jobRepo.SaveAsync(job, ct);
    var cts = new CancellationTokenSource();
    _active[jobId] = cts;
    return jobId;
}
```

Update `JobEndpoints.SubmitJob` to pass the config through:

```csharp
var jobId = await executor.SubmitAsync(request.JobType, request.Config!, ct);
```

**Step 2 — Add a background worker.**

Create `src/TradingResearchEngine.Application/Research/JobWorkerService.cs` as a `BackgroundService` that:

1. Polls `JobExecutor.ListJobsAsync()` for jobs in `JobStatus.Queued` on a configurable interval (default 2 seconds).
2. For each queued job, calls `executor.MarkRunningAsync(jobId)`, then dispatches based on `JobType`:
   - `SingleRun` → `RunScenarioUseCase.RunAsync(job.Config)`
   - `Sweep` → `ParameterSweepWorkflow.RunAsync(job.Config, job.SweepOptions ?? new SweepOptions())`
   - `MonteCarlo` → `MonteCarloWorkflow.RunAsync(job.Config, job.MonteCarloOptions ?? new MonteCarloOptions())`
   - `WalkForward` → `WalkForwardWorkflow.RunAsync(job.Config, job.WalkForwardOptions ?? new WalkForwardOptions())`
3. On success, persists the result and calls `executor.MarkCompletedAsync(jobId, resultId)`.
4. On failure (exception or cancellation), calls `executor.MarkFailedAsync(jobId, errorMessage)` with the exception message, never a stack trace.
5. Uses the per-job `CancellationToken` from `executor.GetCancellationToken(jobId)` so `DELETE /jobs/{id}` cooperatively cancels running jobs.

Register the service as a hosted service in `Program.cs`:

```csharp
builder.Services.AddHostedService<JobWorkerService>();
```

### Acceptance Criteria

- `POST /jobs` with a valid `ScenarioConfig` returns `202 Accepted` with a `jobId`.
- `GET /jobs/{id}` transitions from `Queued` → `Running` → `Completed` without manual intervention.
- `GET /jobs/{id}/result` returns the full `BacktestResult` after completion.
- `DELETE /jobs/{id}` on a running job transitions it to `Cancelled` and stops execution.
- A job with an invalid config transitions to `Failed` with a descriptive `ErrorMessage`.
- Process restart marks in-flight jobs as `Failed` (existing `RecoverOrphanedJobsAsync` behaviour preserved).
- Worker respects `MaxDegreeOfParallelism` from `SweepOptions` for sweep jobs (no unbounded parallelism).

---

## Issue 3 — ConfigDraft Step Transitions Have No Server-Side Validation

### Problem

`ConfigDraft.CurrentStep` is an integer field with no enforced semantics. A caller can persist a step-4 draft with `DataConfig = null`, which will fail at run-time with a null reference rather than returning a structured validation error at submission. The builder UI has to duplicate step-completeness logic that should live in the domain.

**Affected file:** `src/TradingResearchEngine.Application/Strategy/ConfigDraft.cs` (and whichever handler promotes drafts)

### Fix

Create `src/TradingResearchEngine.Application/Strategy/ConfigDraftValidator.cs`:

```csharp
public static class ConfigDraftValidator
{
    public static IReadOnlyList<string> ValidateStep(ConfigDraft draft)
    {
        var errors = new List<string>();
        if (draft.CurrentStep >= 2 && string.IsNullOrWhiteSpace(draft.StrategyType))
            errors.Add("Step 2 requires StrategyType to be set.");
        if (draft.CurrentStep >= 2 && draft.DataConfig is null)
            errors.Add("Step 2 requires DataConfig.");
        if (draft.CurrentStep >= 3 && (draft.StrategyParameters is null || draft.StrategyParameters.Count == 0))
            errors.Add("Step 3 requires at least one StrategyParameter.");
        if (draft.CurrentStep >= 4 && draft.ExecutionConfig is null)
            errors.Add("Step 4 requires ExecutionConfig.");
        if (draft.CurrentStep >= 4 && draft.RiskConfig is null)
            errors.Add("Step 4 requires RiskConfig.");
        return errors;
    }
}
```

Call `ConfigDraftValidator.ValidateStep` before persisting a draft (in any `SaveDraftAsync`-style handler), and return a 400 with the error list if validation fails. Do not persist an invalid draft.

### Acceptance Criteria

- Submitting a draft with `CurrentStep = 4` and `ExecutionConfig = null` returns 400 with `"Step 4 requires ExecutionConfig."`.
- Submitting a draft with `CurrentStep = 2` and valid `DataConfig` + `StrategyType` persists successfully.
- Validation is called server-side regardless of what the client sends — no client-only enforcement.

---

## Issue 4 — Strategy Parameter Dictionary Keys Are Not Validated Against Schema

### Problem

`ScenarioConfig.StrategyParameters` is a `Dictionary<string, object>` whose keys are string identifiers. The `StrategySchemaProvider` knows the exact set of valid parameter names (by reflecting on the strategy constructor), but `PreflightValidator` does not compare the caller's keys against the schema. A typo (`"fastperod"` instead of `"fastPeriod"`) silently defaults to the constructor's default value at instantiation, producing a misleading result rather than an error.

**Affected files:**
- `src/TradingResearchEngine.Application/Configuration/PreflightValidator.cs` (or wherever preflight checks live)
- `src/TradingResearchEngine.Application/Strategy/StrategySchemaProvider.cs`

### Fix

Add a parameter key validation step to `PreflightValidator.Validate`. After resolving the strategy type, call `IStrategySchemaProvider.GetSchema(strategyType)` to get the known parameter names, then:

1. Emit a `PreflightSeverity.Error` finding for any key in `ScenarioConfig.StrategyParameters` that does not appear in the schema (unknown parameter).
2. Emit a `PreflightSeverity.Warning` finding for any required schema parameter (where `IsRequired = true`) that is absent from `ScenarioConfig.StrategyParameters` (missing required parameter — the engine would use the constructor default, which may be misleading).
3. For numeric parameters where the schema specifies `Min` or `Max`, emit a `PreflightSeverity.Error` if the supplied value is out of range.

Unknown-parameter errors must block execution (they indicate a typo). Missing-required-parameter warnings are non-blocking (defaults are legitimate). Out-of-range errors block execution.

### Acceptance Criteria

- Submitting `{ "StrategyType": "volatility-scaled-trend", "StrategyParameters": { "fastperod": 10 } }` returns a 400 with `"Unknown parameter 'fastperod'. Did you mean 'fastPeriod'?"` (fuzzy match is optional but recommended).
- Submitting a config with no `StrategyParameters` when all parameters have defaults returns a 200 with a warning in the response headers or body.
- Submitting a parameter value outside the declared `Min`/`Max` range returns a 400.
- Validation runs before any engine execution attempt.

---

## Issue 5 — No Progress Streaming for Long-Running Jobs

### Problem

`JobExecutor.UpdateProgressAsync` correctly persists a `ProgressSnapshot` on every update, and `GET /jobs/{id}` returns the full job including `Progress`. However, consumers must poll. For walk-forward runs (20+ windows) or parameter sweeps (hundreds of scenarios), the job feels frozen from the caller's perspective. `IProgressReporter` and `ProgressSnapshot` already exist but are not wired to any streaming transport.

**Affected file:** `src/TradingResearchEngine.Api/Endpoints/JobEndpoints.cs`

### Fix

Add a Server-Sent Events streaming endpoint:

```csharp
app.MapGet("/jobs/{jobId}/progress/stream", async (
    string jobId,
    JobExecutor executor,
    CancellationToken ct) =>
{
    return Results.Stream(async stream =>
    {
        var writer = new StreamWriter(stream) { AutoFlush = true };
        while (!ct.IsCancellationRequested)
        {
            var job = await executor.GetJobAsync(jobId, ct);
            if (job is null) break;

            var data = JsonSerializer.Serialize(new
            {
                status = job.Status.ToString(),
                progress = job.Progress
            });
            await writer.WriteLineAsync($"data: {data}\n");

            if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                break;

            await Task.Delay(1000, ct);
        }
    }, contentType: "text/event-stream");
}).WithName("StreamJobProgress").WithTags("Jobs");
```

The response must set `Cache-Control: no-cache` and `Connection: keep-alive` headers. The stream must emit a final event when the job reaches a terminal state (`Completed`, `Failed`, `Cancelled`) and then close cleanly.

### Acceptance Criteria

- `GET /jobs/{id}/progress/stream` returns `Content-Type: text/event-stream`.
- Emits a JSON event at least once per second while the job is running.
- Each event includes `{ "status": "Running", "progress": { ... } }`.
- Emits a final event with the terminal status and then closes.
- Client disconnection (cancelled `CancellationToken`) closes the stream without error.
- Returns 404 if the job does not exist.

---

## Issue 6 — Monte Carlo Bootstrap Is IID and Ignores Serial Correlation

### Problem

`MonteCarloWorkflow` samples trade returns uniformly at random with replacement:

```csharp
int idx = rng.Next(tradeCount);
decimal sampledReturn = returns[idx];
```

This assumes trade returns are serially independent. For trend-following strategies, winning trades cluster during trending periods and losing trades cluster during flat or reversing markets — autocorrelation is a structural feature of the return series, not noise. The IID bootstrap breaks this clustering, overstating diversification and systematically understating tail drawdowns.

**Affected file:** `src/TradingResearchEngine.Application/Research/MonteCarloWorkflow.cs`

### Fix

Add `BlockSize` to `MonteCarloOptions`:

```csharp
// In OptionClasses.cs
public sealed class MonteCarloOptions
{
    public int SimulationCount { get; set; } = MonteCarloDefaults.DefaultSimulationCount;
    public int? Seed { get; set; }
    public decimal RuinThresholdPercent { get; set; } = 0.5m;

    /// <summary>
    /// Block size for block bootstrap. When 1 (default), uses standard IID bootstrap.
    /// When > 1, samples contiguous blocks of this length to preserve serial autocorrelation
    /// in the trade return sequence. Recommended for trend-following strategies: set to
    /// approximate average holding period in trades.
    /// </summary>
    public int BlockSize { get; set; } = 1;
}
```

Update `RunSimulation` to use block sampling when `options.BlockSize > 1`:

```csharp
for (int i = 0; i < tradeCount; i++)
{
    decimal sampledReturn;
    if (options.BlockSize <= 1)
    {
        sampledReturn = returns[rng.Next(tradeCount)];
    }
    else
    {
        // Block bootstrap: sample a random starting index, then draw contiguous returns
        int blockStart = rng.Next(tradeCount);
        int blockOffset = i % options.BlockSize;
        int idx = (blockStart + blockOffset) % tradeCount;
        sampledReturn = returns[idx];
    }
    // ... rest of inner loop unchanged
}
```

Expose `BlockSize` via the API through the `MonteCarloOptions` wrapper added in Issue 1. Document in the API and `GET /workflows` response that `BlockSize = 1` is IID (appropriate for short-hold strategies), and `BlockSize ≈ avgHoldingPeriodInTrades` is recommended for trend-following.

### Acceptance Criteria

- `MonteCarloOptions.BlockSize` defaults to `1`, preserving existing IID behaviour.
- `BlockSize > 1` produces a different equity path distribution (verifiable by comparing P10/P90 band width with IID for a trend strategy with known autocorrelation).
- `BlockSize` is accepted via `POST /scenarios/montecarlo` in the `Options` object.
- `BlockSize` validation: values `< 1` return a 400 preflight error; values `> tradeCount` are clamped to `tradeCount` with a warning.
- The `GET /workflows` `MonteCarlo` entry documents `BlockSize` as an optional parameter.

---

## Execution Order

The issues above are largely independent and can be worked in parallel, with one dependency:

1. **Issue 1** (options wrappers) must be completed before Issue 2 (job worker) and Issue 6 (block bootstrap), as both depend on `MonteCarloOptions` being passed through.
2. **Issue 4** (parameter validation) requires `IStrategySchemaProvider` to already be injectable in `PreflightValidator` — confirm the DI registration before implementing.
3. Issues 3 and 5 are fully independent.

Suggested order for a single developer: Issue 1 → Issue 6 → Issue 4 → Issue 3 → Issue 2 → Issue 5.

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/TradingResearchEngine.Api/Dtos/WorkflowRequests.cs` | `SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest` records |
| `src/TradingResearchEngine.Application/Research/JobWorkerService.cs` | `BackgroundService` executing queued jobs |
| `src/TradingResearchEngine.Application/Strategy/ConfigDraftValidator.cs` | Step-by-step draft validation |

## Files to Modify

| File | Change |
|------|--------|
| `src/TradingResearchEngine.Api/Endpoints/ScenarioEndpoints.cs` | Bind `SweepRequest`, `MonteCarloRequest`, `WalkForwardRequest`; pass options through |
| `src/TradingResearchEngine.Api/Endpoints/JobEndpoints.cs` | Pass config to `SubmitAsync`; add SSE streaming endpoint |
| `src/TradingResearchEngine.Application/Research/BacktestJob.cs` | Add `ScenarioConfig? Config` and workflow-specific options fields |
| `src/TradingResearchEngine.Application/Research/JobExecutor.cs` | Accept `ScenarioConfig` in `SubmitAsync` |
| `src/TradingResearchEngine.Application/Configuration/OptionClasses.cs` | Add `BlockSize` to `MonteCarloOptions` |
| `src/TradingResearchEngine.Application/Research/MonteCarloWorkflow.cs` | Implement block bootstrap |
| `src/TradingResearchEngine.Application/Configuration/PreflightValidator.cs` | Add schema key + range validation |
| `src/TradingResearchEngine.Api/Program.cs` | Register `JobWorkerService` as hosted service |
