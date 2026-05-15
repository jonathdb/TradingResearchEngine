using Microsoft.Extensions.Options;

namespace TradingResearchEngine.UnitTests.PaperTrading;

/// <summary>
/// A simple <see cref="IOptionsMonitor{TOptions}"/> implementation for unit testing.
/// Supports updating the current value and notifying listeners of changes.
/// </summary>
internal sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
{
    private Action<TOptions, string?>? _listener;

    public TestOptionsMonitor(TOptions initialValue)
    {
        CurrentValue = initialValue;
    }

    public TOptions CurrentValue { get; private set; }

    public TOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        _listener = listener;
        return new ChangeDisposable(this);
    }

    /// <summary>
    /// Simulates a configuration change by updating the current value and notifying listeners.
    /// </summary>
    public void Update(TOptions newValue)
    {
        CurrentValue = newValue;
        _listener?.Invoke(newValue, null);
    }

    private sealed class ChangeDisposable(TestOptionsMonitor<TOptions> owner) : IDisposable
    {
        public void Dispose() => owner._listener = null;
    }
}
