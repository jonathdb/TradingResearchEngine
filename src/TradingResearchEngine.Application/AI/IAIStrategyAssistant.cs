using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.AI;

/// <summary>
/// Generates and refines strategy drafts using a large language model.
/// Implementations use structured JSON output mode for reliable parsing.
/// </summary>
public interface IAIStrategyAssistant
{
    /// <summary>
    /// Generates a strategy draft from a natural-language description.
    /// The returned draft is tagged with <see cref="Strategy.SourceType.AIGenerated"/>.
    /// </summary>
    /// <param name="prompt">Natural-language description of the desired trading strategy.</param>
    /// <param name="ct">Cancellation token propagated to all async calls.</param>
    /// <returns>A fully populated <see cref="AIStrategyDraft"/>.</returns>
    Task<AIStrategyDraft> GenerateStrategyAsync(string prompt, CancellationToken ct);

    /// <summary>
    /// Streams the raw text response token by token for strategy generation.
    /// </summary>
    /// <param name="prompt">Natural-language description of the desired trading strategy.</param>
    /// <param name="ct">Cancellation token propagated to all async calls.</param>
    /// <returns>An async enumerable of text chunks.</returns>
    IAsyncEnumerable<string> StreamGenerateAsync(string prompt, CancellationToken ct);

    /// <summary>
    /// Refines an existing draft using backtest results and user feedback.
    /// Key metrics (Sharpe, MaxDrawdown, WinRate, TradeCount, DSR) from the
    /// backtest result are included in the refinement context.
    /// </summary>
    /// <param name="current">The current strategy draft to refine.</param>
    /// <param name="lastResult">Backtest result providing performance context.</param>
    /// <param name="refinementPrompt">User feedback describing desired changes.</param>
    /// <param name="ct">Cancellation token propagated to all async calls.</param>
    /// <returns>A revised <see cref="AIStrategyDraft"/> incorporating the feedback.</returns>
    Task<AIStrategyDraft> RefineStrategyAsync(
        AIStrategyDraft current,
        BacktestResult lastResult,
        string refinementPrompt,
        CancellationToken ct);

    /// <summary>
    /// Streams the raw text response token by token for strategy refinement.
    /// </summary>
    /// <param name="current">The current strategy draft to refine.</param>
    /// <param name="refinementPrompt">User feedback describing desired changes.</param>
    /// <param name="ct">Cancellation token propagated to all async calls.</param>
    /// <returns>An async enumerable of text chunks.</returns>
    IAsyncEnumerable<string> StreamRefineAsync(AIStrategyDraft current, string refinementPrompt, CancellationToken ct);
}
