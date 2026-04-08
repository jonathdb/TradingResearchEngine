using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.PropFirm;

/// <summary>
/// A specific firm's challenge rules with multi-phase support.
/// Richer than <see cref="FirmRuleSet"/> — supports challenge phases,
/// scaling, and unsupported rule documentation.
/// </summary>
public sealed record PropFirmRulePack(
    string RulePackId,
    string FirmName,
    string ChallengeName,
    decimal AccountSizeUsd,
    IReadOnlyList<ChallengePhase> Phases,
    decimal? PayoutSplitPercent = null,
    decimal? ScalingThresholdPercent = null,
    IReadOnlyList<string>? UnsupportedRules = null,
    bool IsBuiltIn = false,
    string? Notes = null) : IHasId
{
    /// <inheritdoc/>
    public string Id => RulePackId;
}
