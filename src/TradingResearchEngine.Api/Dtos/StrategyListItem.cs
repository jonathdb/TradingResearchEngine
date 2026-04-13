using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Api.Dtos;

/// <summary>Summary item returned by <c>GET /strategies</c>.</summary>
public sealed record StrategyListItem(
    string Name,
    string DisplayName,
    string Family,
    string Description,
    string Hypothesis,
    string? BestFor,
    string[]? SuggestedStudies,
    DifficultyLevel DifficultyLevel);
