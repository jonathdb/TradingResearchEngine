using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Queue;

/// <summary>Thread-safe FIFO queue for engine events.</summary>
public interface IEventQueue
{
    /// <summary>Enqueues an event for processing.</summary>
    void Enqueue(EngineEvent evt);

    /// <summary>Attempts to dequeue the next event. Returns <c>false</c> when the queue is empty.</summary>
    bool TryDequeue(out EngineEvent? evt);

    /// <summary>Returns <c>true</c> when the queue contains no events.</summary>
    bool IsEmpty { get; }
}
