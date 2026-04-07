namespace TradingResearchEngine.Application.PropFirm;

/// <summary>Configuration for an instant-funding prop-firm account.</summary>
public sealed record InstantFundingConfig(
    decimal DirectFundedProbabilityPercent,
    decimal AccountFeeUsd,
    decimal NotionalSizeUsd,
    decimal GrossMonthlyReturnPercent,
    decimal PayoutSplitPercent,
    decimal PayoutFrictionFactor,
    int ExpectedPayoutMonths,
    FirmRuleSet FirmRuleSet);
