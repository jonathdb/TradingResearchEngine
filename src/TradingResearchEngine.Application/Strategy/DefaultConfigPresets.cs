using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>Built-in configuration presets shipped with V5.</summary>
public static class DefaultConfigPresets
{
    /// <summary>Zero-cost, relaxed risk for quick hypothesis validation.</summary>
    public static readonly ConfigPreset FastIdeaCheck = new(
        "preset-fast-idea",
        "Fast Idea Check",
        "Zero-cost, relaxed risk for quick hypothesis validation.",
        PresetCategory.QuickCheck,
        new ExecutionConfig(
            "ZeroSlippageModel",
            "ZeroCommissionModel",
            FillMode.NextBarOpen,
            ExecutionRealismProfile.FastResearch),
        RiskConfig: null,
        IsBuiltIn: true);

    /// <summary>Moderate costs and standard risk for baseline evaluation.</summary>
    public static readonly ConfigPreset StandardBacktest = new(
        "preset-standard",
        "Standard Backtest",
        "Moderate costs and standard risk for baseline evaluation.",
        PresetCategory.Standard,
        new ExecutionConfig(
            "FixedSpreadSlippageModel",
            "PerTradeCommissionModel",
            FillMode.NextBarOpen,
            ExecutionRealismProfile.StandardBacktest),
        RiskConfig: null,
        IsBuiltIn: true);

    /// <summary>ATR-scaled slippage, per-share commission, session rules.</summary>
    public static readonly ConfigPreset ConservativeRealistic = new(
        "preset-conservative",
        "Conservative Realistic",
        "ATR-scaled slippage, per-share commission, session rules.",
        PresetCategory.Realistic,
        new ExecutionConfig(
            "AtrScaledSlippageModel",
            "PerShareCommissionModel",
            FillMode.NextBarOpen,
            ExecutionRealismProfile.BrokerConservative),
        RiskConfig: null,
        IsBuiltIn: true);

    /// <summary>BrokerConservative profile with recommendation to run sensitivity and walk-forward studies.</summary>
    public static readonly ConfigPreset ResearchGrade = new(
        "preset-research-grade",
        "Research-Grade Validation",
        "BrokerConservative profile with recommendation to run sensitivity and walk-forward studies.",
        PresetCategory.ResearchGrade,
        new ExecutionConfig(
            "AtrScaledSlippageModel",
            "PerShareCommissionModel",
            FillMode.NextBarOpen,
            ExecutionRealismProfile.BrokerConservative),
        RiskConfig: null,
        IsBuiltIn: true);

    /// <summary>All built-in presets.</summary>
    public static IReadOnlyList<ConfigPreset> All { get; } = new[]
    {
        FastIdeaCheck,
        StandardBacktest,
        ConservativeRealistic,
        ResearchGrade
    };
}
