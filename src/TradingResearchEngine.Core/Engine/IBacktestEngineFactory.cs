using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Sessions;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Core.Engine;

/// <summary>
/// Factory for creating <see cref="IBacktestEngine"/> instances.
/// Enables dependency injection and testability by removing direct <c>new BacktestEngine(...)</c>
/// calls from use cases and workflows.
/// </summary>
public interface IBacktestEngineFactory
{
    /// <summary>
    /// Creates a new <see cref="IBacktestEngine"/> configured with the specified pipeline components.
    /// Logger dependencies are resolved internally by the factory implementation.
    /// </summary>
    /// <param name="dataProvider">Market data source for the backtest.</param>
    /// <param name="strategy">Strategy instance to execute.</param>
    /// <param name="riskLayer">Risk management layer for signal/order evaluation.</param>
    /// <param name="executionHandler">Execution handler for order fills.</param>
    /// <param name="sessionCalendar">Optional session calendar for trading-hours filtering.</param>
    /// <param name="barDataPool">Optional object pool for bar data allocation reduction.</param>
    /// <returns>A fully configured <see cref="IBacktestEngine"/> ready to run.</returns>
    IBacktestEngine Create(
        IDataProvider dataProvider,
        IStrategy strategy,
        IRiskLayer riskLayer,
        IExecutionHandler executionHandler,
        ISessionCalendar? sessionCalendar = null,
        BarDataPool? barDataPool = null);
}
