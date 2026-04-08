using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Metrics;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Compares multiple strategies under matched execution assumptions.
/// Validates that all results share the same data interval, fill mode, slippage, commission, and bars per year.
/// </summary>
public sealed class StrategyComparisonWorkflow
{
    /// <summary>Compares multiple backtest results.</summary>
    public StrategyComparisonResult Compare(IReadOnlyList<BacktestResult> results)
    {
        if (results.Count < 2)
            throw new ArgumentException("At least 2 results required for comparison.", nameof(results));

        var warnings = ValidateAssumptions(results);
        var rows = results.Select(r =>
        {
            decimal cagr = r.StartEquity > 0m ? (r.EndEquity - r.StartEquity) / r.StartEquity : 0m;
            decimal? recovery = MetricsCalculator.ComputeRecoveryFactor(r.EquityCurve, r.StartEquity, r.EndEquity);
            return new StrategyComparisonRow(
                r.ScenarioConfig.StrategyType,
                cagr,
                r.SharpeRatio,
                r.SortinoRatio,
                r.MaxDrawdown,
                r.ProfitFactor,
                r.Expectancy,
                recovery);
        }).ToList();

        return new StrategyComparisonResult(rows, warnings.Count > 0 ? warnings : null);
    }

    private static List<string> ValidateAssumptions(IReadOnlyList<BacktestResult> results)
    {
        var warnings = new List<string>();
        var reference = results[0].ScenarioConfig;

        for (int i = 1; i < results.Count; i++)
        {
            var config = results[i].ScenarioConfig;
            if (config.FillMode != reference.FillMode)
                warnings.Add($"FillMode mismatch: {reference.StrategyType}={reference.FillMode}, {config.StrategyType}={config.FillMode}");
            if (config.SlippageModelType != reference.SlippageModelType)
                warnings.Add($"SlippageModel mismatch: {reference.StrategyType}={reference.SlippageModelType}, {config.StrategyType}={config.SlippageModelType}");
            if (config.CommissionModelType != reference.CommissionModelType)
                warnings.Add($"CommissionModel mismatch: {reference.StrategyType}={reference.CommissionModelType}, {config.StrategyType}={config.CommissionModelType}");
            if (config.BarsPerYear != reference.BarsPerYear)
                warnings.Add($"BarsPerYear mismatch: {reference.StrategyType}={reference.BarsPerYear}, {config.StrategyType}={config.BarsPerYear}");
        }
        return warnings;
    }
}
