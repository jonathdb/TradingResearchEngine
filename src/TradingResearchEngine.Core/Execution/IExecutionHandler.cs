using TradingResearchEngine.Core.Events;

namespace TradingResearchEngine.Core.Execution;

/// <summary>Converts a risk-approved <see cref="OrderEvent"/> into a <see cref="FillEvent"/>.</summary>
public interface IExecutionHandler
{
    /// <summary>Executes the order against the current market state and returns a fill.</summary>
    FillEvent Execute(OrderEvent order, MarketDataEvent currentBar);
}
