using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>
/// Scales slippage as configurable basis points of the execution price.
/// Deterministic given the same inputs.
/// </summary>
public sealed class PercentOfPriceSlippageModel : ISlippageModel
{
    private readonly decimal _basisPoints;

    /// <param name="basisPoints">Slippage in basis points (default 5 = 0.05%).</param>
    public PercentOfPriceSlippageModel(decimal basisPoints = 5m)
    {
        _basisPoints = basisPoints;
    }

    /// <inheritdoc/>
    public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market)
    {
        decimal basePrice = market switch
        {
            BarEvent bar => bar.Close,
            TickEvent tick => tick.LastTrade.Price,
            _ => 0m
        };
        return basePrice * _basisPoints / 10_000m;
    }
}
