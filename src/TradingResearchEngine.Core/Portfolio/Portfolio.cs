using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Core.Portfolio;

/// <summary>
/// Tracks open positions, cash balance, and equity curve by consuming <see cref="FillEvent"/> instances.
/// </summary>
public sealed class Portfolio
{
    private readonly ILogger<Portfolio> _logger;
    private readonly Dictionary<string, PositionState> _positions = new();
    private readonly List<EquityCurvePoint> _equityCurve = new();
    private readonly List<ClosedTrade> _closedTrades = new();

    /// <summary>Initialises the portfolio with a starting cash balance.</summary>
    public Portfolio(decimal initialCash, ILogger<Portfolio> logger)
    {
        CashBalance = initialCash;
        StartEquity = initialCash;
        TotalEquity = initialCash;
        _logger = logger;
    }

    /// <summary>The initial cash balance at the start of the simulation.</summary>
    public decimal StartEquity { get; }

    /// <summary>Current cash balance. Clamped to zero on margin breach.</summary>
    public decimal CashBalance { get; private set; }

    /// <summary>Cash plus sum of unrealised P&amp;L across all open positions.</summary>
    public decimal TotalEquity { get; private set; }

    /// <summary>Open positions keyed by symbol.</summary>
    public IReadOnlyDictionary<string, Position> Positions =>
        _positions.ToDictionary(kv => kv.Key, kv => kv.Value.ToPosition());

    /// <summary>Time-ordered equity snapshots, one per fill.</summary>
    public IReadOnlyList<EquityCurvePoint> EquityCurve => _equityCurve;

    /// <summary>All completed round-trip trades.</summary>
    public IReadOnlyList<ClosedTrade> ClosedTrades => _closedTrades;

    /// <summary>Returns an immutable snapshot of current portfolio state for the RiskLayer.</summary>
    public PortfolioSnapshot TakeSnapshot() =>
        new(Positions, CashBalance, TotalEquity);

    /// <summary>
    /// Updates portfolio state from a fill. Logs a <c>MarginBreachWarning</c> if cash would go negative.
    /// </summary>
    public void Update(FillEvent fill)
    {
        if (fill.Direction == Direction.Long)
        {
            decimal cost = fill.FillPrice * fill.Quantity + fill.Commission;
            decimal newCash = CashBalance - cost;
            if (newCash < 0m)
            {
                _logger.LogWarning("MarginBreachWarning: fill {Symbol} would reduce cash to {Amount:F2}; clamping to 0.",
                    fill.Symbol, newCash);
                newCash = 0m;
            }
            CashBalance = newCash;
            ApplyBuyFill(fill);
        }
        else if (fill.Direction == Direction.Short || fill.Direction == Direction.Flat)
        {
            decimal proceeds = fill.FillPrice * fill.Quantity - fill.Commission;
            CashBalance += proceeds;
            ApplySellFill(fill);
        }

        MarkToMarketFromFill(fill);
        RecalculateTotalEquity();
        _equityCurve.Add(new EquityCurvePoint(fill.Timestamp, TotalEquity));
    }

    private void ApplyBuyFill(FillEvent fill)
    {
        if (!_positions.TryGetValue(fill.Symbol, out var state))
        {
            state = new PositionState(fill.Symbol);
            _positions[fill.Symbol] = state;
        }
        state.AddBuy(fill.Quantity, fill.FillPrice, fill.Timestamp);
    }

    private void ApplySellFill(FillEvent fill)
    {
        if (!_positions.TryGetValue(fill.Symbol, out var state) || state.Quantity == 0m)
            return;

        decimal closedQty = Math.Min(fill.Quantity, state.Quantity);
        decimal grossPnl = (fill.FillPrice - state.AverageEntryPrice) * closedQty;
        decimal netPnl = grossPnl - fill.Commission;

        _closedTrades.Add(new ClosedTrade(
            fill.Symbol,
            state.EntryTime,
            fill.Timestamp,
            state.AverageEntryPrice,
            fill.FillPrice,
            closedQty,
            Direction.Long,
            grossPnl,
            fill.Commission,
            netPnl));

        state.ReducePosition(closedQty, netPnl);
        if (state.Quantity == 0m) _positions.Remove(fill.Symbol);
    }

    private void RecalculateTotalEquity()
    {
        decimal unrealised = _positions.Values.Sum(p => p.UnrealisedPnl);
        TotalEquity = CashBalance + unrealised;
    }

    /// <summary>
    /// Updates unrealised P&amp;L for all open positions using the latest market price.
    /// Called internally after each fill; can also be called with a market price update.
    /// </summary>
    internal void MarkToMarket(decimal currentPrice, string symbol)
    {
        if (_positions.TryGetValue(symbol, out var state))
            state.UpdateUnrealisedPnl(currentPrice);
    }

    /// <summary>
    /// Updates unrealised P&amp;L for a position using the fill price as a proxy for current market price.
    /// </summary>
    private void MarkToMarketFromFill(FillEvent fill)
    {
        if (_positions.TryGetValue(fill.Symbol, out var state))
            state.UpdateUnrealisedPnl(fill.FillPrice);
    }

    // Internal mutable position state
    private sealed class PositionState
    {
        public string Symbol { get; }
        public decimal Quantity { get; private set; }
        public decimal AverageEntryPrice { get; private set; }
        public decimal UnrealisedPnl { get; private set; }
        public decimal RealisedPnl { get; private set; }
        public DateTimeOffset EntryTime { get; private set; }

        public PositionState(string symbol) => Symbol = symbol;

        public void AddBuy(decimal qty, decimal price, DateTimeOffset timestamp)
        {
            decimal totalCost = AverageEntryPrice * Quantity + price * qty;
            Quantity += qty;
            AverageEntryPrice = Quantity > 0 ? totalCost / Quantity : 0m;
            if (EntryTime == default) EntryTime = timestamp;
        }

        public void ReducePosition(decimal qty, decimal netPnl)
        {
            Quantity -= qty;
            RealisedPnl += netPnl;
            if (Quantity == 0m)
            {
                AverageEntryPrice = 0m;
                UnrealisedPnl = 0m;
            }
        }

        public void UpdateUnrealisedPnl(decimal currentPrice)
        {
            UnrealisedPnl = (currentPrice - AverageEntryPrice) * Quantity;
        }

        public Position ToPosition() =>
            new(Symbol, Quantity, AverageEntryPrice, UnrealisedPnl, RealisedPnl);
    }
}
