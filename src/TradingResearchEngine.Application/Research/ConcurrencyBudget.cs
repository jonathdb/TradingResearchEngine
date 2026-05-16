using Microsoft.Extensions.Options;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Global concurrency budget preventing nested parallel workflows from
/// oversubscribing CPU resources. Wraps a <see cref="SemaphoreSlim"/>
/// and exposes an async acquire/release pattern via <see cref="IDisposable"/>.
/// </summary>
public sealed class ConcurrencyBudget : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    /// <summary>
    /// Initializes a new <see cref="ConcurrencyBudget"/> with the specified maximum concurrency.
    /// </summary>
    /// <param name="options">Options providing the configured max concurrency.</param>
    public ConcurrencyBudget(IOptions<ConcurrencyOptions> options)
    {
        var max = options.Value.MaxGlobalConcurrency;
        _semaphore = new SemaphoreSlim(max, max);
    }

    /// <summary>
    /// Initializes a new <see cref="ConcurrencyBudget"/> with an explicit maximum concurrency.
    /// Primarily used for testing.
    /// </summary>
    /// <param name="maxConcurrency">Maximum number of concurrent permits.</param>
    public ConcurrencyBudget(int maxConcurrency)
    {
        _semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>Gets the number of permits currently available.</summary>
    public int Available => _semaphore.CurrentCount;

    /// <summary>
    /// Asynchronously acquires a concurrency permit. The returned <see cref="IDisposable"/>
    /// releases the permit when disposed.
    /// </summary>
    /// <param name="ct">Cancellation token to observe while waiting for a permit.</param>
    /// <returns>An <see cref="IDisposable"/> that releases the permit on disposal.</returns>
    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        return new Releaser(_semaphore);
    }

    /// <inheritdoc />
    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser(SemaphoreSlim sem) : IDisposable
    {
        public void Dispose() => sem.Release();
    }
}

/// <summary>
/// Configuration options for the global <see cref="ConcurrencyBudget"/>.
/// Bound from <c>appsettings.json:Concurrency</c> via <c>IOptions&lt;ConcurrencyOptions&gt;</c>.
/// </summary>
public sealed class ConcurrencyOptions
{
    /// <summary>
    /// Maximum number of concurrent permits available globally.
    /// Defaults to <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public int MaxGlobalConcurrency { get; set; } = Environment.ProcessorCount;
}
