using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Polls an existing <see cref="IDataProvider"/> and emits bars as an async stream
/// with configurable playback speed. Supports fast-forward playback for paper trading
/// simulation and testing scenarios.
/// </summary>
/// <remarks>
/// <para>
/// The provider loads all available bars from the inner <see cref="IDataProvider"/> and
/// replays them one at a time with a delay of <c>pollInterval / speedRatio</c> between
/// each bar. This simulates real-time bar arrival at controllable speeds.
/// </para>
/// <para>
/// A <c>speedRatio</c> of 1.0 yields real-time playback; 10.0 yields 10× faster playback.
/// </para>
/// </remarks>
public sealed class PollingStreamingDataProvider : IStreamingDataProvider
{
    private readonly IDataProvider _inner;
    private readonly TimeSpan _pollInterval;
    private readonly double _speedRatio;
    private readonly ILogger<PollingStreamingDataProvider>? _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="PollingStreamingDataProvider"/>.
    /// </summary>
    /// <param name="inner">The underlying data provider to load bars from.</param>
    /// <param name="pollInterval">The base interval between emitted bars at real-time speed.</param>
    /// <param name="speedRatio">
    /// Playback speed multiplier. 1.0 = real-time, 10.0 = 10× faster.
    /// Must be greater than zero.
    /// </param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="speedRatio"/> is less than or equal to zero.</exception>
    public PollingStreamingDataProvider(
        IDataProvider inner,
        TimeSpan pollInterval,
        double speedRatio,
        ILogger<PollingStreamingDataProvider>? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _pollInterval = pollInterval;
        _speedRatio = speedRatio > 0
            ? speedRatio
            : throw new ArgumentOutOfRangeException(nameof(speedRatio), "Speed ratio must be greater than zero.");
        _logger = logger;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BarRecord> StreamAsync(
        string symbol,
        string interval,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var effectiveDelay = _pollInterval / _speedRatio;

        _logger?.LogDebug(
            "Starting streaming playback for {Symbol} ({Interval}) with effective delay {Delay}ms (pollInterval={PollInterval}, speedRatio={SpeedRatio})",
            symbol, interval, effectiveDelay.TotalMilliseconds, _pollInterval, _speedRatio);

        // Load all bars from the inner provider using full historical range
        var bars = new List<BarRecord>();
        await foreach (var bar in _inner.GetBars(
            symbol, interval, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, ct).ConfigureAwait(false))
        {
            bars.Add(bar);
        }

        _logger?.LogDebug("Loaded {BarCount} bars for streaming playback", bars.Count);

        // Yield bars one at a time with the configured delay
        foreach (var bar in bars)
        {
            ct.ThrowIfCancellationRequested();
            yield return bar;

            await Task.Delay(effectiveDelay, ct).ConfigureAwait(false);
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
        // Delegate to the inner provider for non-streaming access
        await foreach (var bar in _inner.GetBars(symbol, interval, from, to, ct).ConfigureAwait(false))
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
        // Delegate to the inner provider for tick access
        await foreach (var tick in _inner.GetTicks(symbol, from, to, ct).ConfigureAwait(false))
        {
            yield return tick;
        }
    }
}
