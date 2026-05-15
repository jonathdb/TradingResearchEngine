using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Exceptions;

namespace TradingResearchEngine.Core.DataHandling;

/// <summary>
/// Merges bars from multiple timeframe data providers into a single chronologically-ordered stream.
/// Secondary timeframe bars are interleaved with primary timeframe bars such that a secondary bar
/// is delivered before any primary bar whose timestamp is equal to or later than the secondary bar's timestamp.
/// </summary>
public sealed class MultiTimeframeDataHandler
{
    private readonly IDataProviderFactory _providerFactory;
    private readonly ILogger<MultiTimeframeDataHandler> _logger;
    private readonly List<SecondaryTimeframeStream> _streams = new();
    private bool _initialized;

    /// <summary>
    /// Initialises the multi-timeframe data handler.
    /// </summary>
    /// <param name="providerFactory">Factory for creating data providers from configuration.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    public MultiTimeframeDataHandler(
        IDataProviderFactory providerFactory,
        ILogger<MultiTimeframeDataHandler> logger)
    {
        _providerFactory = providerFactory;
        _logger = logger;
    }

    /// <summary>
    /// Validates that all specified secondary timeframe data sources are available and can be created.
    /// Must be called before <see cref="InitializeAsync"/>.
    /// </summary>
    /// <param name="secondaryTimeframes">The secondary timeframe configurations to validate.</param>
    /// <returns>A list of validation errors. Empty if all sources are valid.</returns>
    public IReadOnlyList<string> ValidateDataSources(IReadOnlyList<SecondaryTimeframeConfig> secondaryTimeframes)
    {
        var errors = new List<string>();

        foreach (var config in secondaryTimeframes)
        {
            if (string.IsNullOrWhiteSpace(config.Timeframe))
            {
                errors.Add("Secondary timeframe configuration has an empty Timeframe label.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(config.DataProviderType))
            {
                errors.Add($"Secondary timeframe '{config.Timeframe}' has an empty DataProviderType.");
                continue;
            }

            try
            {
                var provider = _providerFactory.Create(config.DataProviderType, config.DataProviderOptions);
                if (provider is null)
                {
                    errors.Add($"Secondary timeframe '{config.Timeframe}': data provider '{config.DataProviderType}' could not be created.");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Secondary timeframe '{config.Timeframe}': failed to create data provider '{config.DataProviderType}' — {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Initialises the secondary timeframe streams by creating data providers and starting enumeration.
    /// Call <see cref="ValidateDataSources"/> first to ensure all sources are available.
    /// </summary>
    /// <param name="secondaryTimeframes">The secondary timeframe configurations.</param>
    /// <param name="primaryOptions">The primary data provider options (used to extract symbol and date range).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task InitializeAsync(
        IReadOnlyList<SecondaryTimeframeConfig> secondaryTimeframes,
        Dictionary<string, object> primaryOptions,
        CancellationToken ct = default)
    {
        if (_initialized)
            return;

        string symbol = primaryOptions.TryGetValue("Symbol", out var s) ? s?.ToString() ?? "" : "";
        var from = ParseDateTimeOffset(primaryOptions, "From", DateTimeOffset.MinValue);
        var to = ParseDateTimeOffset(primaryOptions, "To", DateTimeOffset.MaxValue);

        foreach (var config in secondaryTimeframes)
        {
            var provider = _providerFactory.Create(config.DataProviderType, config.DataProviderOptions);

            // Determine the symbol for the secondary provider — use its own options if specified, else primary symbol
            string secondarySymbol = config.DataProviderOptions.TryGetValue("Symbol", out var ss)
                ? ss?.ToString() ?? symbol
                : symbol;

            var secondaryFrom = ParseDateTimeOffset(config.DataProviderOptions, "From", from);
            var secondaryTo = ParseDateTimeOffset(config.DataProviderOptions, "To", to);

            // Determine interval from the timeframe config
            string interval = config.Timeframe;

            var enumerator = provider.GetBars(secondarySymbol, interval, secondaryFrom, secondaryTo, ct).GetAsyncEnumerator(ct);

            var stream = new SecondaryTimeframeStream(config.Timeframe, enumerator);

            // Advance to the first bar
            if (await enumerator.MoveNextAsync())
            {
                stream.CurrentBar = enumerator.Current;
                stream.HasMore = true;
            }
            else
            {
                stream.HasMore = false;
                _logger.LogWarning("Secondary timeframe '{Timeframe}' has no data available.", config.Timeframe);
            }

            _streams.Add(stream);
        }

        _initialized = true;
    }

    /// <summary>
    /// Returns all secondary timeframe bars that should be delivered before the given primary bar timestamp.
    /// Bars are returned in chronological order across all secondary timeframes.
    /// After calling this method, the returned bars are consumed and will not be returned again.
    /// </summary>
    /// <param name="primaryTimestamp">The timestamp of the next primary bar about to be processed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of (timeframe, bar) tuples ordered chronologically.</returns>
    public async Task<IReadOnlyList<(string Timeframe, BarRecord Bar)>> GetSecondaryBarsBeforeAsync(
        DateTimeOffset primaryTimestamp,
        CancellationToken ct = default)
    {
        var results = new List<(string Timeframe, BarRecord Bar, DateTimeOffset Timestamp)>();

        foreach (var stream in _streams)
        {
            while (stream.HasMore && stream.CurrentBar is not null && stream.CurrentBar.Timestamp <= primaryTimestamp)
            {
                results.Add((stream.Timeframe, stream.CurrentBar, stream.CurrentBar.Timestamp));

                if (await stream.Enumerator.MoveNextAsync())
                {
                    stream.CurrentBar = stream.Enumerator.Current;
                }
                else
                {
                    stream.HasMore = false;
                    stream.CurrentBar = null;
                }
            }
        }

        // Sort chronologically across all secondary timeframes
        results.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        return results.Select(r => (r.Timeframe, r.Bar)).ToList();
    }

    /// <summary>
    /// Returns any remaining secondary timeframe bars after the primary data is exhausted.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Remaining bars ordered chronologically.</returns>
    public async Task<IReadOnlyList<(string Timeframe, BarRecord Bar)>> DrainRemainingAsync(CancellationToken ct = default)
    {
        var results = new List<(string Timeframe, BarRecord Bar, DateTimeOffset Timestamp)>();

        foreach (var stream in _streams)
        {
            while (stream.HasMore && stream.CurrentBar is not null)
            {
                results.Add((stream.Timeframe, stream.CurrentBar, stream.CurrentBar.Timestamp));

                if (await stream.Enumerator.MoveNextAsync())
                {
                    stream.CurrentBar = stream.Enumerator.Current;
                }
                else
                {
                    stream.HasMore = false;
                    stream.CurrentBar = null;
                }
            }
        }

        results.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        return results.Select(r => (r.Timeframe, r.Bar)).ToList();
    }

    /// <summary>
    /// Returns <c>true</c> if any secondary timeframe stream still has bars to deliver.
    /// </summary>
    public bool HasMoreSecondaryBars => _streams.Any(s => s.HasMore);

    private static DateTimeOffset ParseDateTimeOffset(
        Dictionary<string, object> opts, string key, DateTimeOffset fallback)
    {
        if (!opts.TryGetValue(key, out var val)) return fallback;
        if (val is DateTimeOffset dto) return dto;
        if (val is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
        if (val is string str && DateTimeOffset.TryParse(str, out var parsed)) return parsed;
        if (val?.ToString() is string s && DateTimeOffset.TryParse(s, out var parsed2)) return parsed2;
        return fallback;
    }

    /// <summary>Internal state for a single secondary timeframe data stream.</summary>
    private sealed class SecondaryTimeframeStream
    {
        public SecondaryTimeframeStream(string timeframe, IAsyncEnumerator<BarRecord> enumerator)
        {
            Timeframe = timeframe;
            Enumerator = enumerator;
        }

        public string Timeframe { get; }
        public IAsyncEnumerator<BarRecord> Enumerator { get; }
        public BarRecord? CurrentBar { get; set; }
        public bool HasMore { get; set; }
    }
}
