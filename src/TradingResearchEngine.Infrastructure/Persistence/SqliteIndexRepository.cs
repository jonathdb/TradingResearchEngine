using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Persistence;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// Index-only SQLite layer over existing JSON files for <see cref="BacktestResult"/>.
/// Primary store remains JSON; SQLite provides O(log n) lookups by version/strategy.
/// </summary>
public sealed class SqliteIndexRepository : IBacktestResultRepository
{
    private readonly string _jsonDir;
    private readonly string _indexDbPath;
    private readonly ILogger<SqliteIndexRepository> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <inheritdoc cref="SqliteIndexRepository"/>
    public SqliteIndexRepository(
        IOptions<RepositoryOptions> options,
        ILogger<SqliteIndexRepository> logger)
    {
        _jsonDir = string.IsNullOrWhiteSpace(options.Value.BaseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", nameof(BacktestResult))
            : options.Value.BaseDirectory;

        if (!Directory.Exists(_jsonDir))
            Directory.CreateDirectory(_jsonDir);

        var indexDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TradingResearchEngine");
        if (!Directory.Exists(indexDir))
            Directory.CreateDirectory(indexDir);

        _indexDbPath = Path.Combine(indexDir, "index.db");
        _logger = logger;
    }

    /// <summary>Scans the JSON directory and builds/rebuilds the SQLite index.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);

        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = """
            CREATE TABLE IF NOT EXISTS BacktestResultIndex (
                Id TEXT PRIMARY KEY,
                StrategyVersionId TEXT NOT NULL,
                StrategyId TEXT NOT NULL DEFAULT '',
                RunDate TEXT,
                Status TEXT,
                FilePath TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_br_version ON BacktestResultIndex(StrategyVersionId);
            CREATE INDEX IF NOT EXISTS idx_br_strategy ON BacktestResultIndex(StrategyId);
            CREATE INDEX IF NOT EXISTS idx_br_date ON BacktestResultIndex(RunDate);
            """;
        await createCmd.ExecuteNonQueryAsync(ct);

        // Rebuild: scan JSON directory and upsert all entries
        if (!Directory.Exists(_jsonDir)) return;

        var jsonFiles = Directory.GetFiles(_jsonDir, "*.json");
        _logger.LogDebug("SqliteIndex: rebuilding index from {Count} JSON files", jsonFiles.Length);

        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var file in jsonFiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var entity = JsonSerializer.Deserialize<BacktestResult>(json, JsonOptions);
                if (entity is null) continue;

                await UpsertIndexRowAsync(connection, entity, file, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SqliteIndex: failed to index {File}, skipping", file);
            }
        }
        await transaction.CommitAsync(ct);
        _logger.LogDebug("SqliteIndex: index rebuild complete — {Count} files indexed", jsonFiles.Length);
    }

    /// <inheritdoc/>
    public async Task<BacktestResult?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FilePath FROM BacktestResultIndex WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        var filePath = (string?)await cmd.ExecuteScalarAsync(ct);
        if (filePath is null) return null;

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("SqliteIndex: stale index row for {Id} — file {Path} not found, removing row", id, filePath);
            await RemoveIndexRowAsync(connection, id, ct);
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath, ct);
        return JsonSerializer.Deserialize<BacktestResult>(json, JsonOptions);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(BacktestResult entity, CancellationToken ct = default)
    {
        // Write JSON file FIRST, then upsert SQLite index row
        var filePath = Path.Combine(_jsonDir, $"{entity.Id}.json");
        var json = JsonSerializer.Serialize(entity, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct);

        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);
        await UpsertIndexRowAsync(connection, entity, filePath, ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        // Remove JSON file
        var filePath = Path.Combine(_jsonDir, $"{id}.json");
        if (File.Exists(filePath)) File.Delete(filePath);

        // Remove index row
        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);
        await RemoveIndexRowAsync(connection, id, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BacktestResult>> ListAsync(CancellationToken ct = default)
{
    var results = new List<BacktestResult>();

    await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=True");
    await connection.OpenAsync(ct);

    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT FilePath FROM BacktestResultIndex ORDER BY RunDate DESC";

    await using var reader = await cmd.ExecuteReaderAsync(ct);
    while (await reader.ReadAsync(ct))
    {
        ct.ThrowIfCancellationRequested();
        var filePath = reader.GetString(0);
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("SqliteIndex: stale index row — file {Path} not found", filePath);
            continue;
        }
        var json = await File.ReadAllTextAsync(filePath, ct);
        var entity = JsonSerializer.Deserialize<BacktestResult>(json, JsonOptions);
        if (entity is not null) results.Add(entity);
    }
    return results;
}

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BacktestResult>> ListByVersionAsync(
        string versionId, CancellationToken ct = default)
    {
        return await QueryByColumnAsync("StrategyVersionId", versionId, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BacktestResult>> ListByStrategyAsync(
        string strategyId, CancellationToken ct = default)
    {
        return await QueryByColumnAsync("StrategyId", strategyId, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BacktestResult>> GetRecentRunsAsync(int limit, CancellationToken ct = default)
    {
        var results = new List<BacktestResult>();

        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FilePath FROM BacktestResultIndex ORDER BY RunDate DESC LIMIT @limit";
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var filePath = reader.GetString(0);
            if (!File.Exists(filePath)) continue;
            var json = await File.ReadAllTextAsync(filePath, ct);
            var entity = JsonSerializer.Deserialize<BacktestResult>(json, JsonOptions);
            if (entity is not null) results.Add(entity);
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, BacktestResult>> GetLastRunPerStrategyAsync(CancellationToken ct = default)
    {
        var results = new Dictionary<string, BacktestResult>();

        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT FilePath, StrategyId FROM BacktestResultIndex
            WHERE rowid IN (
                SELECT MAX(rowid) FROM BacktestResultIndex GROUP BY StrategyId
            )
            """;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var filePath = reader.GetString(0);
            var strategyId = reader.GetString(1);
            if (!File.Exists(filePath)) continue;
            var json = await File.ReadAllTextAsync(filePath, ct);
            var entity = JsonSerializer.Deserialize<BacktestResult>(json, JsonOptions);
            if (entity is not null) results[strategyId] = entity;
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BacktestResult>> GetRunSummariesByStrategyAsync(string strategyId, CancellationToken ct = default)
    {
        return await QueryByColumnAsync("StrategyId", strategyId, ct);
    }

    private async Task<IReadOnlyList<BacktestResult>> QueryByColumnAsync(
        string column, string value, CancellationToken ct)
    {
        var results = new List<BacktestResult>();

        await using var connection = new SqliteConnection($"Data Source={_indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT FilePath FROM BacktestResultIndex WHERE {column} = @val";
        cmd.Parameters.AddWithValue("@val", value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var filePath = reader.GetString(0);
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("SqliteIndex: stale index row — file {Path} not found", filePath);
                continue;
            }

            var json = await File.ReadAllTextAsync(filePath, ct);
            var entity = JsonSerializer.Deserialize<BacktestResult>(json, JsonOptions);
            if (entity is not null) results.Add(entity);
        }

        return results;
    }

    private static async Task UpsertIndexRowAsync(
        SqliteConnection connection, BacktestResult entity, string filePath, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO BacktestResultIndex (Id, StrategyVersionId, StrategyId, RunDate, Status, FilePath)
            VALUES (@id, @versionId, @strategyId, @runDate, @status, @filePath)
            """;
        cmd.Parameters.AddWithValue("@id", entity.Id);
        cmd.Parameters.AddWithValue("@versionId", entity.StrategyVersionId ?? "");
        cmd.Parameters.AddWithValue("@strategyId", entity.ScenarioConfig.StrategyType ?? "");
        cmd.Parameters.AddWithValue("@runDate", entity.Metadata?.DataRangeStart.ToString("O") ?? "");
        cmd.Parameters.AddWithValue("@status", entity.Status.ToString());
        cmd.Parameters.AddWithValue("@filePath", filePath);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task RemoveIndexRowAsync(
        SqliteConnection connection, string id, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM BacktestResultIndex WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
