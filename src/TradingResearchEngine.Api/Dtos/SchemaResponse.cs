using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Api.Dtos;

/// <summary>Response for <c>GET /strategies/{name}/schema</c>.</summary>
public sealed record SchemaResponse(
    string StrategyName,
    string SchemaVersion,
    IReadOnlyList<StrategyParameterSchema> Parameters,
    IReadOnlyList<string>? DeprecatedFields = null,
    string? CompatibilityNotes = null);
