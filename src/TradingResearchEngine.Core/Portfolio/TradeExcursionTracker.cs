using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Portfolio;

/// <summary>
/// Tracks intra-trade price extremes for computing Maximum Adverse Excursion (MAE)
/// and Maximum Favorable Excursion (MFE) during an open position's lifetime.
/// MAE/MFE are expressed as fractions of entry value.
/// </summary>
public sealed class TradeExcursionTracker
{
    private readonly decimal _entryPrice;
    private readonly decimal _quantity;
    private readonly Direction _direction;

    private decimal _maxFavorablePrice;
    private decimal _maxAdversePrice;
    private bool _hasUpdates;

    /// <summary>
    /// Initialises a new excursion tracker for a trade.
    /// </summary>
    /// <param name="entryPrice">The average entry price of the position.</param>
    /// <param name="quantity">The position quantity (absolute value).</param>
    /// <param name="direction">The trade direction (Long or Short).</param>
    public TradeExcursionTracker(decimal entryPrice, decimal quantity, Direction direction)
    {
        _entryPrice = entryPrice;
        _quantity = quantity;
        _direction = direction;
        _maxFavorablePrice = entryPrice;
        _maxAdversePrice = entryPrice;
    }

    /// <summary>
    /// Updates the tracker with a full OHLC bar. Uses intra-bar High/Low extremes
    /// based on position direction for accurate MAE/MFE computation.
    /// </summary>
    /// <param name="bar">The bar record containing Open, High, Low, Close prices.</param>
    public void UpdateBar(BarRecord bar)
    {
        _hasUpdates = true;

        if (_direction == Direction.Long)
        {
            // For longs: favorable = bar.High (best price), adverse = bar.Low (worst price)
            decimal favorablePrice = bar.High;
            decimal adversePrice = bar.Low;

            if (favorablePrice > _maxFavorablePrice)
                _maxFavorablePrice = favorablePrice;
            if (adversePrice < _maxAdversePrice)
                _maxAdversePrice = adversePrice;
        }
        else if (_direction == Direction.Short)
        {
            // For shorts: favorable = bar.Low (best price), adverse = bar.High (worst price)
            decimal favorablePrice = bar.Low;
            decimal adversePrice = bar.High;

            if (favorablePrice < _maxFavorablePrice)
                _maxFavorablePrice = favorablePrice;
            if (adversePrice > _maxAdversePrice)
                _maxAdversePrice = adversePrice;
        }
    }

    /// <summary>
    /// Convenience overload that constructs a synthetic bar from a single price
    /// (Open = High = Low = Close = price) and delegates to <see cref="UpdateBar"/>.
    /// Preserves backward compatibility for callers that only have a close price.
    /// </summary>
    /// <param name="price">The current market price for the symbol.</param>
    public void UpdatePrice(decimal price)
    {
        var syntheticBar = new BarRecord(
            Symbol: string.Empty,
            Interval: string.Empty,
            Open: price,
            High: price,
            Low: price,
            Close: price,
            Volume: 0m,
            Timestamp: DateTimeOffset.MinValue);

        UpdateBar(syntheticBar);
    }

    /// <summary>
    /// Builds a <see cref="TradeAnatomy"/> record from the tracked excursion data.
    /// MAE and MFE are expressed as fractions of entry value.
    /// </summary>
    /// <param name="entryTime">The trade entry timestamp.</param>
    /// <param name="exitTime">The trade exit timestamp.</param>
    /// <returns>A <see cref="TradeAnatomy"/> with computed MAE, MFE, and duration.</returns>
    public TradeAnatomy BuildAnatomy(DateTimeOffset entryTime, DateTimeOffset exitTime)
    {
        TimeSpan duration = exitTime - entryTime;

        if (!_hasUpdates || _entryPrice == 0m)
        {
            return new TradeAnatomy(null, null, duration);
        }

        decimal mae;
        decimal mfe;

        if (_direction == Direction.Long)
        {
            // MAE: worst drawdown from entry (negative or zero fraction)
            mae = (_maxAdversePrice - _entryPrice) / _entryPrice;
            // MFE: best run-up from entry (positive or zero fraction)
            mfe = (_maxFavorablePrice - _entryPrice) / _entryPrice;
        }
        else
        {
            // Short: MAE is adverse move up, MFE is favorable move down
            // MAE: worst adverse move (price went up = loss for short)
            mae = (_entryPrice - _maxAdversePrice) / _entryPrice;
            // MFE: best favorable move (price went down = gain for short)
            mfe = (_entryPrice - _maxFavorablePrice) / _entryPrice;
        }

        return new TradeAnatomy(mae, mfe, duration);
    }
}
