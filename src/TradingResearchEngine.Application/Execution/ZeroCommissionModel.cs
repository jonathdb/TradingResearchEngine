using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;

namespace TradingResearchEngine.Application.Execution;

/// <summary>Commission model that charges zero commission. Used as the default fallback.</summary>
public sealed class ZeroCommissionModel : ICommissionModel
{
    /// <inheritdoc/>
    public decimal ComputeCommission(OrderEvent order, decimal fillPrice, decimal quantity) => 0m;
}
