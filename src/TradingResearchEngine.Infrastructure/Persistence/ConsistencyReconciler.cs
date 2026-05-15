using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// Hosted service that reconciles the SQLite index with the JSON file store at application startup.
/// The JSON store is the source of truth: missing entries are added to the index, and orphaned
/// index entries (not backed by a JSON file) are removed.
/// </summary>
/// <remarks>
/// Runs once during startup before the application accepts requests, then completes.
/// Logs structured diagnostics for every corrective action taken.
/// </remarks>
public sealed class ConsistencyReconciler : IHostedService
{
    private readonly IOptions<RepositoryOptions> _options;
    private readonly ILogger<ConsistencyReconciler> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Creates a new instance of the consistency reconciler.
    /// </summary>
    /// <param name="options">Repository options providing the base directory for JSON files.</param>
    /// <param name="logger">Logger for structured diagnostics.</param>
    public ConsistencyReconciler(
        IOptions<RepositoryOptions> options,
        ILogger<ConsistencyReconciler> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ConsistencyReconciler: starting SQLite/JSON reconciliation");

        try
        {
            await ReconcileBacktestResultsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "ConsistencyReconciler: reconciliation failed with an unexpected error");
        }

        _logger.LogInformation("ConsistencyReconciler: reconciliation complete");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Reconciles the BacktestResult SQLite index with the JSON file store.
    /// JSON store is the source of truth.
    /// </summary>
    private async Task ReconcileBacktestResultsAsync(CancellationToken ct)
    {
        var jsonDir = ResolveJsonDirectory();
        var indexDbPath = ResolveIndexDbPath();

        if (!Directory.Exists(jsonDir))
        {
            _logger.LogDebug("ConsistencyReconciler: JSON directory {Directory} does not exist, skipping", jsonDir);
            return;
        }

        if (!File.Exists(indexDbPath))
        {
            _logger.LogDebug("ConsistencyReconciler: SQLite index {Path} does not exist, skipping (will be created on first use)", indexDbPath);
            return;
        }

        // Collect all IDs from the JSON store (file names without extension)
        var jsonIds = Directory.GetFiles(jsonDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Collect all IDs from the SQLite index
        var indexIds = await ListIndexIdsAsync(indexDbPath, ct);

        var missingInIndex = jsonIds.Except(indexIds, StringComparer.OrdinalIgnoreCase).ToList();
        var orphanedInIndex = indexIds.Except(jsonIds, StringComparer.OrdinalIgnoreCase).ToList();

        if (missingInIndex.Count == 0 && orphanedInIndex.Count == 0)
        {
            _logger.LogInformation(
                "ConsistencyReconciler: SQLite index and JSON store are consistent ({Count} entities)",
                jsonIds.Count);
            return;
        }

        _logger.LogWarning(
            "ConsistencyReconciler: detected {MissingCount} entries missing from index, {OrphanedCount} orphaned index entries",
            missingInIndex.Count, orphanedInIndex.Count);

        await using var connection = new SqliteConnection($"Data Source={indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);

        // Ensure the table exists (defensive — should already exist from SqliteIndexRepository.InitializeAsync)
        await EnsureTableExistsAsync(connection, ct);

        // Add missing entries to the index (JSON is source of truth)
        foreach (var id in missingInIndex)
        {
            ct.ThrowIfCancellationRequested();
            await AddMissingEntryAsync(connection, jsonDir, id, ct);
        }

        // Remove orphaned entries from the index
        foreach (var id in orphanedInIndex)
        {
            ct.ThrowIfCancellationRequested();
            await RemoveOrphanedEntryAsync(connection, id, ct);
        }

        _logger.LogInformation(
            "ConsistencyReconciler: reconciliation applied — {Added} entries added, {Removed} orphans removed",
            missingInIndex.Count, orphanedInIndex.Count);
    }

    /// <summary>
    /// Lists all entity IDs currently in the SQLite index.
    /// </summary>
    private static async Task<HashSet<string>> ListIndexIdsAsync(string indexDbPath, CancellationToken ct)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var connection = new SqliteConnection($"Data Source={indexDbPath};Pooling=True");
        await connection.OpenAsync(ct);

        // Check if the table exists before querying
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='BacktestResultIndex'";
        var tableName = await checkCmd.ExecuteScalarAsync(ct);
        if (tableName is null) return ids;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id FROM BacktestResultIndex";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    /// <summary>
    /// Adds a missing entry to the SQLite index by reading the JSON file and extracting index fields.
    /// </summary>
    private async Task AddMissingEntryAsync(
        SqliteConnection connection, string jsonDir, string id, CancellationToken ct)
    {
        var filePath = Path.Combine(jsonDir, $"{id}.json");
        if (!File.Exists(filePath))
        {
            _logger.LogWarning(
                "ConsistencyReconciler: expected JSON file {Path} not found during add, skipping",
                filePath);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            var entity = JsonSerializer.Deserialize<BacktestResult>(json, JsonOptions);
            if (entity is null)
            {
                _logger.LogWarning(
                    "ConsistencyReconciler: failed to deserialize {Id} from JSON, skipping",
                    id);
                return;
            }

            await UpsertIndexRowAsync(connection, entity, filePath, ct);

            _logger.LogWarning(
                "ConsistencyReconciler: added missing entry {Id} to SQLite index from JSON store (Strategy: {Strategy}, Status: {Status})",
                id, entity.ScenarioConfig.StrategyType ?? "unknown", entity.Status);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "ConsistencyReconciler: JSON deserialization error for {Id}, skipping",
                id);
        }
    }

    /// <summary>
    /// Removes an orphaned entry from the SQLite index that has no corresponding JSON file.
    /// </summary>
    private async Task RemoveOrphanedEntryAsync(
        SqliteConnection connection, string id, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM BacktestResultIndex WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(ct);

        _logger.LogWarning(
            "ConsistencyReconciler: removed orphaned entry {Id} from SQLite index (no corresponding JSON file)",
            id);
    }

    /// <summary>
    /// Upserts a row into the BacktestResultIndex table.
    /// </summary>
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

    /// <summary>
    /// Ensures the BacktestResultIndex table exists (defensive check).
    /// </summary>
    private static async Task EnsureTableExistsAsync(SqliteConnection connection, CancellationToken ct)
    {
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
            CREATE INDEX IF NOT EXISTS idx_br_version ON BacktestResultIndex(StrategyVersionId);
            CREATE INDEX IF NOT EXISTS idx_br_strategy ON BacktestResultIndex(StrategyId);
            CREATE INDEX IF NOT EXISTS idx_br_date ON BacktestResultIndex(RunDate);
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Resolves the JSON directory for BacktestResult entities.
    /// </summary>
    private string ResolveJsonDirectory()
    {
        var baseDir = _options.Value.BaseDirectory;
        return string.IsNullOrWhiteSpace(baseDir)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", nameof(BacktestResult))
            : baseDir;
    }

    /// <summary>
    /// Resolves the SQLite index database path.
    /// </summary>
    private string ResolveIndexDbPath()
    {
        var configured = _options.Value.IndexDbPath;
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TradingResearchEngine", "index.db")
            : configured;
    }
}
