using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Web.Features.Research.Sweep;

namespace TradingResearchEngine.UnitTests;

// Feature: web-only-ux-overhaul, Property 3: Auto-selection picks first unused parameter

/// <summary>
/// Property 3: Auto-selection picks first unused parameter.
/// For any non-empty list of StrategyParameterSchema items and any subset of used
/// parameter names, SweepParameterSelector.SelectNext returns the Name of the first
/// schema entry whose Name is not in usedNames, or null if all names are in usedNames.
/// **Validates: Requirements 8.1, 8.2**
/// </summary>
public class SweepParameterSelectorProperties
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

    [Property(MaxTest = 100)]
    public bool SelectNext_ReturnsFirstUnusedOrNull(PositiveInt countWrap, int subsetSeed)
    {
        // Generate a non-empty list of schema items with unique names
        var count = (countWrap.Get % 20) + 1; // 1 to 20 items
        var schema = Enumerable.Range(0, count)
            .Select(i => MakeSchema($"param_{i}"))
            .ToList();

        // Generate a subset of names as "used" — use subsetSeed bits to decide inclusion
        var rng = new Random(subsetSeed);
        var usedNames = new HashSet<string>(
            schema
                .Where(_ => rng.Next(2) == 1)
                .Select(s => s.Name));

        var result = SweepParameterSelector.SelectNext(schema, usedNames);

        // Find the expected first unused parameter
        var expectedFirst = schema.FirstOrDefault(s => !usedNames.Contains(s.Name));

        if (expectedFirst is null)
        {
            // All names are used — should return null
            return result is null;
        }
        else
        {
            // Should return the name of the first unused schema entry
            return result == expectedFirst.Name;
        }
    }
}
