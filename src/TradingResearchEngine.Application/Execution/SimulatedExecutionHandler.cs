using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>Named constants for execution realism thresholds.</summary>
public static class ExecutionRealismDefaults
{
    /// <summary>Gap detection threshold: price jump must exceed this multiple of ATR.</summary>
    public const decimal GapAtrMultiple = 2.0m;

    /// <summary>Number of bars used for rolling ATR computation.</summary>
    public const int AtrPeriod = 14;

    /// <summary>Volume warning threshold: fills exceeding this fraction of bar volume trigger an advisory.</summary>
    public const decimal VolumeWarningThreshold = 0.10m;
}

/// <summary>
/// Simulates order execution by applying the active slippage and commission models
/// to produce a <see cref="FillEvent"/> wrapped in an <see cref="ExecutionResult"/>.
/// V5: Supports gap detection with gap-adjusted fill prices and volume constraint enforcement.
/// </summary>
public sealed class SimulatedExecutionHandler : IExecutionHandler
{
    private readonly ISlippageModel _slippage;
    private readonly ICommissionModel _commission;
    private readonly ILogger<SimulatedExecutionHandler> _logger;
    private readonly List<string> _realismAdvisories = new();

    // Gap detection state
    private decimal? _previousBarClose;
    private readonly decimal[] _trueRanges;
    private int _trueRangeCount;
    private int _trueRangeIndex;
    private decimal _rollingAtr;

    // Volume constraint
    private readonly decimal? _maxFillPercentOfVolume;

    /// <summary>Realism advisories collected during the simulation run.</summary>
    public IReadOnlyList<string> RealismAdvisories => _realismAdvisories;

    /// <inheritdoc cref="SimulatedExecutionHandler"/>
    public SimulatedExecutionHandler(
        ISlippageModel slippage,
        ICommissionModel commission,
        ILogger<SimulatedExecutionHandler> logger,
        decimal? maxFillPercentOfVolume = null)
    {
        _slippage = slippage;
        _commission = commission;
        _logger = logger;
        _maxFillPercentOfVolume = maxFillPercentOfVolume;
        _trueRanges = new decimal[ExecutionRealismDefaults.AtrPeriod];
    }

    /// <summary>
    /// Updates gap detection state from a bar. Should be called on every bar
    /// before processing pending orders, so that ATR and previous close are current.
    /// </summary>
    public void UpdateBarState(BarEvent bar)
    {
        if (_previousBarClose.HasValue)
        {
            decimal trueRange = Math.Max(
                bar.High - bar.Low,
                Math.Max(
                    Math.Abs(bar.High - _previousBarClose.Value),
                    Math.Abs(bar.Low - _previousBarClose.Value)));

            _trueRanges[_trueRangeIndex] = trueRange;
            _trueRangeIndex = (_trueRangeIndex + 1) % ExecutionRealismDefaults.AtrPeriod;
            _trueRangeCount = Math.Min(_trueRangeCount + 1, ExecutionRealismDefaults.AtrPeriod);

            if (_trueRangeCount > 0)
            {
                decimal sum = 0m;
                for (int i = 0; i < _trueRangeCount; i++)
                    sum += _trueRanges[i];
                _rollingAtr = sum / _trueRangeCount;
            }
        }

        _previousBarClose = bar.Close;
    }

