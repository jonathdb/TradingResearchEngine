using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.PaperTrading;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Polls a configured REST endpoint at a configurable interval and emits bars
/// through the <see cref="IStreamingDataProvider"/> interface. Implements
/// <see cref="IDataFeedMetrics"/> for observability.
/// </summary>
/// <remarks>
/// <para>
/// On error responses the provider logs the error and retries on the next polling
/// interval without terminating the session. When consecutive failures exceed the
/// configured threshold, a structured warning is emitted at Warning level.
/// </para>
/// </remarks>
public sealed class PollingRestStreamingDataProvider : IStreamingDataProvider, IDataFeedMetrics
{
    private readonly HttpClient _httpClient;
    private readonly PollingProviderOptions _options;
    private readonly ILogger<PollingRestStreamingDataProvider> _logger;

    /// <inheritdoc/>
    public DateTimeOffset? LastSuccessfulPoll { get; private set; }

    /// <inheritdoc/>
    public int ConsecutiveFailureCount { get; private set; }

    /// <inheritdoc/>
    public DataFeedMode CurrentMode { get; private set; } = DataFeedMode.Live;

    /// <summary>
    /// Initializes a new instance of <see cref="PollingRestStreamingDataProvider"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to poll the REST endpoint.</param>
    /// <param name="options">Configuration options for polling behaviour.</param>
    /// <param name="logger">Logger for diagnostics and structured warnings.</param>
    public PollingRestStreamingDataProvider(
        HttpClient httpClient,
        IOptions<PollingProviderOptions> options,
        ILogger<PollingRestStreamingDataProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BarRecord> StreamAsync(
        string symbol,
        string interval,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation(
            "Starting live polling for {Symbol} ({Interval}) at endpoint {Endpoint} with interval {PollInterval}",
            symbol, interval, _options.EndpointUrl, _options.PollingInterval);

        while (!ct.IsCancellationRequested)
        {
            BarRecord? bar = null;

            try
            {
                var url = BuildPollUrl(symbol, interval);
                var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    bar = await response.Content
                        .ReadFromJsonAsync<BarRecord>(cancellationToken: ct)
                        .ConfigureAwait(false);

                    ConsecutiveFailureCount = 0;
                    LastSuccessfulPoll = DateTimeOffset.UtcNow;
                }
                else
                {
                    var statusCode = (int)response.StatusCode;
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    HandlePollFailure($"HTTP {statusCode}: {body}");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Graceful cancellation — exit the loop
                yield break;
            }
            catch (Exception ex)
            {
                HandlePollFailure(ex.Message);
            }

            if (bar is not null)
            {
                yield return bar;
            }

            try
            {
                await Task.Delay(_options.PollingInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                yield break;
            }
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BarRecord> GetBars(
        string symbol,
        string interval,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // For non-streaming access, poll once and return whatever bars are available
        var url = BuildBarsUrl(symbol, interval, from, to);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch bars from {Url}", url);
            yield break;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("GetBars request failed with status {StatusCode}", (int)response.StatusCode);
            yield break;
        }

        var bars = await response.Content
            .ReadFromJsonAsync<List<BarRecord>>(cancellationToken: ct)
            .ConfigureAwait(false) ?? [];

        foreach (var bar in bars)
        {
            yield return bar;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TickRecord> GetTicks(
        string symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Tick streaming is not supported by the polling REST provider
        _logger.LogWarning("GetTicks is not supported by PollingRestStreamingDataProvider");
        await Task.CompletedTask;
        yield break;
    }

    private string BuildPollUrl(string symbol, string interval)
    {
        var baseUrl = _options.EndpointUrl?.TrimEnd('/') ?? string.Empty;
        return $"{baseUrl}/bars/latest?symbol={symbol}&interval={interval}";
    }

    private string BuildBarsUrl(string symbol, string interval, DateTimeOffset from, DateTimeOffset to)
    {
        var baseUrl = _options.EndpointUrl?.TrimEnd('/') ?? string.Empty;
        return $"{baseUrl}/bars?symbol={symbol}&interval={interval}&from={from:O}&to={to:O}";
    }

    private void HandlePollFailure(string errorDetail)
    {
        ConsecutiveFailureCount++;

        _logger.LogError(
            "Poll failure for live data feed: {ErrorDetail} (consecutive failures: {Count})",
            errorDetail, ConsecutiveFailureCount);

        if (ConsecutiveFailureCount >= _options.ConsecutiveFailureWarningThreshold)
        {
            _logger.LogWarning(
                "PollingFailureThresholdExceeded: Consecutive failures: {Count}, threshold: {Threshold}",
                ConsecutiveFailureCount, _options.ConsecutiveFailureWarningThreshold);
        }
    }
}
