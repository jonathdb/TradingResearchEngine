namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Rolling Z-Score indicator computing (value - mean) / stddev over a lookback window.
/// Uses a circular buffer for efficient rolling statistics. Produces a value after
/// <see cref="Period"/> data points have been received.
/// </summary>
public sealed class RollingZScore : IIndicator<decimal>
{
    private readonly int _period;
    private readonly decimal[] _buffer;
    private int _index;
    private int _count;
    private decimal _sum;

    /// <summary>
    /// Initialises a new <see cref="RollingZScore"/> with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars in the rolling window. Must be at least 2.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period"/> is less than 2.</exception>
    public RollingZScore(int period)
    {
        if (period < 2)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 2.");

        _period = period;
        _buffer = new decimal[period];
    }

    /// <summary>The lookback period for this rolling Z-Score calculation.</summary>
    public int Period => _period;

    /// <inheritdoc />
    public decimal? Value
    {
        get
        {
            if (!IsReady) return null;

            var mean = _sum / _period;
            var variance = ComputeVariance(mean);
            var stdDev = (decimal)Math.Sqrt((double)variance);

            if (stdDev == 0m)
                return 0m;

            // The most recent value is at (_index - 1 + _period) % _period
            var latestIndex = (_index - 1 + _period) % _period;
            return (_buffer[latestIndex] - mean) / stdDev;
        }
    }

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

    private decimal ComputeVariance(decimal mean)
    {
        decimal sumSquaredDiff = 0m;
        for (var i = 0; i < _period; i++)
        {
            var diff = _buffer[i] - mean;
            sumSquaredDiff += diff * diff;
        }
        return sumSquaredDiff / _period;
    }
}
