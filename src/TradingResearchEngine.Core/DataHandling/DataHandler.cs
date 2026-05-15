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
/// Provides provider-aware progress estimation that refines as bars are consumed.
/// </summary>
public sealed class DataHandler
{
    private readonly IDataProvider _provider;
    private readonly ScenarioConfig _config;
    private readonly ILogger<DataHandler> _logger;
    private readonly BarDataPool? _barDataPool;
    private readonly IAsyncEnumerator<BarRecord>? _barEnumerator;
    private readonly IAsyncEnumerator<TickRecord>? _tickEnumerator;
    private readonly DateTimeOffset _from;
    private readonly DateTimeOffset _to;
    private bool _hasMore = true;

    private int _estimatedTotalBars;
    private bool _estimateInitialized;
    private int _barsConsumed;

    /// <summary>Number of records skipped due to missing or unparseable fields.</summary>
    public int MalformedRecordCount { get; private set; }

    /// <summary>Returns <c>true</c> while the data provider has more records to emit.</summary>
    public bool HasMore => _hasMore;

    /// <summary>Number of bars consumed so far during execution.</summary>
    public int BarsConsumed => _barsConsumed;

    /// <summary>
    /// Current estimated total bar count. Updated initially from the provider estimate or
    /// date-range fallback, then refined as actual bars are consumed during execution.
    /// Returns zero if no estimate is available yet.
    /// </summary>
    public int EstimatedTotalBars => _estimatedTotalBars;

    /// <summary>
    /// Initialises the handler. Throws <see cref="ConfigurationException"/> when
    /// <see cref="ReplayMode.Tick"/> is requested but the provider only supplies bars.
    /// </summary>
    /// <param name="provider">The data provider to read bars/ticks from.</param>
    /// <param name="config">Scenario configuration controlling replay mode and data options.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <param name="barDataPool">Optional object pool for reducing hot-path allocations. Transparent to callers.</param>
    public DataHandler(IDataProvider provider, ScenarioConfig config, ILogger<DataHandler> logger, BarDataPool? barDataPool = null)
    {
        _provider = provider;
        _config = config;
        _logger = logger;
        _barDataPool = barDataPool;

        var opts = config.DataProviderOptions;
        string symbol = opts.TryGetValue("Symbol", out var s) ? s?.ToString() ?? "" : "";
        string interval = opts.TryGetValue("Interval", out var i) ? i?.ToString() ?? "1D" : "1D";
        _from = ParseDateTimeOffset(opts, "From", DateTimeOffset.MinValue);
        _to = ParseDateTimeOffset(opts, "To", DateTimeOffset.MaxValue);

        if (config.ReplayMode == ReplayMode.Tick)
            _tickEnumerator = provider.GetTicks(symbol, _from, _to).GetAsyncEnumerator();
        else
            _barEnumerator = provider.GetBars(symbol, interval, _from, _to).GetAsyncEnumerator();
    }

    /// <summary>
    /// Initialises the progress estimate by querying the provider first, then falling back
    /// to a date-range-based calculation using <see cref="ScenarioConfig.BarsPerYear"/>.
    /// Must be called before the engine loop starts. Lightweight — does not preload data.
    /// </summary>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    public async ValueTask InitializeEstimateAsync(CancellationToken ct = default)
    {
        if (_estimateInitialized) return;

        // Try provider-aware estimate first (Requirement 13.2)
        var providerEstimate = await _provider.EstimateBarCountAsync(ct);
        if (providerEstimate.HasValue && providerEstimate.Value > 0)
        {
            _estimatedTotalBars = providerEstimate.Value;
            _estimateInitialized = true;
            return;
        }

        // Fallback: estimate from date range using BarsPerYear (Requirement 13.1)
        _estimatedTotalBars = EstimateFromDateRange(_from, _to, _config.BarsPerYear);
        _estimateInitialized = true;
    }

    /// <summary>
    /// Notifies the handler that a bar has been consumed, allowing it to refine
    /// the progress estimate as actual data flows through (Requirement 13.4).
    /// </summary>
    public void NotifyBarConsumed()
    {
        _barsConsumed++;

        // Refine estimate upward if we've already exceeded the initial estimate
        // This handles cases where the provider underestimated or the date-range fallback was too low
        if (_barsConsumed > _estimatedTotalBars && _estimatedTotalBars > 0)
        {
            // Project forward: assume we're ~80% through when we exceed the estimate
            // This provides a smooth ramp rather than a sudden jump
            _estimatedTotalBars = (int)(_barsConsumed * 1.25);
        }
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

    /// <summary>
    /// Estimates total bar count from a date range using BarsPerYear.
    /// Falls back to a 5-year estimate when dates are unbounded.
    /// </summary>
    private static int EstimateFromDateRange(DateTimeOffset from, DateTimeOffset to, int barsPerYear)
    {
        const int DefaultYears = 5;

        // If both bounds are specified and reasonable, compute from the range
        if (from != DateTimeOffset.MinValue && to != DateTimeOffset.MaxValue)
        {
            double years = (to - from).TotalDays / 365.25;
            if (years > 0)
                return Math.Max(1, (int)(barsPerYear * years));
        }

        // Fallback: assume a typical multi-year run
        return barsPerYear * DefaultYears;
    }

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
