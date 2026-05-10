using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.Web.Components.Studies;

/// <summary>
/// Maps each <see cref="StudyType"/> to a dedicated Blazor renderer component.
/// Adding a new study type requires only a new renderer component and a registry entry.
/// </summary>
public static class StudyRendererRegistry
{
    private static readonly Dictionary<StudyType, Type> _map = new()
    {
        [StudyType.MonteCarlo] = typeof(MonteCarloResultRenderer),
        [StudyType.WalkForward] = typeof(WalkForwardResultRenderer),
        [StudyType.AnchoredWalkForward] = typeof(WalkForwardResultRenderer),
        [StudyType.ParameterSweep] = typeof(SweepResultRenderer),
        [StudyType.Sensitivity] = typeof(SweepResultRenderer),
        [StudyType.Realism] = typeof(RealismResultRenderer),
        [StudyType.BenchmarkComparison] = typeof(BenchmarkResultRenderer),
        [StudyType.Cpcv] = typeof(CpcvResultRenderer),
        [StudyType.Variance] = typeof(VarianceResultRenderer),
    };

    /// <summary>
    /// Returns the renderer component type for the given study type, or null if none is registered.
    /// </summary>
    public static Type? GetRenderer(StudyType type)
        => _map.GetValueOrDefault(type);
}
