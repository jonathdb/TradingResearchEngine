using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Web.Features.Research.Sweep;

namespace TradingResearchEngine.UnitTests;

/// <summary>
/// Unit tests for the duplicate-parameter prevention logic in SweepParameterRow.
/// Tests the AvailableParameters filtering pattern: a row's own selected parameter
/// remains visible/selectable while parameters used by other rows are excluded.
/// **Validates: Requirement 8.1**
/// </summary>
public class SweepParameterRowTests
{
    private static StrategyParameterSchema MakeSchema(string name) =>
        new(
            Name: name,
            DisplayName: name,
            Type: "decimal",
            DefaultValue: 0m,
            IsRequired: false,
            Min: null,
            Max: null,
            EnumChoices: null,
            Description: "",
            SensitivityHint: SensitivityHint.Low,
            Group: "Signal",
            IsAdvanced: false,
            DisplayOrder: 0);

    /// <summary>
    /// Reproduces the AvailableParameters logic from SweepParameterRow.razor:
    /// includes the row's own selected parameter AND excludes parameters used by other rows.
    /// </summary>
    private static IEnumerable<StrategyParameterSchema> GetAvailableParameters(
        IReadOnlyList<StrategyParameterSchema> schema,
        SweepRowModel model,
        HashSet<string> usedParametersByOtherRows)
    {
        return schema.Where(p =>
            string.Equals(p.Name, model.ParameterName, StringComparison.Ordinal) ||
            !usedParametersByOtherRows.Contains(p.Name));
    }

    /// <summary>
    /// A row's own selected parameter remains in the available list, unused parameters
    /// are included, and parameters used by other rows are excluded.
    /// **Validates: Requirement 8.1**
    /// </summary>
    [Fact]
    public void SweepParameterRow_PreservesOwnSelection_AndPreventsDuplicateSelectionFromOtherRows()
    {
        // Arrange: 3 parameters in schema
        var schema = new List<StrategyParameterSchema>
        {
            MakeSchema("fastPeriod"),
            MakeSchema("slowPeriod"),
            MakeSchema("threshold")
        };

        // This row has "fastPeriod" selected
        var model = new SweepRowModel { ParameterName = "fastPeriod" };

        // Another row is using "slowPeriod"
        var usedByOtherRows = new HashSet<string> { "slowPeriod" };

        // Act: apply the filtering logic
        var available = GetAvailableParameters(schema, model, usedByOtherRows).ToList();
        var availableNames = available.Select(p => p.Name).ToList();

        // Assert: own selection ("fastPeriod") is preserved
        Assert.Contains("fastPeriod", availableNames);

        // Assert: unused parameter ("threshold") is available
        Assert.Contains("threshold", availableNames);

        // Assert: parameter used by another row ("slowPeriod") is excluded
        Assert.DoesNotContain("slowPeriod", availableNames);

        // Assert: exactly 2 parameters are available
        Assert.Equal(2, available.Count);
    }
}
