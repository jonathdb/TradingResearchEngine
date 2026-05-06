using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Core.PaperTrading;

/// <summary>
/// Abstraction for a simulated live trading session that streams bars
/// and trades in real time. Implementations reuse the same execution
/// pipeline as backtesting for metric equivalence.
/// </summary>
public interface IPaperTradingSession
{
    /// <summary>Current session lifecycle status.</summary>
    PaperTradingStatus Status { get; }

    /// <summary>Live portfolio state, updated on every bar.</summary>
    Portfolio.Portfolio Portfolio { get; }

    /// <summary>Observable stream of bar events with portfolio snapshots.</summary>
    IObservable<PaperBarEvent> BarStream { get; }

    /// <summary>Observable stream of trade events with portfolio snapshots.</summary>
    IObservable<PaperTradeEvent> TradeStream { get; }

    /// <summary>Starts the paper trading session.</summary>
    /// <param name="config">The scenario configuration for this session.</param>
    /// <param name="ct">Cancellation token propagated throughout the session lifecycle.</param>
    Task StartAsync(ScenarioConfig config, CancellationToken ct);

    /// <summary>Stops the session and produces final results.</summary>
    /// <returns>The final paper trading result with metrics and portfolio state.</returns>
    Task<PaperTradingResult> StopAsync();

    /// <summary>Pauses bar consumption, preserving portfolio state.</summary>
    Task PauseAsync();

    /// <summary>Resumes bar consumption after a pause.</summary>
    /// <param name="ct">Cancellation token for the resumed session.</param>
    Task ResumeAsync(CancellationToken ct);
}
