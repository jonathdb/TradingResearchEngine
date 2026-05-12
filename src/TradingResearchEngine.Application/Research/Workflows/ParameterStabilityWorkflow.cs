using TradingResearchEngine.Application.Research.Results;

namespace TradingResearchEngine.Application.Research.Workflows;

/// <summary>
/// Computes stability scores for parameter grid cells based on the variance
/// of the target metric across neighbouring cells (±1 step in each dimension).
/// Identifies stability zones where the score is below a configurable threshold.
/// </summary>
public sealed class ParameterGridStabilityWorkflow
{
    /// <summary>Default stability threshold. Cells with score below this are considered stable.</summary>
    public const double DefaultStabilityThreshold = 0.15;

    /// <summary>
    /// Computes stability scores for a parameter grid.
    /// </summary>
    /// <param name="grid">Grid of parameter combinations with their metric values.</param>
    /// <param name="stabilityThreshold">Threshold below which a cell is considered stable.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Stability analysis result with scores and identified zones.</returns>
    public Task<ParameterGridStabilityResult> RunAsync(
        IReadOnlyList<ParameterStabilityCell> grid,
        double stabilityThreshold = DefaultStabilityThreshold,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Compute stability scores based on variance of neighbours
        var scoredCells = new List<ParameterStabilityCell>(grid.Count);

        for (int i = 0; i < grid.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var cell = grid[i];

            // Find neighbours (adjacent cells in the grid)
            var neighbourMetrics = FindNeighbourMetrics(grid, i);

            double stabilityScore = 0.0;
            if (neighbourMetrics.Count > 0)
            {
                var allValues = neighbourMetrics.Append((double)cell.MetricValue).ToList();
                var mean = allValues.Average();
                var variance = allValues.Sum(v => (v - mean) * (v - mean)) / allValues.Count;
                stabilityScore = Math.Sqrt(variance) / (Math.Abs(mean) + 1e-10);
            }

            scoredCells.Add(cell with { StabilityScore = stabilityScore });
        }

        // Identify stability zones (contiguous regions below threshold)
        var stableCells = scoredCells.Where(c => c.StabilityScore < stabilityThreshold).ToList();
        var zones = new List<StabilityZone>();

        if (stableCells.Count > 0)
        {
            // Simple zone: all stable cells form one zone with center at median metric
            var sorted = stableCells.OrderBy(c => c.MetricValue).ToList();
            var center = sorted[sorted.Count / 2];
            zones.Add(new StabilityZone(stableCells, center.ParameterValues, center.MetricValue));
        }

        return Task.FromResult(new ParameterGridStabilityResult(scoredCells, zones, stabilityThreshold));
    }

    private static List<double> FindNeighbourMetrics(IReadOnlyList<ParameterStabilityCell> grid, int index)
    {
        // For a 1D or flattened grid, neighbours are adjacent indices
        var neighbours = new List<double>();
        if (index > 0) neighbours.Add((double)grid[index - 1].MetricValue);
        if (index < grid.Count - 1) neighbours.Add((double)grid[index + 1].MetricValue);
        return neighbours;
    }
}
