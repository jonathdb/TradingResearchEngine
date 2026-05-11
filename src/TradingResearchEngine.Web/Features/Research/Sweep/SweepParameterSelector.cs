using TradingResearchEngine.Application.Strategies;

namespace TradingResearchEngine.Web.Features.Research.Sweep;

/// <summary>
/// Returns the first schema parameter name not already in usedNames, or null if all are used.
/// </summary>
public static class SweepParameterSelector
{
    /// <summary>
    /// Returns the first schema parameter name not already in <paramref name="usedNames"/>,
    /// or null if all are used.
    /// </summary>
    public static string? SelectNext(
        IReadOnlyList<StrategyParameterSchema> schema,
        IReadOnlySet<string> usedNames)
    {
        return schema.FirstOrDefault(s => !usedNames.Contains(s.Name))?.Name;
    }
}
