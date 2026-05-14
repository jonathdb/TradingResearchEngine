using System.Text.Json;
using TradingResearchEngine.Application.Engine;

namespace TradingResearchEngine.Infrastructure.Persistence;

/// <summary>
/// JSON-file-backed implementation of <see cref="ITestSetAuditLog"/>.
/// Stores one JSON file per strategy version under <c>{baseDir}/{versionId}.json</c>.
/// Thread-safe via <see cref="SemaphoreSlim"/>.
/// </summary>
public sealed class JsonTestSetAuditLog : ITestSetAuditLog
{
    private readonly string _baseDir;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a new <see cref="JsonTestSetAuditLog"/> persisting entries under the specified directory.
    /// </summary>
    /// <param name="baseDir">The root directory for audit log files.</param>
    public JsonTestSetAuditLog(string baseDir)
    {
        _baseDir = baseDir;
    }

    /// <inheritdoc/>
    public async Task RecordUnlockAsync(Guid versionId, string? reason, CancellationToken ct = default)
    {
        var entry = new TestSetAuditEntry(versionId, DateTimeOffset.UtcNow, TestSetAuditAction.Unlock, reason);
        await AppendEntryAsync(versionId, entry, ct);
    }

    /// <inheritdoc/>
    public async Task RecordResealAsync(Guid versionId, string? reason, CancellationToken ct = default)
    {
        var entry = new TestSetAuditEntry(versionId, DateTimeOffset.UtcNow, TestSetAuditAction.Reseal, reason);
        await AppendEntryAsync(versionId, entry, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TestSetAuditEntry>> GetEntriesAsync(Guid versionId, CancellationToken ct = default)
    {
        var filePath = GetFilePath(versionId);
        await _lock.WaitAsync(ct);
        try
        {
            if (!File.Exists(filePath))
                return Array.Empty<TestSetAuditEntry>();

            var json = await File.ReadAllTextAsync(filePath, ct);
            var entries = JsonSerializer.Deserialize<List<TestSetAuditEntry>>(json, JsonOptions);
            return entries ?? (IReadOnlyList<TestSetAuditEntry>)Array.Empty<TestSetAuditEntry>();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task AppendEntryAsync(Guid versionId, TestSetAuditEntry entry, CancellationToken ct)
    {
        var filePath = GetFilePath(versionId);
        await _lock.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_baseDir);

            List<TestSetAuditEntry> entries;
            if (File.Exists(filePath))
            {
                var json = await File.ReadAllTextAsync(filePath, ct);
                entries = JsonSerializer.Deserialize<List<TestSetAuditEntry>>(json, JsonOptions) ?? new();
            }
            else
            {
                entries = new();
            }

            entries.Add(entry);
            var updatedJson = JsonSerializer.Serialize(entries, JsonOptions);
            await File.WriteAllTextAsync(filePath, updatedJson, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string GetFilePath(Guid versionId)
        => Path.Combine(_baseDir, $"{versionId}.json");
}
