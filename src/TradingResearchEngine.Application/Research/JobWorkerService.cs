using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Engine;
using TradingResearchEngine.Application.Research.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Background service that polls for queued jobs and dispatches them
/// to the appropriate workflow or use case. Scoped services are resolved
/// per-job via <see cref="IServiceScopeFactory"/>.
/// </summary>
public sealed class JobWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly JobExecutor _executor;
    private readonly ILogger<JobWorkerService> _logger;
    private readonly JobWorkerOptions _options;

    /// <summary>Creates a new <see cref="JobWorkerService"/>.</summary>
    public JobWorkerService(
        IServiceScopeFactory scopeFactory,
        JobExecutor executor,
        IOptions<JobWorkerOptions> options,
        ILogger<JobWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _executor = executor;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("JobWorkerService started (poll={Poll}s, maxConcurrent={Max})",
            _options.PollInterval.TotalSeconds, _options.MaxConcurrentJobs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await _executor.ListJobsAsync(stoppingToken);
                var queued = jobs.Where(j => j.Status == JobStatus.Queued)
                    .Take(_options.MaxConcurrentJobs)
                    .ToList();

                foreach (var job in queued)
                {
                    await ProcessJobAsync(job, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JobWorkerService poll loop error");
            }

            await Task.Delay(_options.PollInterval, stoppingToken);
        }
    }

    private async Task ProcessJobAsync(BacktestJob job, CancellationToken stoppingToken)
    {
        await _executor.MarkRunningAsync(job.JobId, stoppingToken);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken, _executor.GetCancellationToken(job.JobId));
        var ct = linkedCts.Token;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            await DispatchAsync(job, scope.ServiceProvider, ct);
        }
        catch (OperationCanceledException)
        {
            // Job was cancelled via DELETE /jobs/{id} or host shutdown
            var current = await _executor.GetJobAsync(job.JobId, CancellationToken.None);
            if (current?.Status == JobStatus.Running)
                await _executor.MarkFailedAsync(job.JobId, "Job cancelled.", CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", job.JobId);
            await _executor.MarkFailedAsync(job.JobId, ex.Message, CancellationToken.None);
        }
    }

    private async Task DispatchAsync(BacktestJob job, IServiceProvider services, CancellationToken ct)
    {
        if (job.Config is null)
        {
            await _executor.MarkFailedAsync(job.JobId, "Job has no ScenarioConfig.", CancellationToken.None);
            return;
        }

        switch (job.JobType)
        {
            case JobType.SingleRun:
                var useCase = services.GetRequiredService<RunScenarioUseCase>();
                var runResult = await useCase.RunAsync(job.Config, ct);
                if (runResult.IsSuccess && runResult.Result is not null)
                    await _executor.MarkCompletedAsync(job.JobId, runResult.Result.RunId.ToString());
                else
                    await _executor.MarkFailedAsync(job.JobId,
                        string.Join("; ", runResult.Errors ?? Array.Empty<string>()));
                break;

            case JobType.MonteCarlo:
                var mcWorkflow = services.GetRequiredService<MonteCarloWorkflow>();
                var mcOptions = new MonteCarloOptions();
                await mcWorkflow.RunAsync(job.Config, mcOptions, ct);
                await _executor.MarkCompletedAsync(job.JobId, job.JobId);
                break;

            case JobType.WalkForward:
                var wfWorkflow = services.GetRequiredService<WalkForwardWorkflow>();
                var wfOptions = new WalkForwardOptions();
                await wfWorkflow.RunAsync(job.Config, wfOptions, ct);
                await _executor.MarkCompletedAsync(job.JobId, job.JobId);
                break;

            case JobType.ParameterSweep:
                var sweepWorkflow = services.GetRequiredService<ParameterSweepWorkflow>();
                var sweepOptions = new SweepOptions();
                await sweepWorkflow.RunAsync(job.Config, sweepOptions, ct);
                await _executor.MarkCompletedAsync(job.JobId, job.JobId);
                break;

            default:
                await _executor.MarkFailedAsync(job.JobId,
                    $"Unsupported job type: {job.JobType}", CancellationToken.None);
                break;
        }
    }
}
