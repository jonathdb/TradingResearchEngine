using TradingResearchEngine.Core.Persistence;

namespace TradingResearchEngine.Application.PropFirm;

/// <summary>JSON-configurable set of prop-firm-specific rules.</summary>
public sealed record FirmRuleSet(
    string FirmName,
    decimal MaxDailyDrawdownPercent,
    decimal MaxTotalDrawdownPercent,
    int MinTradingDays,
    decimal? PayoutCapUsd,
    decimal? ConsistencyRulePercent,
    IReadOnlyList<string> CustomRules) : IHasId
{
    /// <inheritdoc/>
    public string Id => FirmName;
}
