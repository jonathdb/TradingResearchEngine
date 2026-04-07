using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Exceptions;
using TradingResearchEngine.Core.Queue;

namespace TradingResearchEngine.Core.DataHandling;

/// <summary>
/// Wraps an <see cref="IDataProvider"/> and emits typed market data events into the EventQueue.
/// Skips malformed records and tracks a <see cref="MalformedRecordCount"/>.
/// </summary>
public sealed class DataHandler
{
    private readonly IDataProvider _provider;
    private readonly ScenarioConfig _config;
    private readonly ILogger<DataHandler> _logger;
    private readonly IAsyncEnumerator<BarRecord>? _barEnumerator;
    private readonly IAsyncEnumerator<TickRecord>? _tickEnumerator;
    private bool _hasMore = true;

    /// <summary>Number of records skipped due to missing or unparseable fields.</summary>
    public int MalformedRecordCount { get; private set; }

    /// <summary>Returns <c>true</c> while the data provider has more records to emit.</summary>
    public bool HasMore => _hasMore;

    /// <summary>
    /// Initialises the handler. Throws <see cref="ConfigurationException"/> when
    /// <see cref="ReplayMode.Tick"/> is requested but the provider only supplies bars.
    /// </summary>
    public DataHandler(IDataProvider provider, ScenarioConfig config, ILogger<DataHandler> logger)
    {
        _provider = provider;
        _config = config;
        _logger = logger;

        var opts = config.DataProviderOptions;
        string symbol = opts.TryGetValue("Symbol", out var s) ? s?.ToString() ?? "" : "";
        string interval = opts.TryGetValue("Interval", out var i) ? i?.ToString() ?? "1D" : "1D";
        DateTimeOffset from = ParseDateTimeOffset(opts, "From", DateTimeOffset.MinValue);
        DateTimeOffset to = ParseDateTimeOffset(opts, "To", DateTimeOffset.MaxValue);

        if (config.ReplayMode == ReplayMode.Tick)
            _tickEnumerator = provider.GetTicks(symbol, from, to).GetAsyncEnumerator();
        else
            _barEnumerator = provider.GetBars(symbol, interval, from, to).GetAsyncEnumerator();
    }

    /// <summary>
    /// Advances one step and enqueues the next <see cref="BarEvent"/> or <see cref="TickEvent"/>.
    /// Sets <see cref="HasMore"/> to <c>false</c> when the provider is exhausted.
    /// </summary>
    public async Task EmitNextAsync(IEventQueue queue, CancellationToken ct = default)
    {
        if (_config.ReplayMode == ReplayMode.Bar)
            await EmitNextBarAsync(queue, ct);
        else
            await EmitNextTickAsync(queue, ct);
    }

    private async Task EmitNextBarAsync(IEventQueue queue, CancellationToken ct)
    {
        while (true)
        {
            bool moved = await _barEnumerator!.MoveNextAsync();
            if (!moved) { _hasMore = false; return; }

            var r = _barEnumerator.Current;
            if (!IsValidBar(r))
            {
                MalformedRecordCount++;
                _logger.LogWarning("MalformedRecord: skipping bar record for {Symbol} at {Timestamp}.", r.Symbol, r.Timestamp);
                continue;
            }
            queue.Enqueue(new BarEvent(r.Symbol, r.Interval, r.Open, r.High, r.Low, r.Close, r.Volume, r.Timestamp));
            return;
        }
    }

    private async Task EmitNextTickAsync(IEventQueue queue, CancellationToken ct)
    {
        while (true)
        {
            bool moved = await _tickEnumerator!.MoveNextAsync();
            if (!moved) { _hasMore = false; return; }

            var r = _tickEnumerator.Current;
            if (!IsValidTick(r))
            {
                MalformedRecordCount++;
                _logger.LogWarning("MalformedRecord: skipping tick record for {Symbol} at {Timestamp}.", r.Symbol, r.Timestamp);
                continue;
            }
            queue.Enqueue(new TickEvent(r.Symbol, r.BidLevels, r.AskLevels, r.LastTrade, r.Timestamp));
            return;
        }
    }

    private static bool IsValidBar(BarRecord r) =>
        !string.IsNullOrEmpty(r.Symbol) && r.Open > 0 && r.High > 0 && r.Low > 0 && r.Close > 0;

    private static bool IsValidTick(TickRecord r) =>
        !string.IsNullOrEmpty(r.Symbol) && r.BidLevels.Count > 0 && r.AskLevels.Count > 0;

    private static DateTimeOffset ParseDateTimeOffset(
        Dictionary<string, object> opts, string key, DateTimeOffset fallback)
    {
        if (!opts.TryGetValue(key, out var val)) return fallback;
        if (val is DateTimeOffset dto) return dto;
        if (val is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
        if (val is string str && DateTimeOffset.TryParse(str, out var parsed)) return parsed;
        // Handle System.Text.Json's JsonElement
        if (val?.ToString() is string s && DateTimeOffset.TryParse(s, out var parsed2)) return parsed2;
        return fallback;
    }
}
