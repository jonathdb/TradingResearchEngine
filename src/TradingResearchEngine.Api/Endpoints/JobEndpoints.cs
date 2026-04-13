using System.Text.Json;
using TradingResearchEngine.Api.Dtos;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Api.Endpoints;

/// <summary>Maps job lifecycle endpoints.</summary>
public static class JobEndpoints
{
    /// <summary>Registers all job endpoints on the route builder.</summary>
    public static void MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/jobs", SubmitJob)
            .WithName("SubmitJob").WithTags("Jobs")
            .Produces<JobSubmittedResponse>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status400BadRequest);

        app.MapGet("/jobs/{jobId}", GetJob)
            .WithName("GetJob").WithTags("Jobs")
            .Produces<BacktestJob>()
            .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/jobs/{jobId}", CancelJob)
            .WithName("CancelJob").WithTags("Jobs")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/jobs/{jobId}/result", GetJobResult)
            .WithName("GetJobResult").WithTags("Jobs")
            .Produces<BacktestResult>()
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/jobs/{jobId}/progress/stream", StreamJobProgress)
            .WithName("StreamJobProgress").WithTags("Jobs")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> SubmitJob(
        SubmitJobRequest request,
        JobExecutor executor,
        CancellationToken ct)
    {
        var errors = ValidateSubmitRequest(request);
        if (errors.Count > 0)
            return Results.BadRequest(new { errors });

        var jobId = await executor.SubmitAsync(request.JobType, request.Config!, ct);
        var job = await executor.GetJobAsync(jobId, ct);

        var response = new JobSubmittedResponse(jobId, job!.Status, job.SubmittedAt);
        return Results.Created($"/jobs/{jobId}", response);
    }

    private static async Task<IResult> GetJob(
        string jobId,
        JobExecutor executor,
        CancellationToken ct)
    {
        var job = await executor.GetJobAsync(jobId, ct);
        return job is not null ? Results.Ok(job) : Results.NotFound();
    }

    private static async Task<IResult> CancelJob(
        string jobId,
        JobExecutor executor,
        CancellationToken ct)
    {
        var cancelled = await executor.CancelAsync(jobId, ct);
        return cancelled
            ? Results.Ok(new { message = "Job cancelled." })
            : Results.NotFound();
    }

    private static async Task<IResult> GetJobResult(
        string jobId,
        JobExecutor executor,
        IRepository<BacktestResult> resultRepo,
        CancellationToken ct)
    {
        var job = await executor.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        if (job.ResultId is null)
            return Results.NotFound();

        var result = await resultRepo.GetByIdAsync(job.ResultId, ct);
        return result is not null ? Results.Ok(result) : Results.NotFound();
    }

    private static async Task<IResult> StreamJobProgress(
        string jobId,
        JobExecutor executor,
        CancellationToken ct)
    {
        var job = await executor.GetJobAsync(jobId, ct);
        if (job is null)
            return Results.NotFound();

        return Results.Stream(async stream =>
        {
            var writer = new StreamWriter(stream) { AutoFlush = true };
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var current = await executor.GetJobAsync(jobId, CancellationToken.None);
                    if (current is null) break;

                    var data = JsonSerializer.Serialize(new
                    {
                        status = current.Status.ToString(),
                        progress = current.Progress
                    });
                    await writer.WriteLineAsync($"data: {data}");
                    await writer.WriteLineAsync();

                    if (current.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                        break;

                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected — exit silently
            }
        }, contentType: "text/event-stream");
    }

    private static List<object> ValidateSubmitRequest(SubmitJobRequest request)
    {
        var errors = new List<object>();

        if (request.Config is null && request.Strategy is null)
            errors.Add(new { field = "Config", message = "Either Config or Strategy sub-object is required." });

        return errors;
    }
}
