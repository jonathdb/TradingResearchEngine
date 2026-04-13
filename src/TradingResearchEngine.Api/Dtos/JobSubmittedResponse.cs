using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Api.Dtos;

/// <summary>Response returned when a job is successfully submitted.</summary>
public sealed record JobSubmittedResponse(
    string JobId,
    JobStatus Status,
    DateTimeOffset SubmittedAt);
