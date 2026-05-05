namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Relative Strength Index indicator using Wilder's smoothing for average gain and loss.
/// Produces a value between 0 and 100 after <see cref="Period"/> + 1 data points
/// (requires one extra point to compute the first change).
/// </summary>
public sealed class RelativeStrengthIndex : IIndicator<decimal>
{
    private readonly int _period;
    private decimal _avgGain;
    private decimal _avgLoss;
    private decimal _previousClose;
    private int _count;
    private decimal _gainSum;
    private decimal _lossSum;

    /// <summary>
    /// Initialises a new <see cref="RelativeStrengthIndex"/> with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars for the RSI calculation. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period"/> is less than 1.</exception>
    public RelativeStrengthIndex(int period)
    {
        if (period < 1)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

        _period = period;
    }

    /// <summary>The lookback period for this RSI calculation.</summary>
    public int Period => _period;

    /// <inheritdoc />
    public decimal? Value
    {
        get
        {
            if (!IsReady) return null;

            if (_avgLoss == 0m)
                return 100m;

            var rs = _avgGain / _avgLoss;
            return 100m - (100m / (1m + rs));
        }
    }

    /// <inheritdoc />
    public bool IsReady => _count > _period;

    /// <inheritdoc />
    public void Update(decimal close, decimal? high = null, decimal? low = null)
    {
        _count++;

        if (_count == 1)
        {
            // First data point — no change to compute yet
            _previousClose = close;
            return;
        }

        var change = close - _previousClose;
        _previousClose = close;

        var gain = change > 0 ? change : 0m;
        var loss = change < 0 ? -change : 0m;

        if (_count <= _period + 1)
        {
            // Accumulating the initial period
            _gainSum += gain;
            _lossSum += loss;

            if (_count == _period + 1)
            {
                // First RSI value: simple average of gains/losses
                _avgGain = _gainSum / _period;
                _avgLoss = _lossSum / _period;
            }
        }
        else
        {
            // Wilder smoothing
            _avgGain = ((_avgGain * (_period - 1)) + gain) / _period;
            _avgLoss = ((_avgLoss * (_period - 1)) + loss) / _period;
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _avgGain = 0m;
        _avgLoss = 0m;
        _previousClose = 0m;
        _count = 0;
        _gainSum = 0m;
        _lossSum = 0m;
    }
}
