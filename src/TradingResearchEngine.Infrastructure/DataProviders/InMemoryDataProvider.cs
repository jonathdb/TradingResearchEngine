using System.Runtime.CompilerServices;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>
/// Serves bar and tick records from in-memory lists.
/// Used by research workflows that partition data (RandomizedOOS, WalkForward)
/// and by tests that need a fast, deterministic data source.
/// </summary>
public sealed class InMemoryDataProvider : IDataProvider
{
    private readonly IReadOnlyList<BarRecord> _bars;
    private readonly IReadOnlyList<TickRecord> _ticks;

    /// <summary>Creates a provider from pre-loaded bar records.</summary>
    public InMemoryDataProvider(IReadOnlyList<BarRecord> bars)
    {
        _bars = bars;
        _ticks = Array.Empty<TickRecord>();
    }

    /// <summary>Creates a provider from pre-loaded tick records.</summary>
    public InMemoryDataProvider(IReadOnlyList<TickRecord> ticks)
    {
        _bars = Array.Empty<BarRecord>();
        _ticks = ticks;
    }

    /// <summary>Creates a provider from both bar and tick records.</summary>
    public InMemoryDataProvider(IReadOnlyList<BarRecord> bars, IReadOnlyList<TickRecord> ticks)
    {
        _bars = bars;
        _ticks = ticks;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<BarRecord> GetBars(
        string symbol, string interval, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var bar in _bars)
        {
            ct.ThrowIfCancellationRequested();
            if (bar.Timestamp >= from && bar.Timestamp <= to)
                yield return bar;
        }
        await Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TickRecord> GetTicks(
        string symbol, DateTimeOffset from, DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var tick in _ticks)
        {
            ct.ThrowIfCancellationRequested();
            if (tick.Timestamp >= from && tick.Timestamp <= to)
                yield return tick;
        }
        await Task.CompletedTask;
    }
}
