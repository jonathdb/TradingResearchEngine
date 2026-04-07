using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Core.Reporting;

/// <summary>Renders backtest results to console or Markdown.</summary>
public interface IReporter
{
    /// <summary>Writes a formatted <see cref="BacktestResult"/> to standard output.</summary>
    void RenderToConsole(BacktestResult result);

    /// <summary>Returns a Markdown string representation of a <see cref="BacktestResult"/>.</summary>
    string RenderToMarkdown(BacktestResult result);

    /// <summary>Writes a formatted <see cref="ComparisonReport"/> to standard output.</summary>
    void RenderToConsole(ComparisonReport report);

    /// <summary>Returns a Markdown string representation of a <see cref="ComparisonReport"/>.</summary>
    string RenderToMarkdown(ComparisonReport report);
}
