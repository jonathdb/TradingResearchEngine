namespace TradingResearchEngine.Web.Features.Research.Sweep;

/// <summary>
/// Mutable UI state for a single parameter sweep row.
/// </summary>
public sealed class SweepRowModel
{
    public string ParameterName { get; set; } = "";
    public decimal Low { get; set; }
    public decimal High { get; set; }
    public decimal Increment { get; set; } = 1m;
}
