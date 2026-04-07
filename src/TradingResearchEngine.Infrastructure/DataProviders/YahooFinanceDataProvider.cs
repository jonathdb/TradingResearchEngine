using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Downloads historical bar data from Yahoo Finance (free, no API key required).
/// Retries on 429 rate-limit responses with exponential backoff.
/// </summary>
public sealed class YahooFinanceDataProvider : IDataProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YahooFinanceDataProvider> _logger;

    public YahooFinanceDataProvider(HttpClient httpClient, ILogger<YahooFinanceDataProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async IAsyncEnumerable<BarRecord> GetBars(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var csv = await FetchCsvAsync(symbol, interval, from, to, ct);
        if (csv is null) yield break;

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 1; i < lines.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var parts = lines[i].Split(',');
            if (parts.Length < 7) continue;

            BarRecord? bar = null;
            try
            {
                var date = DateTimeOffset.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                bar = new BarRecord(symbol, interval,
                    decimal.Parse(parts[1].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[2].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[3].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[4].Trim(), CultureInfo.InvariantCulture),
                    decimal.Parse(parts[6].Trim(), CultureInfo.InvariantCulture),
                    date);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping malformed Yahoo row: {Row}", lines[i]);
                continue;
            }
            yield return bar;
        }
    }

    public async IAsyncEnumerable<TickRecord> GetTicks(
        string symbol, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _logger.LogWarning("Yahoo Finance does not support tick data.");
        yield break;
    }

    private async Task<string?> FetchCsvAsync(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var yahooInterval = MapInterval(interval);
        long period1 = from.ToUnixTimeSeconds();
        long period2 = to.ToUnixTimeSeconds();

        var url = $"https://query1.finance.yahoo.com/v7/finance/download/{symbol}" +
                  $"?period1={period1}&period2={period2}&interval={yahooInterval}" +
                  $"&events=history&includeAdjustedClose=true";

        _logger.LogInformation("Fetching Yahoo Finance data for {Symbol} ({From} to {To})", symbol, from, to);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.GetAsync(url, ct);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request failed for {Symbol}", symbol);
                return null;
            }

            if ((int)response.StatusCode == 429 && attempt < 3)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning("Yahoo 429 rate limit. Retrying in {Delay}...", delay);
                await Task.Delay(delay, ct);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Yahoo returned {Code}: {Body}", (int)response.StatusCode, body);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }

        return null;
    }

    private static string MapInterval(string interval) => interval.ToLowerInvariant() switch
    {
        "1d" or "daily" => "1d",
        "1wk" or "weekly" => "1wk",
        "1mo" or "monthly" => "1mo",
        "1h" or "hourly" => "1h",
        "5m" => "5m", "15m" => "15m", "30m" => "30m", "60m" => "60m", "90m" => "90m",
        _ => "1d"
    };
}
