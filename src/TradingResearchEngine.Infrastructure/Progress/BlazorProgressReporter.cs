using System.Diagnostics;
using System.Threading.Channels;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Infrastructure.Progress;

/// <summary>
/// Blazor-specific <see cref="IProgressReporter"/> that streams structured
/// <see cref="ProgressSnapshot"/> updates via a bounded <see cref="Channel{T}"/>.
/// <para>
/// Throttles UI updates to a maximum of 4 per second (250ms interval) to avoid
/// excessive re-renders. Uses <see cref="BoundedChannelFullMode.DropOldest"/> so
/// the latest state is always available without memory growth.
/// </para>
/// <para>
/// Also preserves the legacy callback-based API for backward compatibility with
/// existing consumers that use the <c>Action&lt;int, int, string&gt;</c> constructor.
/// </para>
/// </summary>
public sealed class BlazorProgressReporter : IProgressReporter, IAsyncDisposable
{
    private readonly Channel<ProgressSnapshot> _channel;
    private readonly Action<int, int, string>? _legacyCallback;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private DateTime _lastEmit = DateTime.MinValue;

    /// <summary>Minimum interval between channel writes (4 updates/sec max).</summary>
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Reader for consuming progress snapshots from the UI layer.
    /// Multiple subscribers can read from this reader concurrently.
    /// </summary>
    public ChannelReader<ProgressSnapshot> Reader => _channel.Reader;

    /// <summary>
    /// Creates a new channel-based progress reporter.
    /// </summary>
    public BlazorProgressReporter()
    {
        _channel = Channel.CreateBounded<ProgressSnapshot>(
            new BoundedChannelOptions(16)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleWriter = true,
                SingleReader = false
            });
    }

    /// <summary>
    /// Creates a progress reporter with a legacy callback for backward compatibility.
    /// The callback is invoked in addition to writing to the channel.
    /// </summary>
    /// <param name="callback">Legacy callback receiving (current, total, label).</param>
    public BlazorProgressReporter(Action<int, int, string> callback) : this()
    {
        _legacyCallback = callback;
    }

    /// <inheritdoc/>
    public void Report(int current, int total, string label)
    {
        // Invoke legacy callback unconditionally (existing behavior)
        _legacyCallback?.Invoke(current, total, label);

        // Build a snapshot and write to channel (throttled)
        var snapshot = new ProgressSnapshot(
            Current: current,
            Total: total,
            Percentage: total > 0 ? (decimal)current / total * 100m : 0m,
            Stage: label,
            CurrentItemLabel: null,
            ElapsedTime: _stopwatch.Elapsed,
            Warnings: Array.Empty<string>());

        TryWriteThrottled(snapshot);
    }

    /// <inheritdoc/>
    public void Report(ProgressSnapshot snapshot)
    {
        // Invoke legacy callback for backward compatibility
        _legacyCallback?.Invoke(snapshot.Current, snapshot.Total, snapshot.CurrentItemLabel ?? snapshot.Stage);

        TryWriteThrottled(snapshot);
    }

    /// <summary>
    /// Writes the snapshot to the channel if the throttle interval has elapsed.
    /// Always writes the final update (Current == Total) regardless of throttle.
    /// </summary>
    private void TryWriteThrottled(ProgressSnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        var isFinalUpdate = snapshot.Current > 0 && snapshot.Current >= snapshot.Total;

        if (!isFinalUpdate && (now - _lastEmit) < ThrottleInterval)
            return;

        _lastEmit = now;
        // TryWrite is non-blocking; DropOldest ensures we never block
        _channel.Writer.TryWrite(snapshot);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
