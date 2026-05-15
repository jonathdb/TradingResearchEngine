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
    /// Updates the tracker with the current market price.
    /// Tracks the highest and lowest prices seen during the trade's lifetime.
    /// </summary>
    /// <param name="currentPrice">The current market price for the symbol.</param>
    public void UpdatePrice(decimal currentPrice)
    {
        _hasUpdates = true;

        if (_direction == Direction.Long)
        {
            // For longs: favorable = price goes up, adverse = price goes down
            if (currentPrice > _maxFavorablePrice)
                _maxFavorablePrice = currentPrice;
            if (currentPrice < _maxAdversePrice)
                _maxAdversePrice = currentPrice;
        }
        else if (_direction == Direction.Short)
        {
            // For shorts: favorable = price goes down, adverse = price goes up
            if (currentPrice < _maxFavorablePrice)
                _maxFavorablePrice = currentPrice;
            if (currentPrice > _maxAdversePrice)
                _maxAdversePrice = currentPrice;
        }
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
