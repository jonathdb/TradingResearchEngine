using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Infrastructure.Persistence;

namespace TradingResearchEngine.IntegrationTests.Persistence;

public class ConsistencyReconcilerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _jsonDir;
    private readonly string _indexDbPath;
    private readonly Mock<ILogger<ConsistencyReconciler>> _loggerMock;
    private readonly ConsistencyReconciler _reconciler;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ConsistencyReconcilerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tre-reconciler-test-{Guid.NewGuid():N}");
        _jsonDir = Path.Combine(_tempDir, "json");
        var indexDir = Path.Combine(_tempDir, "index");
        Directory.CreateDirectory(_jsonDir);
        Directory.CreateDirectory(indexDir);
        _indexDbPath = Path.Combine(indexDir, "index.db");

        _loggerMock = new Mock<ILogger<ConsistencyReconciler>>();

        var options = Options.Create(new RepositoryOptions
        {
            BaseDirectory = _jsonDir,
            IndexDbPath = _indexDbPath
        });
        _reconciler = new ConsistencyReconciler(options, _loggerMock.Object);
    }

    public void Dispose()
    {
        // Clear SQLite connection pool to release file locks before cleanup
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task StartAsync_ConsistentState_LogsNoCorrectiveActions()
    {
        // Arrange: create matching JSON files and index entries
        var result = MakeResult();
        await WriteJsonFile(result);
        await CreateIndexWithEntries(result);

        // Act
        await _reconciler.StartAsync(CancellationToken.None);

        // Assert: no warning-level logs about adding or removing
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("added missing entry")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("removed orphaned entry")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }

    [Fact]
    public async Task StartAsync_MissingFromIndex_AddsEntryToIndex()
    {
        // Arrange: JSON file exists but no index entry
        var result = MakeResult();
        await WriteJsonFile(result);
        await CreateEmptyIndex();

        // Act
        await _reconciler.StartAsync(CancellationToken.None);

        // Assert: entry was added to the index
        var indexIds = await GetIndexIds();
        Assert.Contains(result.Id, indexIds);
    }

    [Fact]
    public async Task StartAsync_OrphanedInIndex_RemovesFromIndex()
    {
        // Arrange: index has an entry but no corresponding JSON file
        var result = MakeResult();
        await CreateIndexWithEntries(result);
        // Don't write the JSON file — this makes it orphaned

        // Act
        await _reconciler.StartAsync(CancellationToken.None);

        // Assert: orphaned entry was removed
        var indexIds = await GetIndexIds();
        Assert.DoesNotContain(result.Id, indexIds);
    }

    [Fact]
    public async Task StartAsync_JsonStoreIsSourceOfTruth_NoDataLoss()
    {
        // Arrange: multiple JSON files, some in index, some not
        var existing = MakeResult();
        var missing = MakeResult();
        var orphanId = Guid.NewGuid().ToString();

        await WriteJsonFile(existing);
        await WriteJsonFile(missing);
        await CreateIndexWithEntries(existing);
        await AddOrphanToIndex(orphanId);

        // Act
        await _reconciler.StartAsync(CancellationToken.None);

        // Assert: both JSON-backed entries are in the index
        var indexIds = await GetIndexIds();
        Assert.Contains(existing.Id, indexIds);
        Assert.Contains(missing.Id, indexIds);
        // Orphan is removed
        Assert.DoesNotContain(orphanId, indexIds);

        // JSON files are untouched (no data loss)
        Assert.True(File.Exists(Path.Combine(_jsonDir, $"{existing.Id}.json")));
        Assert.True(File.Exists(Path.Combine(_jsonDir, $"{missing.Id}.json")));
    }

    [Fact]
    public async Task StartAsync_NoJsonDirectory_SkipsGracefully()
    {
        // Arrange: delete the JSON directory
        Directory.Delete(_jsonDir, true);

        // Act & Assert: no exception
        await _reconciler.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_NoIndexDb_SkipsGracefully()
    {
        // Arrange: JSON exists but no index DB
        var result = MakeResult();
        await WriteJsonFile(result);
        // Don't create the index DB

        // Act & Assert: no exception
        await _reconciler.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StartAsync_LogsStructuredDiagnostics()
    {
        // Arrange: one missing, one orphaned
        var missing = MakeResult();
        var orphanId = Guid.NewGuid().ToString();

        await WriteJsonFile(missing);
        await CreateEmptyIndex();
        await AddOrphanToIndex(orphanId);

        // Act
        await _reconciler.StartAsync(CancellationToken.None);

        // Assert: warning logs were emitted for both corrective actions
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(missing.Id)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(orphanId)),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    #region Helpers

    private async Task WriteJsonFile(BacktestResult result)
    {
        var path = Path.Combine(_jsonDir, $"{result.Id}.json");
        var json = JsonSerializer.Serialize(result, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    private async Task CreateEmptyIndex()
    {
        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=False");
        await connection.OpenAsync();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BacktestResultIndex (
                Id TEXT PRIMARY KEY,
                StrategyVersionId TEXT NOT NULL,
                StrategyId TEXT NOT NULL DEFAULT '',
                RunDate TEXT,
                Status TEXT,
                FilePath TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task CreateIndexWithEntries(params BacktestResult[] results)
    {
        await CreateEmptyIndex();

        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=False");
        await connection.OpenAsync();

        foreach (var result in results)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT OR REPLACE INTO BacktestResultIndex (Id, StrategyVersionId, StrategyId, RunDate, Status, FilePath)
                VALUES (@id, @versionId, @strategyId, @runDate, @status, @filePath)
                """;
            cmd.Parameters.AddWithValue("@id", result.Id);
            cmd.Parameters.AddWithValue("@versionId", result.StrategyVersionId ?? "");
            cmd.Parameters.AddWithValue("@strategyId", result.ScenarioConfig.StrategyType ?? "");
            cmd.Parameters.AddWithValue("@runDate", "");
            cmd.Parameters.AddWithValue("@status", result.Status.ToString());
            cmd.Parameters.AddWithValue("@filePath", Path.Combine(_jsonDir, $"{result.Id}.json"));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private async Task AddOrphanToIndex(string id)
    {
        await CreateEmptyIndex();

        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=False");
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO BacktestResultIndex (Id, StrategyVersionId, StrategyId, RunDate, Status, FilePath)
            VALUES (@id, '', '', '', 'Completed', @filePath)
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@filePath", Path.Combine(_jsonDir, $"{id}.json"));
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<HashSet<string>> GetIndexIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=False");
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id FROM BacktestResultIndex";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetString(0));
        }
        return ids;
    }

    private static BacktestResult MakeResult() =>
        new(Guid.NewGuid(),
            new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint>(),
            new List<ClosedTrade>(),
            100_000m, 105_000m, 0.05m, 1.0m, 1.0m, null, null, 10, 0.6m, 1.5m, 200m, -100m, 10m, null, null, 3, 5, 50);

    #endregion
}
