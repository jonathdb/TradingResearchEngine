using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Application.MarketData;

/// <summary>Progress update from a running import.</summary>
public sealed record ImportProgressUpdate(
    string ImportId, int Current, int Total, string Label);

/// <summary>Completion notification from a finished import.</summary>
public sealed record ImportCompletionUpdate(
    string ImportId, MarketDataImportStatus Status, string? ErrorMessage);

/// <summary>Snapshot of the currently active import.</summary>
public sealed record ActiveImport(
    string ImportId, string Source, string Symbol, string Timeframe,
    int Current, int Total, DateTimeOffset StartedAt);

/// <summary>
/// Orchestrates the full market data import lifecycle:
/// validate → download → normalize → register DataFileRecord → update import record.
/// Singleton. Only one import may run at a time.
/// </summary>
public sealed class MarketDataImportService : IDisposable
{
    private readonly IMarketDataImportRepository _importRepo;
    private readonly IDataFileRepository _dataFileRepo;
    private readonly IEnumerable<IMarketDataProvider> _providers;
    private readonly ILogger<MarketDataImportService> _logger;
    private readonly string _dataDirectory;

    private CancellationTokenSource? _activeCts;
    private ActiveImport? _activeImport;
    private readonly object _lock = new();

    /// <summary>Raised on each progress step of a running import.</summary>
    public event Action<ImportProgressUpdate>? OnProgress;

    /// <summary>Raised when an import completes (success, failure, or cancellation).</summary>
    public event Action<ImportCompletionUpdate>? OnCompleted;

    /// <summary>Creates the import service.</summary>
    public MarketDataImportService(
        IMarketDataImportRepository importRepo,
        IDataFileRepository dataFileRepo,
        IEnumerable<IMarketDataProvider> providers,
        ILogger<MarketDataImportService> logger,
        string dataDirectory)
    {
        _importRepo = importRepo;
        _dataFileRepo = dataFileRepo;
        _providers = providers;
        _logger = logger;
        _dataDirectory = dataDirectory;
    }

