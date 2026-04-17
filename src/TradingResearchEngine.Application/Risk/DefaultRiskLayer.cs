using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Application.Risk;

/// <summary>
/// Default risk layer. Delegates position sizing to <see cref="IPositionSizingPolicy"/>
/// and enforces MaxExposurePercent plus optional <see cref="PortfolioConstraints"/>.
/// </summary>
public sealed class DefaultRiskLayer : IRiskLayer
{
    private readonly RiskOptions _options;
    private readonly PortfolioConstraints? _constraints;
    private readonly IPositionSizingPolicy _sizingPolicy;
    private readonly ILogger<DefaultRiskLayer> _logger;
    private MarketDataEvent? _lastMarket;

    /// <inheritdoc cref="DefaultRiskLayer"/>
    public DefaultRiskLayer(
        IOptions<RiskOptions> options,
        ILogger<DefaultRiskLayer> logger,
        IPositionSizingPolicy? sizingPolicy = null,
        PortfolioConstraints? constraints = null)
    {
        _options = options.Value;
        _logger = logger;
        _sizingPolicy = sizingPolicy ?? new PercentEquitySizingPolicy(0.02m);
        _constraints = constraints;
    }

    /// <summary>Updates the last market event for sizing policy context.</summary>
    public void UpdateMarketData(MarketDataEvent market) => _lastMarket = market;

    /// <inheritdoc/>
    public OrderEvent? ConvertSignal(SignalEvent signal, PortfolioSnapshot snapshot)
    {
        // Flat signal = close existing position for this symbol (long or short)
        if (signal.Direction == Direction.Flat)
        {
            // Check for open long position
            if (snapshot.Positions.TryGetValue(signal.Symbol, out var longPos) && longPos.Quantity > 0)
            {
                return new OrderEvent(
                    signal.Symbol, Direction.Flat, longPos.Quantity,
                    OrderType.Market, null, signal.Timestamp, true);
            }

            // Check for open short position
            if (snapshot.ShortPositions is not null &&
                snapshot.ShortPositions.TryGetValue(signal.Symbol, out var shortPos) && shortPos.Quantity > 0)
            {
                return new OrderEvent(
                    signal.Symbol, Direction.Flat, shortPos.Quantity,
                    OrderType.Market, null, signal.Timestamp, true);
            }

            return null;
        }

        // Check for opposing position when AllowReversals is false
        if (signal.Direction == Direction.Short &&
            snapshot.Positions.TryGetValue(signal.Symbol, out var existingLong) && existingLong.Quantity > 0)
        {
            _logger.LogWarning("RiskRejection: Short signal for {Symbol} rejected — open long position exists and AllowReversals is false.", signal.Symbol);
            return null;
        }

        if (signal.Direction == Direction.Long &&
            snapshot.ShortPositions is not null &&
            snapshot.ShortPositions.TryGetValue(signal.Symbol, out var existingShort) && existingShort.Quantity > 0)
        {
            _logger.LogWarning("RiskRejection: Long signal for {Symbol} rejected — open short position exists and AllowReversals is false.", signal.Symbol);
            return null;
        }

        // Delegate sizing to the active policy
        var dummyMarket = _lastMarket ?? CreateDummyMarket(signal);
        decimal quantity = _sizingPolicy.ComputeSize(signal, snapshot, dummyMarket);

        if (quantity <= 0m)
        {
            _logger.LogWarning("RiskRejection: sizing policy returned zero quantity for {Symbol}.", signal.Symbol);
            return null;
        }

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

        // Enforce MaxExposurePercent using absolute values for both long and short
        decimal maxExposure = snapshot.TotalEquity * (_options.MaxExposurePercent / 100m);
        decimal currentExposure = snapshot.Positions.Values.Sum(p => Math.Abs(p.Quantity * p.AverageEntryPrice));
        if (snapshot.ShortPositions is not null)
            currentExposure += snapshot.ShortPositions.Values.Sum(p => Math.Abs(p.Quantity * p.AverageEntryPrice));
        decimal remainingCapacity = maxExposure - currentExposure;

        if (remainingCapacity <= 0m)
        {
            _logger.LogWarning("RiskRejection: {Symbol} order rejected — portfolio at max exposure ({Max:P0}).",
                order.Symbol, _options.MaxExposurePercent / 100m);
            return null;
        }

        // Optional portfolio constraints
        if (_constraints is not null && (order.Direction == Direction.Long || order.Direction == Direction.Short))
        {
            if (_constraints.MaxConcurrentPositions.HasValue)
            {
                int totalPositions = snapshot.Positions.Count;
                if (snapshot.ShortPositions is not null)
                    totalPositions += snapshot.ShortPositions.Count;
                if (totalPositions >= _constraints.MaxConcurrentPositions.Value)
                {
                    _logger.LogWarning("RiskRejection: {Symbol} — max concurrent positions ({Max}) reached.",
                        order.Symbol, _constraints.MaxConcurrentPositions.Value);
                    return null;
                }
            }

            if (_constraints.MaxCapitalPerSymbolPercent.HasValue)
            {
                decimal maxPerSymbol = snapshot.TotalEquity * (_constraints.MaxCapitalPerSymbolPercent.Value / 100m);
                decimal price = order.LimitPrice ?? GetOrderPrice(order);
                decimal orderValue = price * order.Quantity;
                if (orderValue > maxPerSymbol)
                {
                    _logger.LogWarning("RiskRejection: {Symbol} — order value {Value:F2} exceeds max per symbol {Max:F2}.",
                        order.Symbol, orderValue, maxPerSymbol);
                    return null;
                }
            }

            if (_constraints.MaxGrossExposurePercent.HasValue)
            {
                decimal maxGross = snapshot.TotalEquity * (_constraints.MaxGrossExposurePercent.Value / 100m);
                if (currentExposure >= maxGross)
                {
                    _logger.LogWarning("RiskRejection: {Symbol} — gross exposure at max ({Max:P0}).",
                        order.Symbol, _constraints.MaxGrossExposurePercent.Value / 100m);
                    return null;
                }
            }

            if (_constraints.MaxExposurePerSymbol.HasValue)
            {
                decimal maxPerSymbolExposure = snapshot.TotalEquity * (_constraints.MaxExposurePerSymbol.Value / 100m);
                decimal existingSymbolExposure = 0m;
                if (snapshot.Positions.TryGetValue(order.Symbol, out var existingPos))
                    existingSymbolExposure = existingPos.Quantity * existingPos.AverageEntryPrice;

                decimal price = order.LimitPrice ?? GetOrderPrice(order);
                decimal newSymbolExposure = existingSymbolExposure + (price * order.Quantity);
                if (newSymbolExposure > maxPerSymbolExposure)
                {
                    _logger.LogWarning(
                        "RiskRejection: {Symbol} — order would cause symbol exposure {Exposure:F2} to exceed max per-symbol limit {Max:F2}.",
                        order.Symbol, newSymbolExposure, maxPerSymbolExposure);
                    return null;
                }
            }
        }

        return order;
    }

    private static decimal GetOrderPrice(OrderEvent order) => order.StopPrice ?? 1m;

    private static MarketDataEvent CreateDummyMarket(SignalEvent signal)
    {
        decimal price = signal.Strength ?? 1m;
        return new BarEvent(signal.Symbol, "1D", price, price, price, price, 0m, signal.Timestamp);
    }
}
