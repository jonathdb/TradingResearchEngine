using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.Core.Portfolio;

/// <summary>
/// Tracks open positions, cash balance, and equity curve by consuming <see cref="FillEvent"/> instances.
/// V6: Adds ShortPositions dictionary for short position tracking with correct mark-to-market.
/// </summary>
public sealed class Portfolio
{
    private readonly ILogger<Portfolio> _logger;
    private readonly Dictionary<string, PositionState> _positions = new();
    private readonly Dictionary<string, ShortPositionState> _shortPositions = new();
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

    /// <summary>Open long positions keyed by symbol.</summary>
    public IReadOnlyDictionary<string, Position> Positions =>
        _positions.ToDictionary(kv => kv.Key, kv => kv.Value.ToPosition());

    /// <summary>V6: Open short positions keyed by symbol.</summary>
    public IReadOnlyDictionary<string, Position> ShortPositions =>
        _shortPositions.ToDictionary(kv => kv.Key, kv => kv.Value.ToPosition());

    /// <summary>Time-ordered equity snapshots, one per fill.</summary>
    public IReadOnlyList<EquityCurvePoint> EquityCurve => _equityCurve;

    /// <summary>All completed round-trip trades.</summary>
    public IReadOnlyList<ClosedTrade> ClosedTrades => _closedTrades;

    /// <summary>Count of all open positions (long + short).</summary>
    public int OpenPositionCount =>
        _positions.Count(p => p.Value.Quantity > 0m) +
        _shortPositions.Count(p => p.Value.Quantity > 0m);

    /// <summary>Returns an immutable snapshot of current portfolio state for the RiskLayer.</summary>
    public PortfolioSnapshot TakeSnapshot() =>
        new(Positions, CashBalance, TotalEquity, ShortPositions);

    /// <summary>
    /// Returns exposure by symbol as a dictionary of symbol → exposure percentage.
    /// Exposure is computed as absolute (position market value / total equity) for each symbol,
    /// summing both long and short exposure.
    /// </summary>
    public Dictionary<string, decimal> GetExposureBySymbol()
    {
        if (TotalEquity <= 0m)
            return new Dictionary<string, decimal>();

        var result = new Dictionary<string, decimal>();

        foreach (var kv in _positions.Where(kv => kv.Value.Quantity > 0m))
        {
            decimal exposure = Math.Abs(kv.Value.AverageEntryPrice * kv.Value.Quantity) / TotalEquity * 100m;
            result[kv.Key] = result.TryGetValue(kv.Key, out var existing) ? existing + exposure : exposure;
        }

        foreach (var kv in _shortPositions.Where(kv => kv.Value.Quantity > 0m))
        {
            decimal exposure = Math.Abs(kv.Value.AverageEntryPrice * kv.Value.Quantity) / TotalEquity * 100m;
            result[kv.Key] = result.TryGetValue(kv.Key, out var existing) ? existing + exposure : exposure;
        }

        return result;
    }

    /// <summary>
    /// Updates portfolio state from a fill. Handles Long, Short, and Flat directions.
    /// Logs a <c>MarginBreachWarning</c> if cash would go negative on a long buy.
    /// </summary>
    public void Update(FillEvent fill)
    {
        switch (fill.Direction)
        {
            case Direction.Long:
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
                break;
            }
            case Direction.Short:
            {
                // Short sale proceeds: receive cash for selling borrowed shares
                decimal proceeds = fill.FillPrice * fill.Quantity - fill.Commission;
                CashBalance += proceeds;
                OpenShortPosition(fill);
                break;
            }
            case Direction.Flat:
            {
                if (_positions.TryGetValue(fill.Symbol, out var longState) && longState.Quantity > 0m)
                {
                    decimal proceeds = fill.FillPrice * fill.Quantity - fill.Commission;
                    CashBalance += proceeds;
                    ApplySellFill(fill);
                }
                else if (_shortPositions.TryGetValue(fill.Symbol, out var shortState) && shortState.Quantity > 0m)
                {
                    // Cover cost: pay to buy back borrowed shares
                    decimal cost = fill.FillPrice * fill.Quantity + fill.Commission;
                    CashBalance -= cost;
                    CloseShortPosition(fill);
                }
                else
                {
                    _logger.LogWarning("OrphanFlatFill: {Symbol} flat fill received with no open position; ignoring.",
                        fill.Symbol);
                }
                break;
            }
        }

        MarkToMarketFromFill(fill);
        RecalculateTotalEquity();
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

