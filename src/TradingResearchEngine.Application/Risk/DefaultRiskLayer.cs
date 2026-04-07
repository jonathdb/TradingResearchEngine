using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Application.Risk;

/// <summary>
/// Default risk layer implementing FixedFractional position sizing
/// and MaxExposurePercent enforcement.
/// </summary>
public sealed class DefaultRiskLayer : IRiskLayer
{
    private readonly RiskOptions _options;
    private readonly ILogger<DefaultRiskLayer> _logger;

    /// <inheritdoc cref="DefaultRiskLayer"/>
    public DefaultRiskLayer(IOptions<RiskOptions> options, ILogger<DefaultRiskLayer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public OrderEvent? ConvertSignal(SignalEvent signal, PortfolioSnapshot snapshot)
    {
        // Flat signal = close existing position for this symbol
        if (signal.Direction == Direction.Flat)
        {
            if (snapshot.Positions.TryGetValue(signal.Symbol, out var pos) && pos.Quantity > 0)
            {
                return new OrderEvent(
                    signal.Symbol, Direction.Short, pos.Quantity,
                    OrderType.Market, null, signal.Timestamp, true);
            }
            return null; // no position to close
        }

        // FixedFractional: risk 2% of equity per trade
        const decimal fractionPerTrade = 0.02m;
        decimal riskBudget = snapshot.TotalEquity * fractionPerTrade;

        // Use signal Strength as a price hint when available; otherwise default to 1
        // (treating quantity as dollar-denominated notional units)
        decimal referencePrice = signal.Strength.HasValue && signal.Strength.Value > 0
            ? signal.Strength.Value
            : 1m;

        decimal quantity = Math.Floor(riskBudget / referencePrice);

        var order = new OrderEvent(
            signal.Symbol,
            signal.Direction,
            quantity,
            OrderType.Market,
            null,
            signal.Timestamp);

        return EvaluateOrder(order, snapshot);
    }

    /// <inheritdoc/>
    public OrderEvent? EvaluateOrder(OrderEvent order, PortfolioSnapshot snapshot)
    {
        if (order.Quantity <= 0m)
        {
            _logger.LogWarning("RiskRejection: order for {Symbol} has zero or negative quantity.", order.Symbol);
            return null;
        }

        // Enforce MaxExposurePercent
        decimal maxExposure = snapshot.TotalEquity * (_options.MaxExposurePercent / 100m);
        decimal currentExposure = snapshot.Positions.Values.Sum(p => p.Quantity * p.AverageEntryPrice);
        decimal remainingCapacity = maxExposure - currentExposure;

        if (remainingCapacity <= 0m)
        {
            _logger.LogWarning("RiskRejection: {Symbol} order rejected — portfolio at max exposure ({Max:P0}).",
                order.Symbol, _options.MaxExposurePercent / 100m);
            return null;
        }

        return order;
    }
}