    /// <summary>
    /// Validates the request, creates a Running import record, launches the background
    /// download, and returns the import ID immediately.
    /// </summary>
    /// <exception cref="InvalidOperationException">An import is already running.</exception>
    /// <exception cref="ArgumentException">Invalid request parameters.</exception>
    public async Task<string> StartImportAsync(
        string source, string symbol, string timeframe,
        DateTimeOffset requestedStart, DateTimeOffset requestedEnd,
        CancellationToken ct = default)
    {
        // Validate
        if (requestedStart >= requestedEnd)
            throw new ArgumentException("Start date must be before end date.");

        var provider = _providers.FirstOrDefault(p =>
            p.SourceName.Equals(source, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown source: {source}");

        var symbols = await provider.GetSupportedSymbolsAsync(ct);
        if (!symbols.Any(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Unsupported symbol: {symbol}");

        lock (_lock)
        {
            if (_activeImport is not null)
                throw new InvalidOperationException("An import is already in progress.");
        }

        var importId = $"imp-{Guid.NewGuid():N}"[..16];
        var outputFileName = $"{source.ToLowerInvariant()}_{symbol}_{timeframe}_{requestedStart:yyyyMMdd}_{requestedEnd:yyyyMMdd}.csv";
        var outputPath = Path.Combine(_dataDirectory, outputFileName);
        var tempPath = outputPath + ".tmp";

        var record = new MarketDataImportRecord(
            ImportId: importId,
            Source: source,
            Symbol: symbol,
            Timeframe: timeframe,
            RequestedStart: requestedStart,
            RequestedEnd: requestedEnd,
            Status: MarketDataImportStatus.Running,
            CreatedAt: DateTimeOffset.UtcNow);

        await _importRepo.SaveAsync(record, ct);

        var cts = new CancellationTokenSource();
        lock (_lock)
        {
            _activeCts = cts;
            _activeImport = new ActiveImport(importId, source, symbol, timeframe, 0, 0, DateTimeOffset.UtcNow);
        }

        // Launch background task
        _ = Task.Run(() => ExecuteImportAsync(
            importId, provider, symbol, timeframe,
            requestedStart, requestedEnd,
            outputPath, tempPath, cts.Token), CancellationToken.None);

        return importId;
    }

    /// <summary>Cancels the running import.</summary>
    public void CancelImport(string importId)
    {
        lock (_lock)
        {
            if (_activeImport?.ImportId == importId)
                _activeCts?.Cancel();
        }
    }

    /// <summary>Returns the currently running import, if any.</summary>
    public ActiveImport? GetActiveImport()
    {
        lock (_lock) { return _activeImport; }
    }

    /// <summary>
    /// Checks for an existing completed import with matching parameters.
    /// Returns the import record if found, null otherwise.
    /// </summary>
    public async Task<MarketDataImportRecord?> FindDuplicateAsync(
        string source, string symbol, string timeframe,
        DateTimeOffset requestedStart, DateTimeOffset requestedEnd,
        CancellationToken ct = default)
    {
        var all = await _importRepo.ListAsync(ct);
        return all.FirstOrDefault(r =>
            r.Status == MarketDataImportStatus.Completed &&
            r.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
            r.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase) &&
            r.Timeframe.Equals(timeframe, StringComparison.OrdinalIgnoreCase) &&
            r.RequestedStart == requestedStart &&
            r.RequestedEnd == requestedEnd);
    }

    private async Task ExecuteImportAsync(
        string importId, IMarketDataProvider provider,
        string symbol, string timeframe,
        DateTimeOffset requestedStart, DateTimeOffset requestedEnd,
        string outputPath, string tempPath, CancellationToken ct)
    {
        try
        {
            var progress = new ServiceProgressReporter(importId, this);

            var result = await provider.DownloadToFileAsync(
                symbol, timeframe, requestedStart, requestedEnd, tempPath, progress, ct);

            ct.ThrowIfCancellationRequested();

            // Atomic rename: delete existing, move temp to final
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(tempPath, outputPath);

            // Create or update DataFileRecord
            var fileId = $"df-{Guid.NewGuid():N}"[..16];

            // Check for existing DataFileRecord from a previous duplicate import
            var existingImport = await FindDuplicateAsync(
                provider.SourceName, symbol, timeframe, requestedStart, requestedEnd, CancellationToken.None);
            if (existingImport?.OutputFileId is not null)
            {
                var existingDf = await _dataFileRepo.GetAsync(existingImport.OutputFileId, CancellationToken.None);
                if (existingDf is not null)
                    fileId = existingDf.FileId;
            }

            var dataFileRecord = new DataFileRecord(
                FileId: fileId,
                FileName: Path.GetFileName(outputPath),
                FilePath: outputPath,
                DetectedSymbol: result.Symbol,
                DetectedTimeframe: result.Timeframe,
                FirstBar: result.FirstBar,
                LastBar: result.LastBar,
                BarCount: result.BarCount,
                ValidationStatus: result.BarCount > 0 ? ValidationStatus.Valid : ValidationStatus.Invalid,
                ValidationError: result.BarCount == 0 ? "No bars in the requested range" : null,
                AddedAt: DateTimeOffset.UtcNow);

            await _dataFileRepo.SaveAsync(dataFileRecord, CancellationToken.None);

            var updated = (await _importRepo.GetAsync(importId, CancellationToken.None))! with
            {
                Status = MarketDataImportStatus.Completed,
                OutputFilePath = outputPath,
                OutputFileId = fileId,
                CompletedAt = DateTimeOffset.UtcNow
            };
            await _importRepo.SaveAsync(updated, CancellationToken.None);

            _logger.LogInformation("Import {ImportId} completed: {BarCount} bars → {Path}",
                importId, result.BarCount, outputPath);

            OnCompleted?.Invoke(new ImportCompletionUpdate(
                importId, MarketDataImportStatus.Completed, null));
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(tempPath);
            await SetFailedOrCancelled(importId, MarketDataImportStatus.Cancelled, "Import cancelled by user.");
            OnCompleted?.Invoke(new ImportCompletionUpdate(
                importId, MarketDataImportStatus.Cancelled, "Import cancelled by user."));
        }
        catch (Exception ex)
        {
            CleanupTempFile(tempPath);
            _logger.LogError(ex, "Import {ImportId} failed", importId);
            await SetFailedOrCancelled(importId, MarketDataImportStatus.Failed, ex.Message);
            OnCompleted?.Invoke(new ImportCompletionUpdate(
                importId, MarketDataImportStatus.Failed, ex.Message));
        }
        finally
        {
            lock (_lock)
            {
                _activeImport = null;
                _activeCts?.Dispose();
                _activeCts = null;
            }
        }
    }

    private async Task SetFailedOrCancelled(string importId, MarketDataImportStatus status, string detail)
    {
        try
        {
            var record = await _importRepo.GetAsync(importId, CancellationToken.None);
            if (record is not null)
            {
                var updated = record with
                {
                    Status = status,
                    ErrorDetail = detail,
                    CompletedAt = DateTimeOffset.UtcNow
                };
                await _importRepo.SaveAsync(updated, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update import record {ImportId} to {Status}", importId, status);
        }
    }

    private static void CleanupTempFile(string tempPath)
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); }
        catch { /* best effort */ }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            _activeCts?.Cancel();
            _activeCts?.Dispose();
            _activeCts = null;
            _activeImport = null;
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Called on startup to reset orphaned Running records to Failed
    /// and clean up .tmp files in the data directory.
    /// </summary>
    public async Task RecoverOnStartupAsync(CancellationToken ct = default)
    {
        var all = await _importRepo.ListAsync(ct);
        foreach (var record in all.Where(r => r.Status == MarketDataImportStatus.Running))
        {
            var updated = record with
            {
                Status = MarketDataImportStatus.Failed,
                ErrorDetail = "Interrupted by application restart",
                CompletedAt = DateTimeOffset.UtcNow
            };
            await _importRepo.SaveAsync(updated, ct);
            _logger.LogWarning("Import {ImportId} reset to Failed (interrupted by restart)", record.ImportId);
        }

        // Clean up orphaned .tmp files
        try
        {
            if (Directory.Exists(_dataDirectory))
            {
                foreach (var tmpFile in Directory.GetFiles(_dataDirectory, "*.tmp"))
                {
                    File.Delete(tmpFile);
                    _logger.LogInformation("Deleted orphaned temp file: {Path}", tmpFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up temp files in {Dir}", _dataDirectory);
        }
    }

    /// <summary>Internal progress reporter that bridges provider callbacks to service events.</summary>
    private sealed class ServiceProgressReporter : IProgressReporter
    {
        private readonly string _importId;
        private readonly MarketDataImportService _service;

        public ServiceProgressReporter(string importId, MarketDataImportService service)
        {
            _importId = importId;
            _service = service;
        }

        public void Report(int current, int total, string label)
        {
            lock (_service._lock)
            {
                if (_service._activeImport?.ImportId == _importId)
                    _service._activeImport = _service._activeImport with { Current = current, Total = total };
            }
            _service.OnProgress?.Invoke(new ImportProgressUpdate(_importId, current, total, label));
        }

        public void Report(ProgressSnapshot snapshot)
            => Report(snapshot.Current, snapshot.Total, snapshot.CurrentItemLabel ?? snapshot.Stage);
    }
}
