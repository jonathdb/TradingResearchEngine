using System.Collections.Concurrent;
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
    private readonly IStudyRepository _studyRepo;
    private readonly ILogger<JobWorkerService> _logger;
    private readonly JobWorkerOptions _options;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private int _cleanupCounter;

    /// <summary>Creates a new <see cref="JobWorkerService"/>.</summary>
    public JobWorkerService(
        IServiceScopeFactory scopeFactory,
        JobExecutor executor,
        IStudyRepository studyRepo,
        IOptions<JobWorkerOptions> options,
        ILogger<JobWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _executor = executor;
        _studyRepo = studyRepo;
        _logger = logger;
        _options = options.Value;
        _concurrencySemaphore = new SemaphoreSlim(_options.MaxConcurrentJobs, _options.MaxConcurrentJobs);
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
                var queued = await _executor.ListQueuedJobsAsync(_options.MaxConcurrentJobs, stoppingToken);

                if (queued.Count > 0)
                {
                    var tasks = queued.Select(job => ProcessJobWithSemaphoreAsync(job, stoppingToken)).ToList();
                    await Task.WhenAll(tasks);
                }

                // Flush any pending progress snapshots
                await _executor.FlushAllProgressAsync(stoppingToken);

                // Periodic cleanup: every 60 poll cycles (~5 minutes at default 5s poll)
                if (++_cleanupCounter % 60 == 0)
                    await _executor.CleanupExpiredJobsAsync(stoppingToken);
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

    private async Task ProcessJobWithSemaphoreAsync(BacktestJob job, CancellationToken stoppingToken)
    {
        await _concurrencySemaphore.WaitAsync(stoppingToken);
        try
        {
            await ProcessJobAsync(job, stoppingToken);
        }
        finally
        {
            _concurrencySemaphore.Release();
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
        finally
        {
            // Flush progress for this job on completion
            await _executor.FlushProgressAsync(job.JobId, CancellationToken.None);
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
                var mcResult = await mcWorkflow.RunAsync(job.Config, mcOptions, ct);
                var mcStudyId = await PersistStudyResultAsync(job, StudyType.MonteCarlo, mcResult, ct);
                await _executor.MarkCompletedAsync(job.JobId, mcStudyId);
                break;

            case JobType.WalkForward:
                var wfWorkflow = services.GetRequiredService<WalkForwardWorkflow>();
                var wfOptions = new WalkForwardOptions();
                var wfResult = await wfWorkflow.RunAsync(job.Config, wfOptions, ct);
                var wfStudyId = await PersistStudyResultAsync(job, StudyType.WalkForward, wfResult, ct);
                await _executor.MarkCompletedAsync(job.JobId, wfStudyId);
                break;

            case JobType.ParameterSweep:
                var sweepWorkflow = services.GetRequiredService<ParameterSweepWorkflow>();
                var sweepOptions = new SweepOptions();
                var sweepResult = await sweepWorkflow.RunAsync(job.Config, sweepOptions, ct);
                var sweepStudyId = await PersistStudyResultAsync(job, StudyType.ParameterSweep, sweepResult, ct);
                await _executor.MarkCompletedAsync(job.JobId, sweepStudyId);
                break;

            case JobType.BenchmarkComparison:
                var benchWorkflow = services.GetRequiredService<BenchmarkComparisonWorkflow>();
                var benchOptions = new BenchmarkOptions { InitialCash = job.Config.InitialCash };
                var benchResult = await benchWorkflow.RunAsync(job.Config, benchOptions, ct);
                var benchStudyId = await PersistStudyResultAsync(job, StudyType.BenchmarkComparison, benchResult, ct);
                await _executor.MarkCompletedAsync(job.JobId, benchStudyId);
                break;

            case JobType.Variance:
                var varWorkflow = services.GetRequiredService<VarianceTestingWorkflow>();
                var varOptions = new VarianceOptions();
                var varResult = await varWorkflow.RunAsync(job.Config, varOptions, ct);
                var varStudyId = await PersistStudyResultAsync(job, StudyType.Variance, varResult, ct);
                await _executor.MarkCompletedAsync(job.JobId, varStudyId);
                break;

            case JobType.RandomisedOos:
                var oosWorkflow = services.GetRequiredService<RandomizedOosWorkflow>();
                var oosOptions = new RandomizedOosOptions();
                var oosResult = await oosWorkflow.RunAsync(job.Config, oosOptions, ct);
                var oosStudyId = await PersistStudyResultAsync(job, StudyType.RandomisedOos, oosResult, ct);
                await _executor.MarkCompletedAsync(job.JobId, oosStudyId);
                break;

            default:
                await _executor.MarkFailedAsync(job.JobId,
                    $"Unsupported job type: {job.JobType}", CancellationToken.None);
                break;
        }
    }

    /// <summary>
    /// Persists a workflow result as a study record and returns the study ID for result linkage.
    /// </summary>
    private async Task<string> PersistStudyResultAsync<TResult>(
        BacktestJob job, StudyType studyType, TResult result, CancellationToken ct)
    {
        var studyId = Guid.NewGuid().ToString("N");
        var study = new StudyRecord(
            StudyId: studyId,
            StrategyVersionId: job.Config?.StrategyVersionId ?? "",
            Type: studyType,
            Status: StudyStatus.Completed,
            CreatedAt: DateTimeOffset.UtcNow);
        await _studyRepo.SaveAsync(study, ct);

        var resultJson = System.Text.Json.JsonSerializer.Serialize(result);
        await _studyRepo.SaveResultAsync(studyId, resultJson, ct);

        return studyId;
    }
}