    /// <inheritdoc/>
    public ExecutionResult Execute(OrderEvent order, MarketDataEvent currentBar)
    {
        LongOnlyGuard.EnsureLongOnly(order.Direction);

        decimal basePrice = currentBar switch
        {
            BarEvent bar => ResolveBarFillPrice(bar, order),
            TickEvent tick => GetTickFillPrice(tick, order.Direction),
            _ => throw new InvalidOperationException($"Unsupported MarketDataEvent type: {currentBar.GetType().Name}")
        };

        decimal slippageAmount = _slippage.ComputeAdjustment(order, currentBar);
        decimal fillPrice = order.Direction == Direction.Long
            ? basePrice + slippageAmount
            : basePrice - slippageAmount;

        decimal quantity = order.Quantity;

        // Volume constraint enforcement (bar data only)
        if (currentBar is BarEvent volumeBar && volumeBar.Volume > 0m)
        {
            quantity = EnforceVolumeConstraints(quantity, volumeBar, order.Symbol);
        }

        decimal commission = _commission.ComputeCommission(order, fillPrice, quantity);

        var fill = new FillEvent(
            order.Symbol,
            order.Direction,
            quantity,
            fillPrice,
            commission,
            slippageAmount,
            currentBar.Timestamp);

        return new ExecutionResult(ExecutionOutcome.Filled, fill);
    }

    /// <summary>
    /// Resolves the fill price for a bar, applying gap-adjusted pricing when a gap is detected.
    /// For stop/limit orders triggered during a gap, fills at the gap bar's Open price.
    /// </summary>
    private decimal ResolveBarFillPrice(BarEvent bar, OrderEvent order)
    {
        bool isGap = DetectGap(bar);

        // For stop/limit orders during a gap, fill at the gap bar's Open price
        if (isGap && order.OrderType is OrderType.StopMarket or OrderType.StopLimit or OrderType.Limit)
        {
            _realismAdvisories.Add(
                $"Gap detected for {order.Symbol} at {bar.Timestamp:yyyy-MM-dd}: " +
                $"fill adjusted from {(order.StopPrice ?? order.LimitPrice ?? bar.Close):F4} to Open {bar.Open:F4}.");
            return bar.Open;
        }

        return bar.Close;
    }

    /// <summary>
    /// Detects an overnight/weekend gap: price jump from previous bar close to current bar open
    /// exceeds 2× the rolling ATR.
    /// </summary>
    private bool DetectGap(BarEvent bar)
    {
        if (!_previousBarClose.HasValue || _rollingAtr <= 0m || _trueRangeCount == 0)
            return false;

        decimal gapSize = Math.Abs(bar.Open - _previousBarClose.Value);
        return gapSize > ExecutionRealismDefaults.GapAtrMultiple * _rollingAtr;
    }

    /// <summary>
    /// Enforces volume constraints: caps fill quantity at MaxFillPercentOfVolume × Volume when set,
    /// and logs a warning when fill exceeds 10% of bar volume regardless of setting.
    /// </summary>
    private decimal EnforceVolumeConstraints(decimal requestedQuantity, BarEvent bar, string symbol)
    {
        decimal quantity = requestedQuantity;

        // Cap fill at MaxFillPercentOfVolume × Volume when configured
        if (_maxFillPercentOfVolume.HasValue)
        {
            decimal maxQuantity = _maxFillPercentOfVolume.Value * bar.Volume;
            if (quantity > maxQuantity)
            {
                _realismAdvisories.Add(
                    $"Volume cap for {symbol} at {bar.Timestamp:yyyy-MM-dd}: " +
                    $"fill capped from {quantity:F2} to {maxQuantity:F2} " +
                    $"({_maxFillPercentOfVolume.Value:P0} of volume {bar.Volume:F0}).");
                quantity = maxQuantity;
            }
        }

        // Always warn when fill exceeds 10% of bar volume
        if (quantity > ExecutionRealismDefaults.VolumeWarningThreshold * bar.Volume)
        {
            _logger.LogWarning(
                "VolumeWarning: fill quantity {Quantity:F2} for {Symbol} exceeds 10% of bar volume {Volume:F0}.",
                quantity, symbol, bar.Volume);
            _realismAdvisories.Add(
                $"Volume warning for {symbol} at {bar.Timestamp:yyyy-MM-dd}: " +
                $"fill {quantity:F2} exceeds 10% of bar volume {bar.Volume:F0}.");
        }

        return quantity;
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
