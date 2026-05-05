namespace TradingResearchEngine.Core.Indicators;

/// <summary>
/// Static registry exposing metadata for all built-in technical indicators.
/// Used by the visual composer and discovery endpoints to enumerate available
/// indicators and their parameter constraints. Parallels the
/// <c>DefaultStrategyTemplates.All</c> pattern for strategy template discovery.
/// </summary>
public static class IndicatorRegistry
{
    /// <summary>
    /// All built-in indicator descriptors with full parameter metadata.
    /// </summary>
    public static IReadOnlyList<IndicatorDescriptor> All { get; } = new[]
    {
        new IndicatorDescriptor(
            "SMA",
            "Simple Moving Average — arithmetic mean of closing prices over a rolling window.",
            new[] { new IndicatorParameterDescriptor("Period", "int", 2, 500, 20) },
            "decimal"),

        new IndicatorDescriptor(
            "EMA",
            "Exponential Moving Average — weighted average giving more weight to recent prices.",
            new[] { new IndicatorParameterDescriptor("Period", "int", 2, 500, 20) },
            "decimal"),

        new IndicatorDescriptor(
            "ATR",
            "Average True Range — measures market volatility using the true range of each bar.",
            new[] { new IndicatorParameterDescriptor("Period", "int", 2, 500, 14) },
            "decimal"),

        new IndicatorDescriptor(
            "RSI",
            "Relative Strength Index — momentum oscillator measuring speed and magnitude of price changes.",
            new[] { new IndicatorParameterDescriptor("Period", "int", 2, 500, 14) },
            "decimal"),

        new IndicatorDescriptor(
            "BollingerBands",
            "Bollinger Bands — upper and lower bands at configurable standard deviations from a simple moving average.",
            new IndicatorParameterDescriptor[]
            {
                new("Period", "int", 2, 500, 20),
                new("StdDevMultiplier", "decimal", 0.5m, 5.0m, 2.0m)
            },
            "BollingerBandsOutput"),

        new IndicatorDescriptor(
            "ZScore",
            "Rolling Z-Score — measures how many standard deviations the current price is from the rolling mean.",
            new[] { new IndicatorParameterDescriptor("Period", "int", 2, 500, 20) },
            "decimal"),

        new IndicatorDescriptor(
            "DonchianChannel",
            "Donchian Channel — highest high and lowest low over a lookback period for breakout detection.",
            new[] { new IndicatorParameterDescriptor("Period", "int", 2, 500, 20) },
            "DonchianChannelOutput"),
    };
}
