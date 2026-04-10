namespace TradingResearchEngine.Application.Strategy;

/// <summary>String constants for strategy family classification.</summary>
public static class StrategyFamily
{
    /// <summary>Trend-following strategies.</summary>
    public const string Trend = "Trend";

    /// <summary>Mean-reversion strategies.</summary>
    public const string MeanReversion = "MeanReversion";

    /// <summary>Breakout / channel strategies.</summary>
    public const string Breakout = "Breakout";

    /// <summary>Regime-aware / hybrid strategies.</summary>
    public const string RegimeAware = "RegimeAware";

    /// <summary>Benchmark / passive strategies.</summary>
    public const string Benchmark = "Benchmark";
}
