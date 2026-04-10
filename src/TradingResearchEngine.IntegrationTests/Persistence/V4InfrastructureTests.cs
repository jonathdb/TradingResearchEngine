using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Infrastructure.Export;
using TradingResearchEngine.Infrastructure.Persistence;

namespace TradingResearchEngine.IntegrationTests.Persistence;

public class V4InfrastructureTests : IDisposable
{
    private readonly string _tempDir;

    public V4InfrastructureTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-v4-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // --- JsonDataFileRepository ---

    [Fact]
    public async Task JsonDataFileRepository_CRUD()
    {
        var repo = new JsonDataFileRepository(Path.Combine(_tempDir, "datafiles"));

        var record = new DataFileRecord(
            "file-1", "EURUSD_Daily.csv", "/data/EURUSD_Daily.csv",
            "EURUSD", "Daily",
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero),
            2515, ValidationStatus.Valid, null, DateTimeOffset.UtcNow);

        // Save
        await repo.SaveAsync(record);

        // Get
        var loaded = await repo.GetAsync("file-1");
        Assert.NotNull(loaded);
        Assert.Equal("EURUSD", loaded.DetectedSymbol);
        Assert.Equal(2515, loaded.BarCount);

        // List
        var all = await repo.ListAsync();
        Assert.Single(all);

        // Delete
        await repo.DeleteAsync("file-1");
        Assert.Null(await repo.GetAsync("file-1"));
    }

    // --- MigrationService ---

    [Fact]
    public async Task MigrationService_OrphanedResults_LinkedToImported()
    {
        var resultDir = Path.Combine(_tempDir, "results");
        var strategyDir = Path.Combine(_tempDir, "strategies");
        var lockDir = Path.Combine(_tempDir, "lock");

        var resultRepo = new JsonFileRepository<BacktestResult>(
            Options.Create(new RepositoryOptions { BaseDirectory = resultDir }));
        var strategyRepo = new JsonStrategyRepository(strategyDir);

        // Save an orphaned result (no StrategyVersionId)
        var orphan = MakeResult();
        await resultRepo.SaveAsync(orphan);

        var migration = new MigrationService(
            resultRepo, strategyRepo,
            NullLogger<MigrationService>.Instance, lockDir);

        await migration.MigrateIfNeededAsync();

        // Verify the result is now linked
        var loaded = await resultRepo.GetByIdAsync(orphan.Id);
        Assert.NotNull(loaded);
        Assert.Equal("imported-pre-v4-v0", loaded.StrategyVersionId);

        // Verify the synthetic strategy was created
        var strategy = await strategyRepo.GetAsync("imported-pre-v4");
        Assert.NotNull(strategy);
        Assert.Equal("Imported (Pre-V4)", strategy.StrategyName);
    }

    [Fact]
    public async Task MigrationService_LockFile_SkipsSecondRun()
    {
        var resultDir = Path.Combine(_tempDir, "results2");
        var strategyDir = Path.Combine(_tempDir, "strategies2");
        var lockDir = Path.Combine(_tempDir, "lock2");

        var resultRepo = new JsonFileRepository<BacktestResult>(
            Options.Create(new RepositoryOptions { BaseDirectory = resultDir }));
        var strategyRepo = new JsonStrategyRepository(strategyDir);

        var orphan = MakeResult();
        await resultRepo.SaveAsync(orphan);

        var migration = new MigrationService(
            resultRepo, strategyRepo,
            NullLogger<MigrationService>.Instance, lockDir);

        // First run
        await migration.MigrateIfNeededAsync();
        Assert.True(File.Exists(Path.Combine(lockDir, "migration_v4.lock")));

        // Save another orphan
        var orphan2 = MakeResult();
        await resultRepo.SaveAsync(orphan2);

        // Second run should skip
        await migration.MigrateIfNeededAsync();

        // orphan2 should NOT be linked (migration skipped)
        var loaded2 = await resultRepo.GetByIdAsync(orphan2.Id);
        Assert.NotNull(loaded2);
        Assert.Null(loaded2.StrategyVersionId);
    }

    // --- ReportExporter ---

    [Fact]
    public async Task ExportMarkdown_RoundTrip_ValidContent()
    {
        var exportDir = Path.Combine(_tempDir, "exports");
        var exporter = new ReportExporter(exportDir);
        var result = MakeResult() with
        {
            DeflatedSharpeRatio = 1.12m,
            TrialCount = 5
        };

        var path = await exporter.ExportMarkdownAsync(result);

        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("# Run Report:", content);
        Assert.Contains("DSR", content);
        Assert.Contains("Key Metrics", content);
    }

    [Fact]
    public async Task ExportJson_RoundTrips_WithoutDataLoss()
    {
        var exportDir = Path.Combine(_tempDir, "exports-json");
        var exporter = new ReportExporter(exportDir);
        var result = MakeResult() with
        {
            FailureDetail = null,
            DeflatedSharpeRatio = 0.98m,
            TrialCount = 3
        };

        var path = await exporter.ExportJsonAsync(result);
        Assert.True(File.Exists(path));

        var json = File.ReadAllText(path);
        var deserialized = JsonSerializer.Deserialize<BacktestResult>(json,
            new JsonSerializerOptions { WriteIndented = true });

        Assert.NotNull(deserialized);
        Assert.Equal(result.RunId, deserialized.RunId);
        Assert.Equal(result.DeflatedSharpeRatio, deserialized.DeflatedSharpeRatio);
        Assert.Equal(result.TrialCount, deserialized.TrialCount);
        Assert.Equal(result.EndEquity, deserialized.EndEquity);
    }

    // --- Helper ---

    private static BacktestResult MakeResult() =>
        new(Guid.NewGuid(),
            new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
                null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint>(),
            new List<ClosedTrade>(),
            100_000m, 110_000m, 0.05m,
            1.42m, null, null, null, 23,
            0.61m, 1.87m, 500m, -300m, 142m, null, null, 3, 5, 1200);
}
