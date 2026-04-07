using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>Charges a fixed flat fee per trade regardless of size.</summary>
public sealed class PerTradeCommissionModel : ICommissionModel
{
    private readonly decimal _feePerTrade;

    /// <param name="feePerTrade">Fixed USD commission charged per trade.</param>
    public PerTradeCommissionModel(decimal feePerTrade) => _feePerTrade = feePerTrade;

    /// <inheritdoc/>
    public decimal ComputeCommission(OrderEvent order, decimal fillPrice, decimal quantity) => _feePerTrade;
}
