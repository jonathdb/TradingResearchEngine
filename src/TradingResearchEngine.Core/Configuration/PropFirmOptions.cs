namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Optional prop-firm evaluation options embedded in a <see cref="ScenarioConfig"/>.
/// Either or both challenge and instant-funding configs may be supplied.
/// </summary>
public sealed record PropFirmOptions(
    ChallengeConfigOptions? Challenge,
    InstantFundingConfigOptions? InstantFunding);

/// <summary>Challenge account parameters for prop-firm evaluation.</summary>
public sealed record ChallengeConfigOptions(
    decimal PassRatePercent,
    decimal PassToFundedConversionPercent,
    decimal AccountFeeUsd,
    decimal NotionalSizeUsd,
    decimal MaxDailyDrawdownPercent,
    decimal MaxTotalDrawdownPercent);

/// <summary>Instant-funding account parameters for prop-firm evaluation.</summary>
public sealed record InstantFundingConfigOptions(
    decimal DirectFundedProbabilityPercent,
    decimal AccountFeeUsd,
    decimal NotionalSizeUsd,
    decimal GrossMonthlyReturnPercent,
    decimal PayoutSplitPercent,
    decimal PayoutFrictionFactor,
    int ExpectedPayoutMonths);
