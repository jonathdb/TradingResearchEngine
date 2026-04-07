using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Execution;

/// <summary>Computes the commission cost for a fill.</summary>
public interface ICommissionModel
{
    /// <summary>Returns the commission amount in USD for the given order, fill price, and quantity.</summary>
    decimal ComputeCommission(OrderEvent order, decimal fillPrice, decimal quantity);
}