    private void OpenShortPosition(FillEvent fill)
    {
        if (!_shortPositions.TryGetValue(fill.Symbol, out var state))
        {
            state = new ShortPositionState(fill.Symbol);
            _shortPositions[fill.Symbol] = state;
        }
        state.AddShort(fill.Quantity, fill.FillPrice, fill.Timestamp);
    }

    private void CloseShortPosition(FillEvent fill)
    {
        if (!_shortPositions.TryGetValue(fill.Symbol, out var state) || state.Quantity == 0m)
            return;

        decimal closedQty = Math.Min(fill.Quantity, state.Quantity);
        // Short PnL = (entryPrice - exitPrice) × quantity
        decimal grossPnl = (state.AverageEntryPrice - fill.FillPrice) * closedQty;
        decimal netPnl = grossPnl - fill.Commission;

        _closedTrades.Add(new ClosedTrade(
            fill.Symbol,
            state.EntryTime,
            fill.Timestamp,
            state.AverageEntryPrice,
            fill.FillPrice,
            closedQty,
            Direction.Short,
            grossPnl,
            fill.Commission,
            netPnl));

        state.ReducePosition(closedQty, netPnl);
        if (state.Quantity == 0m) _shortPositions.Remove(fill.Symbol);
    }

    private void RecalculateTotalEquity()
    {
        decimal longUnrealised = _positions.Values.Sum(p => p.UnrealisedPnl);
        decimal shortUnrealised = _shortPositions.Values.Sum(p => p.UnrealisedPnl);
        TotalEquity = CashBalance + longUnrealised + shortUnrealised;
    }

    /// <summary>Appends an enriched equity curve point with full portfolio state.</summary>
    private void AppendEquityCurvePoint(DateTimeOffset timestamp)
    {
        decimal longUnrealised = _positions.Values.Sum(p => p.UnrealisedPnl);
        decimal shortUnrealised = _shortPositions.Values.Sum(p => p.UnrealisedPnl);
        decimal unrealisedPnl = longUnrealised + shortUnrealised;
        decimal realisedPnl = _closedTrades.Sum(t => t.NetPnl);

        _equityCurve.Add(new EquityCurvePoint(
            timestamp, TotalEquity, CashBalance,
            unrealisedPnl, realisedPnl, OpenPositionCount));
    }

    /// <summary>
    /// Updates unrealised P&amp;L for open positions and appends an <see cref="EquityCurvePoint"/>.
    /// Called by the engine on every bar, after pending fills are processed and before the strategy is invoked.
    /// V6: Updates both long and short unrealised PnL.
    /// </summary>
    public void MarkToMarket(string symbol, decimal price, DateTimeOffset timestamp)
    {
        if (_positions.TryGetValue(symbol, out var longState))
            longState.UpdateUnrealisedPnl(price);

        if (_shortPositions.TryGetValue(symbol, out var shortState))
            shortState.UpdateUnrealisedPnl(price);

        RecalculateTotalEquity();
        AppendEquityCurvePoint(timestamp);
    }

    /// <summary>
    /// Updates unrealised P&amp;L for a position using the fill price as a proxy for current market price.
    /// </summary>
    private void MarkToMarketFromFill(FillEvent fill)
    {
        if (_positions.TryGetValue(fill.Symbol, out var longState))
            longState.UpdateUnrealisedPnl(fill.FillPrice);

        if (_shortPositions.TryGetValue(fill.Symbol, out var shortState))
            shortState.UpdateUnrealisedPnl(fill.FillPrice);
    }

    // Internal mutable long position state
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

    // Internal mutable short position state
    private sealed class ShortPositionState
    {
        public string Symbol { get; }
        public decimal Quantity { get; private set; }
        public decimal AverageEntryPrice { get; private set; }
        public decimal UnrealisedPnl { get; private set; }
        public decimal RealisedPnl { get; private set; }
        public DateTimeOffset EntryTime { get; private set; }

        public ShortPositionState(string symbol) => Symbol = symbol;

        public void AddShort(decimal qty, decimal price, DateTimeOffset timestamp)
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

        /// <summary>Short unrealised PnL: (entryPrice - currentPrice) × |quantity|</summary>
        public void UpdateUnrealisedPnl(decimal currentPrice)
        {
            UnrealisedPnl = (AverageEntryPrice - currentPrice) * Quantity;
        }

        public Position ToPosition() =>
            new(Symbol, Quantity, AverageEntryPrice, UnrealisedPnl, RealisedPnl);
    }
}
