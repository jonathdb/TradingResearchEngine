using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Infrastructure.Exceptions;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Issues HTTP GET requests to a configurable base URL and deserialises JSON responses.
/// Designed for subclassing — override <see cref="BuildBarsUrl"/> and <see cref="MapBarResponse"/> for named providers.
/// </summary>
public class HttpRestDataProvider : IDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    /// <inheritdoc cref="HttpRestDataProvider"/>
    public HttpRestDataProvider(HttpClient httpClient, string baseUrl)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    /// <summary>Builds the URL for a bars request. Override for named providers.</summary>
    protected virtual string BuildBarsUrl(string symbol, string interval, DateTimeOffset from, DateTimeOffset to)
        => $"{_baseUrl}/bars?symbol={symbol}&interval={interval}&from={from:O}&to={to:O}";

    /// <summary>Builds the URL for a ticks request. Override for named providers.</summary>
    protected virtual string BuildTicksUrl(string symbol, DateTimeOffset from, DateTimeOffset to)
        => $"{_baseUrl}/ticks?symbol={symbol}&from={from:O}&to={to:O}";

    /// <inheritdoc/>
    public async IAsyncEnumerable<BarRecord> GetBars(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = BuildBarsUrl(symbol, interval, from, to);
        var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccess(response);

        var bars = await response.Content.ReadFromJsonAsync<List<BarRecord>>(ct) ?? new();
        foreach (var bar in bars) yield return bar;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TickRecord> GetTicks(
        string symbol, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = BuildTicksUrl(symbol, from, to);
        var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccess(response);

        var ticks = await response.Content.ReadFromJsonAsync<List<TickRecord>>(ct) ?? new();
        foreach (var tick in ticks) yield return tick;
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new DataProviderException((int)response.StatusCode, body);
        }
    }
}
