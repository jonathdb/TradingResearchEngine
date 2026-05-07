using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Web.Features.Research.Sweep;

namespace TradingResearchEngine.UnitTests;

/// <summary>
/// Example-based unit tests for <see cref="SweepParameterSelector"/>.
/// Validates Requirements 8.1, 8.2.
/// </summary>
public class SweepParameterSelectorTests
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
    /// When all schema parameter names are already used, SelectNext returns null.
    /// **Validates: Requirement 8.2**
    /// </summary>
    [Fact]
    public void SweepParameterSelector_AllUsed_ReturnsNull()
    {
        var schema = new List<StrategyParameterSchema>
        {
            MakeSchema("fastPeriod"),
            MakeSchema("slowPeriod"),
            MakeSchema("threshold")
        };

        var usedNames = new HashSet<string> { "fastPeriod", "slowPeriod", "threshold" };

        var result = SweepParameterSelector.SelectNext(schema, usedNames);

        Assert.Null(result);
    }

    /// <summary>
    /// When the schema list is empty, SelectNext returns null.
    /// **Validates: Requirement 8.2 (edge case)**
    /// </summary>
    [Fact]
    public void SweepParameterSelector_EmptySchema_ReturnsNull()
    {
        var schema = new List<StrategyParameterSchema>();
        var usedNames = new HashSet<string>();

        var result = SweepParameterSelector.SelectNext(schema, usedNames);

        Assert.Null(result);
    }
}
