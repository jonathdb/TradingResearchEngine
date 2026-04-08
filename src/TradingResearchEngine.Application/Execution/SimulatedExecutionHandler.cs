using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>
/// Simulates order execution by applying the active slippage and commission models
/// to produce a <see cref="FillEvent"/> wrapped in an <see cref="ExecutionResult"/>.
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
    public ExecutionResult Execute(OrderEvent order, MarketDataEvent currentBar)
    {
        decimal basePrice = currentBar switch
        {
            BarEvent bar => bar.Close,
            TickEvent tick => GetTickFillPrice(tick, order.Direction),
            _ => throw new InvalidOperationException($"Unsupported MarketDataEvent type: {currentBar.GetType().Name}")
        };
        decimal slippageAmount = _slippage.ComputeAdjustment(order, currentBar);
        decimal fillPrice = order.Direction == Direction.Long
            ? basePrice + slippageAmount
            : basePrice - slippageAmount;

        decimal commission = _commission.ComputeCommission(order, fillPrice, order.Quantity);

        var fill = new FillEvent(
            order.Symbol,
            order.Direction,
            order.Quantity,
            fillPrice,
            commission,
            slippageAmount,
            currentBar.Timestamp);

        return new ExecutionResult(ExecutionOutcome.Filled, fill);
    }

    /// <summary>
    /// Returns the appropriate tick fill price based on direction.
    /// Long fills at Ask, Flat (close) fills at Bid. Falls back to LastTrade.Price.
    /// </summary>
    private static decimal GetTickFillPrice(TickEvent tick, Direction direction)
    {
        if (direction == Direction.Long && tick.Ask is not null)
            return tick.Ask.Price;
        if (direction == Direction.Flat && tick.Bid is not null)
            return tick.Bid.Price;
        return tick.LastTrade.Price;
    }
}
