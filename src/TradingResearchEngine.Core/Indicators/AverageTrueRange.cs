namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Average True Range indicator using Wilder's smoothing method.
/// Requires high and low prices for each bar. The first ATR value is the simple
/// average of the first <see cref="Period"/> true range values; subsequent values
/// use Wilder smoothing: ATR = ((ATR_prev * (Period - 1)) + TR) / Period.
/// </summary>
public sealed class AverageTrueRange : IIndicator<decimal>
{
    private readonly int _period;
    private decimal _atr;
    private decimal _previousClose;
    private decimal _trSum;
    private int _count;
    private bool _hasPreviousClose;

    /// <summary>
    /// Initialises a new <see cref="AverageTrueRange"/> with the specified lookback period.
    /// </summary>
    /// <param name="period">The number of bars for the ATR calculation. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="period"/> is less than 1.</exception>
    public AverageTrueRange(int period)
    {
        if (period < 1)
            throw new ArgumentOutOfRangeException(nameof(period), "Period must be at least 1.");

        _period = period;
    }

    /// <summary>The lookback period for this ATR calculation.</summary>
    public int Period => _period;

    /// <inheritdoc />
    public decimal? Value => IsReady ? _atr : null;

    /// <inheritdoc />
    public bool IsReady => _count >= _period;

    /// <inheritdoc />
    public void Update(decimal close, decimal? high = null, decimal? low = null)
    {
        var h = high ?? close;
        var l = low ?? close;

        decimal trueRange;

        if (!_hasPreviousClose)
        {
            // First bar: true range is simply high - low
            trueRange = h - l;
            _hasPreviousClose = true;
        }
        else
        {
            // True Range = max(H-L, |H-PrevClose|, |L-PrevClose|)
            var hl = h - l;
            var hpc = Math.Abs(h - _previousClose);
            var lpc = Math.Abs(l - _previousClose);
            trueRange = Math.Max(hl, Math.Max(hpc, lpc));
        }

        _previousClose = close;
        _count++;

        if (_count <= _period)
        {
            _trSum += trueRange;

            if (_count == _period)
            {
                _atr = _trSum / _period;
            }
        }
        else
        {
            // Wilder smoothing
            _atr = ((_atr * (_period - 1)) + trueRange) / _period;
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _atr = 0m;
        _previousClose = 0m;
        _trSum = 0m;
        _count = 0;
        _hasPreviousClose = false;
    }
}
