using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// A pre-built starting point for strategy creation. Templates provide
/// sensible defaults and a description of the typical use case.
/// </summary>
public sealed record StrategyTemplate(
    string TemplateId,
    string Name,
    string Description,
    string StrategyType,
    string TypicalUseCase,
    Dictionary<string, object> DefaultParameters,
    string RecommendedTimeframe,
    ExecutionRealismProfile RecommendedProfile = ExecutionRealismProfile.StandardBacktest,
    StrategyDescriptor? Descriptor = null,
    /// <summary>V5: Named preset overrides keyed by preset name (e.g. "Conservative" → param dict).</summary>
    Dictionary<string, Dictionary<string, object>>? FamilyPresets = null,
    /// <summary>V5: Difficulty classification for builder UX.</summary>
    DifficultyLevel DifficultyLevel = DifficultyLevel.Beginner) : IHasId
{
    /// <inheritdoc/>
    public string Id => TemplateId;
}

/// <summary>Provides the default set of strategy templates.</summary>
public static class DefaultStrategyTemplates
{
    /// <summary>All built-in strategy templates.</summary>
    public static IReadOnlyList<StrategyTemplate> All { get; } = new[]
    {
        new StrategyTemplate(
            "tpl-vol-trend", "Volatility-Scaled Trend",
            "Trend following with ATR-normalized signal strength for volatility-aware sizing.",
            "volatility-scaled-trend", "Trend following on any instrument",
            new Dictionary<string, object> { ["fastPeriod"] = 10, ["slowPeriod"] = 50, ["atrPeriod"] = 14 },
            "Daily",
            Descriptor: new StrategyDescriptor(
                "volatility-scaled-trend", "Volatility-Scaled Trend", StrategyFamily.Trend,
                "Trend following with ATR-normalized signal strength for volatility-aware sizing.",
                "Persistent directional moves continue long enough for trend-following entries to overcome transaction costs when sized by volatility.",
                "Trending markets with sustained directional moves",
                new[] { "WalkForward", "AnchoredWalkForward", "MonteCarlo", "RegimeSegmentation" }),
            FamilyPresets: new Dictionary<string, Dictionary<string, object>>
            {
                ["Conservative"] = new() { ["fastPeriod"] = 20, ["slowPeriod"] = 100, ["atrPeriod"] = 20 },
                ["Aggressive"] = new() { ["fastPeriod"] = 5, ["slowPeriod"] = 20, ["atrPeriod"] = 10 }
            }),

        new StrategyTemplate(
            "tpl-zscore-mr", "Z-Score Mean Reversion",
            "Buys when z-score drops below entry threshold, exits on reversion to mean.",
            "zscore-mean-reversion", "Mean reversion on range-bound instruments",
            new Dictionary<string, object> { ["lookback"] = 30, ["entryThreshold"] = 2.0m, ["exitThreshold"] = 0.0m },
            "Daily",
            Descriptor: new StrategyDescriptor(
                "zscore-mean-reversion", "Z-Score Mean Reversion", StrategyFamily.MeanReversion,
                "Buys when z-score drops below entry threshold, exits on reversion to mean.",
                "Short-term price dislocations around a rolling equilibrium tend to mean-revert in non-trending regimes.",
                "Range-bound or mean-reverting instruments",
                new[] { "Sensitivity", "RegimeSegmentation", "MonteCarlo", "ParameterStability" }),
            FamilyPresets: new Dictionary<string, Dictionary<string, object>>
            {
                ["Conservative"] = new() { ["lookback"] = 50, ["entryThreshold"] = 2.5m, ["exitThreshold"] = 0.5m },
                ["Aggressive"] = new() { ["lookback"] = 15, ["entryThreshold"] = 1.5m, ["exitThreshold"] = 0.0m }
            }),

        new StrategyTemplate(
            "tpl-donchian-breakout", "Donchian Breakout",
            "Channel breakout trend follower using lagged Donchian bands.",
            "donchian-breakout", "Channel breakout trend following",
            new Dictionary<string, object> { ["period"] = 20 },
            "Any",
            Descriptor: new StrategyDescriptor(
                "donchian-breakout", "Donchian Breakout", StrategyFamily.Breakout,
                "Channel breakout trend follower using lagged Donchian bands.",
                "Range expansion after compression signals the start of a sustained move.",
                "Markets with periodic range expansion",
                new[] { "WalkForward", "MonteCarlo", "Sensitivity" }),
            FamilyPresets: new Dictionary<string, Dictionary<string, object>>
            {
                ["Conservative"] = new() { ["period"] = 40 },
                ["Aggressive"] = new() { ["period"] = 10 }
            }),

        new StrategyTemplate(
            "tpl-stationary-mr", "Stationary Mean Reversion",
            "ADF stationarity filter + z-score mean reversion on returns.",
            "stationary-mean-reversion", "ADF-filtered mean reversion on stationary instruments",
            new Dictionary<string, object> { ["lookback"] = 500, ["entryThreshold"] = 1.0m, ["exitThreshold"] = 1.0m },
            "Daily",
            Descriptor: new StrategyDescriptor(
                "stationary-mean-reversion", "Stationary Mean Reversion", StrategyFamily.MeanReversion,
                "ADF stationarity filter + z-score mean reversion on returns.",
                "Mean reversion signals are only reliable when the return series is statistically stationary.",
                "Instruments with statistically stationary return series",
                new[] { "RegimeSegmentation", "Sensitivity", "MonteCarlo", "ParameterStability" }),
            FamilyPresets: new Dictionary<string, Dictionary<string, object>>
            {
                ["Conservative"] = new() { ["lookback"] = 750, ["entryThreshold"] = 1.5m, ["exitThreshold"] = 1.5m },
                ["Aggressive"] = new() { ["lookback"] = 250, ["entryThreshold"] = 0.5m, ["exitThreshold"] = 0.5m }
            },
            DifficultyLevel: DifficultyLevel.Advanced),

        new StrategyTemplate(
            "tpl-regime-rotation", "Macro Regime Rotation",
            "Regime-aware allocation using vol/trend/momentum indicators with monthly rebalancing.",
            "macro-regime-rotation", "Regime-aware allocation on broad indices",
            new Dictionary<string, object> { ["volLookback"] = 21, ["trendLookback"] = 200, ["momentumLookback"] = 63, ["rebalanceDays"] = 21 },
            "Daily",
            Descriptor: new StrategyDescriptor(
                "macro-regime-rotation", "Macro Regime Rotation", StrategyFamily.RegimeAware,
                "Regime-aware allocation using vol/trend/momentum indicators with monthly rebalancing.",
                "No single signal family dominates across all regimes; switching behavior by regime improves robustness.",
                "Broad market indices with regime shifts",
                new[] { "RegimeSegmentation", "WalkForward", "AnchoredWalkForward", "Realism" }),
            FamilyPresets: new Dictionary<string, Dictionary<string, object>>
            {
                ["Conservative"] = new() { ["volLookback"] = 42, ["trendLookback"] = 252, ["rebalanceDays"] = 42 },
                ["Aggressive"] = new() { ["volLookback"] = 10, ["trendLookback"] = 100, ["rebalanceDays"] = 10 }
            },
            DifficultyLevel: DifficultyLevel.Intermediate),

        new StrategyTemplate(
            "tpl-buy-hold", "Buy & Hold Baseline",
            "Passive buy-and-hold benchmark for comparing active strategy performance.",
            "baseline-buy-and-hold", "Benchmark comparison for any active strategy",
            new Dictionary<string, object> { ["warmupBars"] = 1 },
            "Daily",
            Descriptor: new StrategyDescriptor(
                "baseline-buy-and-hold", "Buy & Hold Baseline", StrategyFamily.Benchmark,
                "Passive buy-and-hold benchmark for comparing active strategy performance.",
                "Markets have a positive long-term drift; any active strategy must outperform passive exposure to justify its complexity.",
                "Benchmark comparison for any active strategy",
                new[] { "MonteCarlo" })),
    };
}
