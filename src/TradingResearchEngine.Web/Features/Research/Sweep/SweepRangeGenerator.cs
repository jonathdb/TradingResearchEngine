namespace TradingResearchEngine.Web.Features.Research.Sweep;

/// <summary>
/// Generates a list of values from Low to High (inclusive) stepping by Increment.
/// Returns null if inputs are invalid (Increment &lt;= 0 or Low &gt; High).
/// This is a pure generation helper — user-facing validation messages are the
/// responsibility of the calling UI layer.
/// </summary>
public static class SweepRangeGenerator
{
    public static IReadOnlyList<decimal>? Generate(decimal low, decimal high, decimal increment)
    {
        if (increment <= 0m || low > high) return null;
        var values = new List<decimal>();
        for (var v = low; v <= high; v += increment)
            values.Add(v);
        return values;
    }
}
