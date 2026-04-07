using System.Collections.Concurrent;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Queue;

/// <summary>Thread-safe event queue backed by <see cref="ConcurrentQueue{T}"/>.</summary>
public sealed class EventQueue : IEventQueue
{
    private readonly ConcurrentQueue<EngineEvent> _queue = new();

    /// <inheritdoc/>
    public void Enqueue(EngineEvent evt) => _queue.Enqueue(evt);

    /// <inheritdoc/>
    public bool TryDequeue(out EngineEvent? evt) => _queue.TryDequeue(out evt);

    /// <inheritdoc/>
    public bool IsEmpty => _queue.IsEmpty;
}
