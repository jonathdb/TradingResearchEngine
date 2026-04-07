using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>
/// Simulates order execution by applying the active slippage and commission models
/// to produce a <see cref="FillEvent"/>.
/// </summary>
public sealed class SimulatedExecutionHandler : IExecutionHandler
{
    private readonly ISlippageModel _slippage;
    private readonly ICommissionModel _commission;

    /// <inheritdoc cref="SimulatedExecutionHandler"/>
    public SimulatedExecutionHandler(ISlippageModel slippage, ICommissionModel commission)
    {
        _slippage = slippage;
        _commission = commission;
    }

    /// <inheritdoc/>
    public FillEvent Execute(OrderEvent order, MarketDataEvent currentBar)
    {
        decimal basePrice = currentBar switch
        {
            BarEvent bar => bar.Close,
            TickEvent tick => tick.LastTrade.Price,
            _ => throw new InvalidOperationException($"Unsupported MarketDataEvent type: {currentBar.GetType().Name}")
        };
        decimal slippageAmount = _slippage.ComputeAdjustment(order, currentBar);
        decimal fillPrice = order.Direction == Direction.Long
            ? basePrice + slippageAmount
            : basePrice - slippageAmount;

        decimal commission = _commission.ComputeCommission(order, fillPrice, order.Quantity);

        return new FillEvent(
            order.Symbol,
            order.Direction,
            order.Quantity,
            fillPrice,
            commission,
            slippageAmount,
            currentBar.Timestamp);
    }
}
