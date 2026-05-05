namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Bollinger Bands indicator computing upper, middle (SMA), and lower bands
/// with a configurable number of standard deviations. Uses a circular buffer
/// for O(1) mean computation and O(n) standard deviation per update.
/// </summary>
public sealed class BollingerBands : IIndicator<BollingerBandsOutput>
{
    private readonly int _period;
    private readonly decimal _numStdDev;
    private readonly decimal[] _buffer;
    private int _index;
    private int _count;
    private decimal _sum;

    /// <summary>
    /// Initialises a new <see cref="BollingerBands"/> indicator.
    /// </summary>
    /// <param name="period">The number of bars for the SMA and standard deviation. Must be at least 2.</param>
    /// <param name="numStdDev">The number of standard deviations for the bands. Defaults to 2.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period"/> is less than 2.</exception>
    public BollingerBands(int period, decimal numStdDev = 2m)
    {
        if (period < 2)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 2.");

        _period = period;
        _numStdDev = numStdDev;
        _buffer = new decimal[period];
    }

    /// <summary>The lookback period for this Bollinger Bands calculation.</summary>
    public int Period => _period;

    /// <inheritdoc />
    public BollingerBandsOutput? Value
    {
        get
        {
            if (!IsReady) return null;

            var mean = _sum / _period;
            var variance = ComputeVariance(mean);
            var stdDev = (decimal)Math.Sqrt((double)variance);

            var upper = mean + (_numStdDev * stdDev);
            var lower = mean - (_numStdDev * stdDev);
            var bandWidth = mean != 0m ? (upper - lower) / mean : 0m;

            return new BollingerBandsOutput(upper, mean, lower, bandWidth);
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
