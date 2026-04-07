using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>Charges a fixed fee per share/unit filled.</summary>
public sealed class PerShareCommissionModel : ICommissionModel
{
    private readonly decimal _feePerShare;

    /// <param name="feePerShare">USD commission charged per share or unit.</param>
    public PerShareCommissionModel(decimal feePerShare) => _feePerShare = feePerShare;

    /// <inheritdoc/>
    public decimal ComputeCommission(OrderEvent order, decimal fillPrice, decimal quantity)
        => _feePerShare * quantity;
}
