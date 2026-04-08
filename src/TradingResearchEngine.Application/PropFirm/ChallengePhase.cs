namespace TradingResearchEngine.Application.PropFirm;

/// <summary>
/// A single phase in a prop firm challenge (e.g. Phase 1, Phase 2, Funded).
/// Each phase has its own profit target, drawdown limits, and trading day requirements.
/// </summary>
public sealed record ChallengePhase(
    string PhaseName,
    decimal ProfitTargetPercent,
    decimal MaxDailyDrawdownPercent,
    decimal MaxTotalDrawdownPercent,
    int MinTradingDays,
    int? MaxTradingDays = null,
    decimal? ConsistencyRulePercent = null,
    bool TrailingDrawdown = false);
