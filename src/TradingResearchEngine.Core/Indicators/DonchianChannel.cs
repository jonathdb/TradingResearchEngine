namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Donchian Channel indicator computing the highest high and lowest low over a
/// lookback period. Uses circular buffers for efficient rolling max/min tracking.
/// Produces a value after <see cref="Period"/> data points have been received.
/// </summary>
public sealed class DonchianChannel : IIndicator<DonchianChannelOutput>
{
    private readonly int _period;
    private readonly decimal[] _highBuffer;
    private readonly decimal[] _lowBuffer;
    private int _index;
    private int _count;

    /// <summary>
    /// Initialises a new <see cref="DonchianChannel"/> with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars in the channel window. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period"/> is less than 1.</exception>
    public DonchianChannel(int period)
    {
        if (period < 1)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

        _period = period;
        _highBuffer = new decimal[period];
        _lowBuffer = new decimal[period];
    }

    /// <summary>The lookback period for this Donchian Channel calculation.</summary>
    public int Period => _period;

    /// <inheritdoc />
    public DonchianChannelOutput? Value
    {
        get
        {
            if (!IsReady) return null;

            var upper = FindMax();
            var lower = FindMin();
            var middle = (upper + lower) / 2m;

            return new DonchianChannelOutput(upper, lower, middle);
        }
    }

    /// <inheritdoc />
    public bool IsReady => _count >= _period;

    /// <inheritdoc />
    public void Update(decimal close, decimal? high = null, decimal? low = null)
    {
        var h = high ?? close;
        var l = low ?? close;

        _highBuffer[_index] = h;
        _lowBuffer[_index] = l;
        _index = (_index + 1) % _period;

        if (_count < _period)
            _count++;
    }

    /// <inheritdoc />
    public void Reset()
    {
        Array.Clear(_highBuffer);
        Array.Clear(_lowBuffer);
        _index = 0;
        _count = 0;
    }

    private decimal FindMax()
    {
        var max = decimal.MinValue;
        for (var i = 0; i < _period; i++)
        {
            if (_highBuffer[i] > max)
                max = _highBuffer[i];
        }
        return max;
    }

    private decimal FindMin()
    {
        var min = decimal.MaxValue;
        for (var i = 0; i < _period; i++)
        {
            if (_lowBuffer[i] < min)
                min = _lowBuffer[i];
        }
        return min;
    }
}
