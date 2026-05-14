using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Sessions;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Default implementation of <see cref="IBacktestEngineFactory"/>.
/// Resolves logger dependencies internally so callers do not need to manage them.
/// </summary>
public sealed class BacktestEngineFactory : IBacktestEngineFactory
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initialises the factory with a logger factory for engine and sub-component logging.</summary>
    /// <param name="loggerFactory">Logger factory used to create loggers for each engine instance.</param>
    public BacktestEngineFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc/>
    public IBacktestEngine Create(
        IDataProvider dataProvider,
        IStrategy strategy,
        IRiskLayer riskLayer,
        IExecutionHandler executionHandler,
        ISessionCalendar? sessionCalendar = null,
        BarDataPool? barDataPool = null)
    {
        var engineLogger = _loggerFactory.CreateLogger<BacktestEngine>();
        return new BacktestEngine(
            dataProvider,
            strategy,
            riskLayer,
            executionHandler,
            engineLogger,
            _loggerFactory,
            sessionCalendar,
            barDataPool);
    }
}
