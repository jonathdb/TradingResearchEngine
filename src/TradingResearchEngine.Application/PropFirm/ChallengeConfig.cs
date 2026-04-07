namespace TradingResearchEngine.Application.PropFirm;

/// <summary>Configuration for a prop-firm challenge account.</summary>
public sealed record ChallengeConfig(
    decimal PassRatePercent,
    decimal PassToFundedConversionPercent,
    decimal AccountFeeUsd,
    decimal NotionalSizeUsd,
    decimal MaxDailyDrawdownPercent,
    decimal MaxTotalDrawdownPercent,
    FirmRuleSet FirmRuleSet);
