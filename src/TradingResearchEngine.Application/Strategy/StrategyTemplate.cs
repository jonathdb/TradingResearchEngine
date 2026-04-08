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
    ExecutionRealismProfile RecommendedProfile = ExecutionRealismProfile.StandardBacktest) : IHasId
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
            "tpl-sma-crossover", "SMA Crossover",
            "Goes long when fast SMA crosses above slow SMA, flat when it crosses below.",
            "sma-crossover", "Trend following on any instrument",
            new Dictionary<string, object> { ["fastPeriod"] = 10, ["slowPeriod"] = 30 },
            "Daily"),

        new StrategyTemplate(
            "tpl-mean-reversion", "Mean Reversion",
            "Buys when price drops N standard deviations below the SMA, sells on reversion.",
            "mean-reversion", "Mean reversion on range-bound instruments",
            new Dictionary<string, object> { ["lookback"] = 20, ["entryStdDevs"] = 2m },
            "Daily"),

        new StrategyTemplate(
            "tpl-rsi", "RSI Momentum",
            "Buys when RSI drops below oversold, sells when RSI rises above overbought.",
            "rsi", "Momentum on oversold/overbought conditions",
            new Dictionary<string, object> { ["period"] = 14, ["oversold"] = 30m, ["overbought"] = 70m },
            "Daily"),

        new StrategyTemplate(
            "tpl-bollinger-bands", "Bollinger Bands",
            "Mean reversion at Bollinger Band extremes.",
            "bollinger-bands", "Mean reversion at band extremes",
            new Dictionary<string, object> { ["period"] = 30, ["stdDevMultiplier"] = 2m, ["exitAtMiddle"] = false },
            "Daily"),

        new StrategyTemplate(
            "tpl-donchian-breakout", "Donchian Breakout",
            "Channel breakout trend follower using lagged Donchian bands.",
            "donchian-breakout", "Channel breakout trend following",
            new Dictionary<string, object> { ["period"] = 20 },
            "Daily"),

        new StrategyTemplate(
            "tpl-stationary-mr", "Stationary Mean Reversion",
            "ADF stationarity filter + z-score mean reversion.",
            "stationary-mean-reversion", "ADF-filtered mean reversion on stationary instruments",
            new Dictionary<string, object> { ["lookback"] = 500, ["entryThreshold"] = 1.0m, ["exitThreshold"] = 1.0m },
            "Daily"),
    };
}
