using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using TradingResearchEngine.Application.TickImport;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.Infrastructure.TickImport;

/// <summary>
/// High-concurrency tick downloader for Dukascopy <c>h_ticks.bi5</c> files.
/// Flattens all hour-files across all trading days into a single work queue,
/// downloads with configurable concurrency, skips weekend hours, and pipelines
/// results back to the caller via a channel.
/// </summary>
public sealed class DukascopyTickDownloader : ITickDownloader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DukascopyTickDownloader> _logger;
    private readonly TickImportOptions _options;

    /// <inheritdoc/>
    public IReadOnlySet<string> SupportedSymbols { get; } =
        new HashSet<string>(DukascopyHelpers.PointSizes.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of <see cref="DukascopyTickDownloader"/>.</summary>
    /// <param name="httpClient">HTTP client for downloading bi5 files.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Tick import configuration options.</param>
    public DukascopyTickDownloader(
        HttpClient httpClient,
        ILogger<DukascopyTickDownloader> logger,
        IOptions<TickImportOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Downloads tick data for all hours in the given trading days.
    /// Yields <see cref="TickDownloadItem"/> tuples as they complete.
    /// </summary>
    /// <param name="symbol">The trading symbol (e.g., EURUSD).</param>
    /// <param name="tradingDays">List of weekday dates to download.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of download results.</returns>
    public async IAsyncEnumerable<TickDownloadItem> DownloadAsync(
        string symbol,
        IReadOnlyList<DateTime> tradingDays,
        IProgress<(int current, int total)>? progress = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Build flattened work queue — skip weekend days entirely
        var workItems = new List<(DateTime Date, int Hour)>();
        foreach (var day in tradingDays)
        {
            if (day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday)
                continue;

            for (int hour = 0; hour < 24; hour++)
            {
                workItems.Add((day, hour));
            }
        }

        var total = workItems.Count;
        var completed = 0;

        var channel = Channel.CreateBounded<TickDownloadItem>(
            new BoundedChannelOptions(_options.MaxConcurrency * 2)
            {
                SingleWriter = false,
                SingleReader = true
            });

        var semaphore = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);

        // Producer: download all hours concurrently
        var producerTask = Task.Run(async () =>
        {
            try
            {
                    var downloadTasks = workItems.Select(async item =>
                    {
                        await semaphore.WaitAsync(ct);
                        try
                        {
                            var ticks = await DownloadHourAsync(symbol, item.Date, item.Hour, ct);
                            var result = new TickDownloadItem(item.Date, item.Hour, ticks);
                            await channel.Writer.WriteAsync(result, ct);

                            var current = Interlocked.Increment(ref completed);
                            progress?.Report((current, total));
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }).ToArray();

                await Task.WhenAll(downloadTasks);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, ct);

        // Consumer: yield results as they arrive
        await foreach (var result in channel.Reader.ReadAllAsync(ct))
        {
            yield return result;
        }

        await producerTask;
    }

    private async Task<IReadOnlyList<TickCsvRow>> DownloadHourAsync(
        string symbol, DateTime date, int hour, CancellationToken ct)
    {
        // Dukascopy months are 0-indexed
        var month = date.Month - 1;
        var url = $"{DukascopyHelpers.BaseUrl}/{symbol}/{date.Year}/{month:D2}/{date.Day:D2}/{hour:D2}h_ticks.bi5";

        byte[] compressed;
        try
        {
            compressed = await DownloadWithRetryAsync(url, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Download failed for {Symbol} {Date:yyyy-MM-dd} hour {Hour}: {Message}",
                symbol, date, hour, ex.Message);
            return Array.Empty<TickCsvRow>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unexpected error downloading {Symbol} {Date:yyyy-MM-dd} hour {Hour}",
                symbol, date, hour);
            return Array.Empty<TickCsvRow>();
        }

        if (compressed.Length == 0)
            return Array.Empty<TickCsvRow>();

        // Decompress LZMA
        byte[] decompressed;
        try
        {
            decompressed = DukascopyHelpers.Decompress(compressed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LZMA decompression failed for {Symbol} {Date:yyyy-MM-dd} hour {Hour}",
                symbol, date, hour);
            return Array.Empty<TickCsvRow>();
        }

        if (decompressed.Length == 0)
            return Array.Empty<TickCsvRow>();

        // Parse ticks using existing helper
        var hourStart = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Utc);

        if (!DukascopyHelpers.PointSizes.TryGetValue(symbol, out var pointSize))
        {
            _logger.LogWarning("No point size configured for symbol {Symbol}, skipping hour {Hour} on {Date:yyyy-MM-dd}",
                symbol, hour, date);
            return Array.Empty<TickCsvRow>();
        }

        var tickRecords = DukascopyHelpers.ParseTicks(decompressed, hourStart, symbol, pointSize);

        // Convert TickRecord → TickCsvRow
        var csvRows = new List<TickCsvRow>(tickRecords.Count);
        foreach (var tick in tickRecords)
        {
            csvRows.Add(new TickCsvRow(
                tick.Timestamp,
                tick.BidLevels[0].Price,
                tick.AskLevels[0].Price,
                tick.BidLevels[0].Size,
                tick.AskLevels[0].Size));
        }

        return csvRows;
    }

    private async Task<byte[]> DownloadWithRetryAsync(string url, CancellationToken ct)
    {
        var pipeline = BuildRetryPipeline();
        try
        {
            return await pipeline.ExecuteAsync(async token =>
            {
                var response = await _httpClient.GetAsync(url, token);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return Array.Empty<byte>();

                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(token);
            }, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<byte>();
        }
    }

    private ResiliencePipeline<byte[]> BuildRetryPipeline() =>
        new ResiliencePipelineBuilder<byte[]>()
            .AddRetry(new RetryStrategyOptions<byte[]>
            {
                MaxRetryAttempts = _options.MaxRetryAttempts,
                DelayGenerator = args => ValueTask.FromResult<TimeSpan?>(
                    TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber))),
                ShouldHandle = new PredicateBuilder<byte[]>()
                    .Handle<HttpRequestException>(ex =>
                        ex.StatusCode is null || (int)ex.StatusCode >= 500),
                OnRetry = args =>
                {
                    _logger.LogDebug("Retry {Attempt}/{Max} after {Delay}s for download",
                        args.AttemptNumber + 1, _options.MaxRetryAttempts, args.RetryDelay.TotalSeconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
}
