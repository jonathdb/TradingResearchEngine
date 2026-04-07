namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>An empty IAsyncEnumerable that yields no elements.</summary>
internal sealed class EmptyAsyncEnumerable<T> : IAsyncEnumerable<T>
{
    public static readonly EmptyAsyncEnumerable<T> Instance = new();

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken ct = default)
        => new EmptyEnumerator();

    private sealed class EmptyEnumerator : IAsyncEnumerator<T>
    {
        public T Current => default!;
        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
