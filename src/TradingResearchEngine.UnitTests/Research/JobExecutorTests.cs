using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.UnitTests.Research;

public class JobExecutorTests
{
    private readonly Mock<IRepository<BacktestJob>> _repoMock;
    private readonly JobExecutor _sut;

    public JobExecutorTests()
    {
        _repoMock = new Mock<IRepository<BacktestJob>>();
        _sut = new JobExecutor(_repoMock.Object, NullLogger<JobExecutor>.Instance);
    }

    private static ScenarioConfig MakeConfig(string id = "test-scenario") =>
        new(
            ScenarioId: id,
            Description: "Test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>(),
            StrategyType: "test-strategy",
            StrategyParameters: new Dictionary<string, object>(),
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: "zero",
            CommissionModelType: "zero",
            InitialCash: 100_000m,
            AnnualRiskFreeRate: 0.02m,
            RandomSeed: null,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null);

    // ─── EnqueueAsync / SubmitAsync ───────────────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_ReturnsJobIdImmediately_WithoutAwaitingBacktest()
    {
        // Arrange
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var jobId = await _sut.EnqueueAsync(JobType.SingleRun, MakeConfig());

        // Assert — returns a non-empty job ID
        Assert.False(string.IsNullOrWhiteSpace(jobId));
        // The job was persisted as Queued (not Running/Completed)
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j => j.JobId == jobId && j.Status == JobStatus.Queued),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnqueueAsync_PersistsJobWithCorrectFields()
    {
        // Arrange
        var config = MakeConfig("my-scenario");
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var jobId = await _sut.EnqueueAsync(JobType.MonteCarlo, config);

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j =>
                j.JobId == jobId &&
                j.JobType == JobType.MonteCarlo &&
                j.Status == JobStatus.Queued &&
                j.Config == config &&
                j.StartedAt == null &&
                j.CompletedAt == null &&
                j.ResultId == null &&
                j.ErrorMessage == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Status Transitions: Queued → Running → Completed ────────────────────

    [Fact]
    public async Task MarkRunningAsync_TransitionsJobToRunning()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-1",
            JobType: JobType.SingleRun,
            Status: JobStatus.Queued,
            SubmittedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync("job-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.MarkRunningAsync("job-1");

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j => j.JobId == "job-1" && j.Status == JobStatus.Running && j.StartedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkCompletedAsync_TransitionsJobToCompleted_WithResultId()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-2",
            JobType: JobType.SingleRun,
            Status: JobStatus.Running,
            SubmittedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync("job-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.MarkCompletedAsync("job-2", "result-abc");

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j =>
                j.JobId == "job-2" &&
                j.Status == JobStatus.Completed &&
                j.ResultId == "result-abc" &&
                j.CompletedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Status Transitions: Queued → Running → Failed ───────────────────────

    [Fact]
    public async Task MarkFailedAsync_TransitionsJobToFailed_WithErrorMessage()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-3",
            JobType: JobType.SingleRun,
            Status: JobStatus.Running,
            SubmittedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync("job-3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.MarkFailedAsync("job-3", "Strategy not found in registry.");

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j =>
                j.JobId == "job-3" &&
                j.Status == JobStatus.Failed &&
                j.ErrorMessage == "Strategy not found in registry." &&
                j.CompletedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── GetStatusAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatusAsync_ReturnsCurrentStatus()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-4",
            JobType: JobType.SingleRun,
            Status: JobStatus.Running,
            SubmittedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync("job-4", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var status = await _sut.GetStatusAsync("job-4");

        // Assert
        Assert.Equal(JobStatus.Running, status);
    }

    [Fact]
    public async Task GetStatusAsync_NonExistentJob_ReturnsNull()
    {
        // Arrange
        _repoMock.Setup(r => r.GetByIdAsync("missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestJob?)null);

        // Act
        var status = await _sut.GetStatusAsync("missing");

        // Assert
        Assert.Null(status);
    }

    // ─── GetResultIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetResultIdAsync_CompletedJob_ReturnsResultId()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-5",
            JobType: JobType.SingleRun,
            Status: JobStatus.Completed,
            SubmittedAt: DateTimeOffset.UtcNow,
            ResultId: "result-xyz");

        _repoMock.Setup(r => r.GetByIdAsync("job-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var resultId = await _sut.GetResultIdAsync("job-5");

        // Assert
        Assert.Equal("result-xyz", resultId);
    }

    [Fact]
    public async Task GetResultIdAsync_RunningJob_ReturnsNull()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-6",
            JobType: JobType.SingleRun,
            Status: JobStatus.Running,
            SubmittedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync("job-6", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var resultId = await _sut.GetResultIdAsync("job-6");

        // Assert
        Assert.Null(resultId);
    }

    // ─── GetErrorAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetErrorAsync_FailedJob_ReturnsErrorMessage()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-7",
            JobType: JobType.SingleRun,
            Status: JobStatus.Failed,
            SubmittedAt: DateTimeOffset.UtcNow,
            ErrorMessage: "Data provider timeout.");

        _repoMock.Setup(r => r.GetByIdAsync("job-7", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var error = await _sut.GetErrorAsync("job-7");

        // Assert
        Assert.Equal("Data provider timeout.", error);
    }

    [Fact]
    public async Task GetErrorAsync_CompletedJob_ReturnsNull()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-8",
            JobType: JobType.SingleRun,
            Status: JobStatus.Completed,
            SubmittedAt: DateTimeOffset.UtcNow,
            ResultId: "result-ok");

        _repoMock.Setup(r => r.GetByIdAsync("job-8", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);

        // Act
        var error = await _sut.GetErrorAsync("job-8");

        // Assert
        Assert.Null(error);
    }

    // ─── CleanupExpiredJobsAsync ─────────────────────────────────────────────

    [Fact]
    public async Task CleanupExpiredJobsAsync_RemovesJobsOlderThan24Hours()
    {
        // Arrange
        var expiredJob = new BacktestJob(
            JobId: "old-job",
            JobType: JobType.SingleRun,
            Status: JobStatus.Completed,
            SubmittedAt: DateTimeOffset.UtcNow.AddHours(-48),
            CompletedAt: DateTimeOffset.UtcNow.AddHours(-25));

        var recentJob = new BacktestJob(
            JobId: "new-job",
            JobType: JobType.SingleRun,
            Status: JobStatus.Completed,
            SubmittedAt: DateTimeOffset.UtcNow.AddHours(-2),
            CompletedAt: DateTimeOffset.UtcNow.AddHours(-1));

        _repoMock.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestJob> { expiredJob, recentJob });
        _repoMock.Setup(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var removed = await _sut.CleanupExpiredJobsAsync();

        // Assert
        Assert.Equal(1, removed);
        _repoMock.Verify(r => r.DeleteAsync("old-job", It.IsAny<CancellationToken>()), Times.Once);
        _repoMock.Verify(r => r.DeleteAsync("new-job", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CleanupExpiredJobsAsync_NoExpiredJobs_ReturnsZero()
    {
        // Arrange
        var recentJob = new BacktestJob(
            JobId: "recent",
            JobType: JobType.SingleRun,
            Status: JobStatus.Completed,
            SubmittedAt: DateTimeOffset.UtcNow,
            CompletedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestJob> { recentJob });

        // Act
        var removed = await _sut.CleanupExpiredJobsAsync();

        // Assert
        Assert.Equal(0, removed);
        _repoMock.Verify(r => r.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── MarkRunningAsync with non-existent job ──────────────────────────────

    [Fact]
    public async Task MarkRunningAsync_NonExistentJob_DoesNotThrow()
    {
        // Arrange
        _repoMock.Setup(r => r.GetByIdAsync("ghost", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BacktestJob?)null);

        // Act & Assert — should not throw
        await _sut.MarkRunningAsync("ghost");

        _repoMock.Verify(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Full lifecycle integration ──────────────────────────────────────────

    [Fact]
    public async Task FullLifecycle_Queued_Running_Completed()
    {
        // Arrange — track persisted state
        BacktestJob? persisted = null;
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Callback<BacktestJob, CancellationToken>((j, _) => persisted = j)
            .Returns(Task.CompletedTask);
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => persisted);

        // Act — submit
        var jobId = await _sut.EnqueueAsync(JobType.SingleRun, MakeConfig());
        Assert.Equal(JobStatus.Queued, persisted!.Status);

        // Act — mark running
        await _sut.MarkRunningAsync(jobId);
        Assert.Equal(JobStatus.Running, persisted.Status);
        Assert.NotNull(persisted.StartedAt);

        // Act — mark completed
        await _sut.MarkCompletedAsync(jobId, "result-final");
        Assert.Equal(JobStatus.Completed, persisted.Status);
        Assert.Equal("result-final", persisted.ResultId);
        Assert.NotNull(persisted.CompletedAt);
    }

    [Fact]
    public async Task FullLifecycle_Queued_Running_Failed()
    {
        // Arrange — track persisted state
        BacktestJob? persisted = null;
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Callback<BacktestJob, CancellationToken>((j, _) => persisted = j)
            .Returns(Task.CompletedTask);
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => persisted);

        // Act — submit
        var jobId = await _sut.EnqueueAsync(JobType.WalkForward, MakeConfig());
        Assert.Equal(JobStatus.Queued, persisted!.Status);

        // Act — mark running
        await _sut.MarkRunningAsync(jobId);
        Assert.Equal(JobStatus.Running, persisted.Status);

        // Act — mark failed
        await _sut.MarkFailedAsync(jobId, "Out of memory.");
        Assert.Equal(JobStatus.Failed, persisted.Status);
        Assert.Equal("Out of memory.", persisted.ErrorMessage);
        Assert.NotNull(persisted.CompletedAt);
    }

    // ─── MarkRetryingAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task MarkRetryingAsync_TransitionsJobToRetrying_WithRetryCount()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-retry-1",
            JobType: JobType.MonteCarlo,
            Status: JobStatus.Running,
            SubmittedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync("job-retry-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.MarkRetryingAsync("job-retry-1", 2);

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j =>
                j.JobId == "job-retry-1" &&
                j.Status == JobStatus.Retrying &&
                j.RetryCount == 2 &&
                j.FailureType == JobFailureType.Transient),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── MarkFailedWithTypeAsync ─────────────────────────────────────────────

    [Fact]
    public async Task MarkFailedWithTypeAsync_TransitionsToFailed_WithFailureTypeAndRetryCount()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-fail-type",
            JobType: JobType.SingleRun,
            Status: JobStatus.Running,
            SubmittedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync("job-fail-type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.MarkFailedWithTypeAsync("job-fail-type", "Network error after retries.", JobFailureType.Transient, 3);

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j =>
                j.JobId == "job-fail-type" &&
                j.Status == JobStatus.Failed &&
                j.ErrorMessage == "Network error after retries." &&
                j.FailureType == JobFailureType.Transient &&
                j.RetryCount == 3 &&
                j.CompletedAt != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MarkFailedWithTypeAsync_TerminalFailure_SetsTerminalType()
    {
        // Arrange
        var job = new BacktestJob(
            JobId: "job-terminal",
            JobType: JobType.SingleRun,
            Status: JobStatus.Running,
            SubmittedAt: DateTimeOffset.UtcNow,
            StartedAt: DateTimeOffset.UtcNow);

        _repoMock.Setup(r => r.GetByIdAsync("job-terminal", It.IsAny<CancellationToken>()))
            .ReturnsAsync(job);
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.MarkFailedWithTypeAsync("job-terminal", "Invalid configuration.", JobFailureType.Terminal, 0);

        // Assert
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j =>
                j.FailureType == JobFailureType.Terminal &&
                j.RetryCount == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── RecoverOrphanedJobsAsync with Retrying status ───────────────────────

    [Fact]
    public async Task RecoverOrphanedJobsAsync_RecoversRetryingJobs()
    {
        // Arrange
        var retryingJob = new BacktestJob(
            JobId: "job-retrying",
            JobType: JobType.MonteCarlo,
            Status: JobStatus.Retrying,
            SubmittedAt: DateTimeOffset.UtcNow,
            RetryCount: 2,
            FailureType: JobFailureType.Transient);

        _repoMock.Setup(r => r.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<BacktestJob> { retryingJob });
        _repoMock.Setup(r => r.SaveAsync(It.IsAny<BacktestJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.RecoverOrphanedJobsAsync();

        // Assert — retrying jobs are recovered to Failed
        _repoMock.Verify(r => r.SaveAsync(
            It.Is<BacktestJob>(j =>
                j.JobId == "job-retrying" &&
                j.Status == JobStatus.Failed &&
                j.ErrorMessage == "Process restarted; job was not completed."),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
