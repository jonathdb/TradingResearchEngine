namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Simple Moving Average indicator using a circular buffer for O(1) per-update computation.
/// Produces a value after <see cref="Period"/> data points have been received.
/// </summary>
public sealed class SimpleMovingAverage : IIndicator<decimal>
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;
    private int _count;
    private decimal _sum;

    /// <summary>
    /// Initialises a new <see cref="SimpleMovingAverage"/> with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars in the moving average window. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period"/> is less than 1.</exception>
    public SimpleMovingAverage(int period)
    {
        if (period < 1)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

        _period = period;
        _buffer = new decimal[period];
    }

    /// <summary>The lookback period for this moving average.</summary>
    public int Period => _period;

    /// <inheritdoc />
    public decimal? Value => IsReady ? _sum / _period : null;

    /// <inheritdoc />
    public bool IsReady => _count >= _period;

    /// <inheritdoc />
    public void Update(decimal close, decimal? high = null, decimal? low = null)
    {
        if (_count >= _period)
        {
            _sum -= _buffer[_index];
        }

        _buffer[_index] = close;
        _sum += close;
        _index = (_index + 1) % _period;

        if (_count < _period)
            _count++;
    }

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(_buffer);
        _index = 0;
        _count = 0;
        _sum = 0m;
    }
}
