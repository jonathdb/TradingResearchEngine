using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TradingResearchEngine.Application.TickImport;

/// <summary>
/// Orchestrates the tick data download lifecycle:
/// validate → detect coverage → build work queue → download concurrently → cache → complete.
/// Singleton. Only one tick import may run at a time.
/// </summary>
public sealed class TickImportService : IDisposable
{
    private readonly ITickCacheService _cacheService;
    private readonly ITickImportRepository _importRepository;
    private readonly ITickDownloader _downloader;
    private readonly TickImportOptions _options;
    private readonly ILogger<TickImportService> _logger;

    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private ActiveTickImport? _activeImport;
    private bool _disposed;

    /// <summary>Raised on each progress step of a running import.</summary>
    public event Action<TickImportProgressUpdate>? OnProgress;

    /// <summary>Raised when an import completes (success, failure, or cancellation).</summary>
    public event Action<TickImportCompletionUpdate>? OnCompleted;

    /// <summary>Initializes a new instance of <see cref="TickImportService"/>.</summary>
    public TickImportService(
        ITickCacheService cacheService,
        ITickImportRepository importRepository,
        ITickDownloader downloader,
        IOptions<TickImportOptions> options,
        ILogger<TickImportService> logger)
    {
        _cacheService = cacheService;
        _importRepository = importRepository;
        _downloader = downloader;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Validates the request, detects existing coverage, creates a Running record,
    /// and launches the background download.
    /// </summary>
    /// <param name="symbol">The trading symbol to import.</param>
    /// <param name="requestedStart">Start of the requested date range.</param>
    /// <param name="requestedEnd">End of the requested date range.</param>
    /// <param name="ct">Cancellation token for the initial setup phase.</param>
    /// <returns>The import ID of the created import record.</returns>
    /// <exception cref="ArgumentException">Thrown when start >= end or symbol is unsupported.</exception>
    /// <exception cref="InvalidOperationException">Thrown when an import is already running.</exception>
    public async Task<string> StartTickImportAsync(
        string symbol, DateTimeOffset requestedStart, DateTimeOffset requestedEnd,
        CancellationToken ct = default)
    {
        // Validate: start < end
        if (requestedStart >= requestedEnd)
            throw new ArgumentException("Requested start must be before requested end.");

        // Validate: symbol in supported list
        if (!_downloader.SupportedSymbols.Contains(symbol.ToUpperInvariant()))
            throw new ArgumentException($"Symbol '{symbol}' is not supported.");

        // Check if import already running
        lock (_lock)
        {
            if (_activeImport is not null)
                throw new InvalidOperationException("A tick import is already in progress.");
        }

        var normalizedSymbol = symbol.ToUpperInvariant();
        var startDate = requestedStart.UtcDateTime.Date;
        var endDate = requestedEnd.UtcDateTime.Date;

        // Detect missing days
        var missingDays = await _cacheService.GetMissingDaysAsync(normalizedSymbol, startDate, endDate, ct);

        var importId = $"tick-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        if (missingDays.Count == 0)
        {
            // All days cached → complete immediately
            var tickCount = await _cacheService.GetTickCountAsync(normalizedSymbol, startDate, endDate, ct);
            var completedRecord = new TickImportRecord(
                ImportId: importId,
                Source: "Dukascopy",
                Symbol: normalizedSymbol,
                RequestedStart: requestedStart,
                RequestedEnd: requestedEnd,
                Status: TickImportStatus.Completed,
                TotalTickCount: tickCount,
                CreatedAt: now,
                CompletedAt: now);

            await _importRepository.SaveAsync(completedRecord, ct);

            _logger.LogInformation(
                "Import {ImportId} for {Symbol} completed immediately — all days cached ({TickCount} ticks)",
                importId, normalizedSymbol, tickCount);

            OnCompleted?.Invoke(new TickImportCompletionUpdate(importId, TickImportStatus.Completed, null));
            return importId;
        }

        // Create Running record
        var record = new TickImportRecord(
            ImportId: importId,
            Source: "Dukascopy",
            Symbol: normalizedSymbol,
            RequestedStart: requestedStart,
            RequestedEnd: requestedEnd,
            Status: TickImportStatus.Running,
            CreatedAt: now);

        await _importRepository.SaveAsync(record, ct);

        // Set up active import tracking
        var cts = new CancellationTokenSource();

        lock (_lock)
        {
            _cts = cts;
            _activeImport = new ActiveTickImport(importId, normalizedSymbol, 0, 0, now);
        }

        _logger.LogInformation(
            "Starting tick import {ImportId} for {Symbol} ({MissingDays} missing days)",
            importId, normalizedSymbol, missingDays.Count);

        // Launch background download
        _ = Task.Run(() => ExecuteDownloadAsync(
            importId, normalizedSymbol, startDate, endDate, missingDays, cts.Token), CancellationToken.None);

        return importId;
    }

    /// <summary>Cancels the running tick import.</summary>
    /// <param name="importId">The import ID to cancel.</param>
    public void CancelImport(string importId)
    {
        lock (_lock)
        {
            if (_activeImport?.ImportId != importId)
                return;

            _cts?.Cancel();
        }
    }

    /// <summary>Returns the currently running import, if any.</summary>
    public ActiveTickImport? GetActiveImport()
    {
        lock (_lock)
        {
            return _activeImport;
        }
    }

    /// <summary>Resets orphaned Running records to Failed on startup.</summary>
    public async Task RecoverOnStartupAsync(CancellationToken ct = default)
    {
        var allRecords = await _importRepository.ListAsync(ct);
        foreach (var record in allRecords)
        {
            if (record.Status == TickImportStatus.Running)
            {
                var failedRecord = record with
                {
                    Status = TickImportStatus.Failed,
                    ErrorDetail = "Interrupted by application restart",
                    CompletedAt = DateTimeOffset.UtcNow
                };
                await _importRepository.SaveAsync(failedRecord, ct);

                _logger.LogWarning(
                    "Recovered orphaned import {ImportId} — marked as Failed",
                    record.ImportId);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async Task ExecuteDownloadAsync(
        string importId,
        string symbol,
        DateTime startDate,
        DateTime endDate,
        IReadOnlyList<DateTime> missingDays,
        CancellationToken ct)
    {
        try
        {
            // Track ticks per day for batched writes
            var dayTicks = new Dictionary<DateTime, List<TickCsvRow>>();
            var progressState = new Progress<(int current, int total)>(p =>
            {
                lock (_lock)
                {
                    if (_activeImport?.ImportId == importId)
                    {
                        _activeImport = _activeImport with { Current = p.current, Total = p.total };
                    }
                }

                OnProgress?.Invoke(new TickImportProgressUpdate(
                    importId, p.current, p.total,
                    $"Downloading {symbol} tick data ({p.current}/{p.total} hours)"));
            });

            await foreach (var item in _downloader.DownloadAsync(symbol, missingDays, progressState, ct))
            {
                ct.ThrowIfCancellationRequested();

                if (item.Ticks.Count > 0)
                {
                    if (!dayTicks.TryGetValue(item.Date, out var list))
                    {
                        list = new List<TickCsvRow>();
                        dayTicks[item.Date] = list;
                    }
                    list.AddRange(item.Ticks);
                }
            }

            // Write all accumulated day ticks to cache
            foreach (var (date, ticks) in dayTicks)
            {
                // Sort ticks by timestamp before writing
                ticks.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
                await _cacheService.WriteDayTicksAsync(symbol, date, ticks, ct);
            }

            // Get total tick count for the full range
            var totalTickCount = await _cacheService.GetTickCountAsync(symbol, startDate, endDate, ct);

            // Update record to Completed
            var completedRecord = new TickImportRecord(
                ImportId: importId,
                Source: "Dukascopy",
                Symbol: symbol,
                RequestedStart: new DateTimeOffset(startDate, TimeSpan.Zero),
                RequestedEnd: new DateTimeOffset(endDate, TimeSpan.Zero),
                Status: TickImportStatus.Completed,
                TotalTickCount: totalTickCount,
                CreatedAt: _activeImport?.StartedAt ?? DateTimeOffset.UtcNow,
                CompletedAt: DateTimeOffset.UtcNow);

            await _importRepository.SaveAsync(completedRecord, CancellationToken.None);

            _logger.LogInformation(
                "Import {ImportId} completed — {TickCount} total ticks",
                importId, totalTickCount);

            lock (_lock)
            {
                _activeImport = null;
                _cts?.Dispose();
                _cts = null;
            }

            OnCompleted?.Invoke(new TickImportCompletionUpdate(importId, TickImportStatus.Completed, null));
        }
        catch (OperationCanceledException)
        {
            await SetTerminalStatusAsync(importId, TickImportStatus.Cancelled, null);
            OnCompleted?.Invoke(new TickImportCompletionUpdate(importId, TickImportStatus.Cancelled, null));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import {ImportId} failed", importId);
            await SetTerminalStatusAsync(importId, TickImportStatus.Failed, ex.Message);
            OnCompleted?.Invoke(new TickImportCompletionUpdate(importId, TickImportStatus.Failed, ex.Message));
        }
    }

    private async Task SetTerminalStatusAsync(string importId, TickImportStatus status, string? errorDetail)
    {
        try
        {
            var existing = await _importRepository.GetAsync(importId, CancellationToken.None);
            if (existing is not null)
            {
                var updated = existing with
                {
                    Status = status,
                    ErrorDetail = errorDetail,
                    CompletedAt = DateTimeOffset.UtcNow
                };
                await _importRepository.SaveAsync(updated, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update terminal status for import {ImportId}", importId);
        }
        finally
        {
            lock (_lock)
            {
                _activeImport = null;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }
}
