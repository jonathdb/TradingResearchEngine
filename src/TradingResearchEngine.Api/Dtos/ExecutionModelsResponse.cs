namespace TradingResearchEngine.Api.Dtos;

/// <summary>Response for <c>GET /execution-models</c> listing all supported execution components.</summary>
public sealed record ExecutionModelsResponse(
    IReadOnlyList<NamedItem> SlippageModels,
    IReadOnlyList<NamedItem> CommissionModels,
    IReadOnlyList<NamedItem> FillModes,
    IReadOnlyList<NamedItem> RealismProfiles,
    IReadOnlyList<NamedItem> SessionCalendars,
    IReadOnlyList<NamedItem> PositionSizingPolicies);
