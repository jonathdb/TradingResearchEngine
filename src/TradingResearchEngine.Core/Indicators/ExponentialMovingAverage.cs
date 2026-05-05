namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Exponential Moving Average indicator using the standard multiplier formula: 2 / (Period + 1).
/// Uses the first <see cref="Period"/> values as a simple average seed, then applies
/// exponential smoothing for subsequent updates.
/// </summary>
public sealed class ExponentialMovingAverage : IIndicator<decimal>
{
    private readonly int _period;
    private readonly decimal _multiplier;
    private decimal _sum;
    private decimal _ema;
    private int _count;

    /// <summary>
    /// Initialises a new <see cref="ExponentialMovingAverage"/> with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars for the warmup seed and smoothing factor. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period"/> is less than 1.</exception>
    public ExponentialMovingAverage(int period)
    {
        if (period < 1)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

        _period = period;
        _multiplier = 2m / (period + 1m);
    }

    /// <summary>The lookback period for this exponential moving average.</summary>
    public int Period => _period;

    /// <inheritdoc />
    public decimal? Value => IsReady ? _ema : null;

    /// <inheritdoc />
    public bool IsReady => _count >= _period;

    /// <inheritdoc />
    public void Update(decimal close, decimal? high = null, decimal? low = null)
    {
        _count++;

        if (_count <= _period)
        {
            _sum += close;

            if (_count == _period)
            {
                _ema = _sum / _period;
            }
        }
        else
        {
            _ema = (close - _ema) * _multiplier + _ema;
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _sum = 0m;
        _ema = 0m;
        _count = 0;
    }
}
