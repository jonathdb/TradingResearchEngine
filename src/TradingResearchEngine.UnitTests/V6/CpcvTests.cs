using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.V6;

public class CpcvTests
{
    [Fact]
    public void GenerateCombinations_C6_2_Produces15()
    {
        var combos = CpcvStudyHandler.GenerateCombinations(6, 2);
        Assert.Equal(15, combos.Count);
    }

    [Fact]
    public void GenerateCombinations_C5_2_Produces10()
    {
        var combos = CpcvStudyHandler.GenerateCombinations(5, 2);
        Assert.Equal(10, combos.Count);
    }

    [Fact]
    public void GenerateCombinations_C4_1_Produces4()
    {
        var combos = CpcvStudyHandler.GenerateCombinations(4, 1);
        Assert.Equal(4, combos.Count);
    }

    [Fact]
    public void GenerateCombinations_C3_1_Produces3()
    {
        var combos = CpcvStudyHandler.GenerateCombinations(3, 1);
        Assert.Equal(3, combos.Count);
    }

    [Fact]
    public void GenerateCombinations_EachCombinationHasKElements()
    {
        var combos = CpcvStudyHandler.GenerateCombinations(6, 2);
        Assert.All(combos, c => Assert.Equal(2, c.Length));
    }

    [Fact]
    public void GenerateCombinations_AllIndicesInRange()
    {
        var combos = CpcvStudyHandler.GenerateCombinations(6, 2);
        Assert.All(combos, c => Assert.All(c, i => Assert.InRange(i, 0, 5)));
    }

    [Fact]
    public void GenerateCombinations_NoDuplicates()
    {
        var combos = CpcvStudyHandler.GenerateCombinations(6, 2);
        var asStrings = combos.Select(c => string.Join(",", c)).ToList();
        Assert.Equal(asStrings.Count, asStrings.Distinct().Count());
    }

    [Fact]
    public async Task Validate_NumPathsLessThan3_Throws()
    {
        var options = new CpcvOptions(NumPaths: 2, TestFolds: 1);
        var handler = new CpcvStudyHandler(null!);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RunAsync(
                MakeConfig(), options, CancellationToken.None));
        Assert.Contains("NumPaths must be at least 3", ex.Message);
    }

    [Fact]
    public async Task Validate_TestFoldsZero_Throws()
    {
        var options = new CpcvOptions(NumPaths: 3, TestFolds: 0);
        var handler = new CpcvStudyHandler(null!);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RunAsync(
                MakeConfig(), options, CancellationToken.None));
        Assert.Contains("TestFolds must be at least 1", ex.Message);
    }

    [Fact]
    public async Task Validate_TestFoldsGreaterOrEqualNumPaths_Throws()
    {
        var options = new CpcvOptions(NumPaths: 3, TestFolds: 3);
        var handler = new CpcvStudyHandler(null!);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.RunAsync(
                MakeConfig(), options, CancellationToken.None));
        Assert.Contains("TestFolds", ex.Message);
        Assert.Contains("must be less than NumPaths", ex.Message);
    }

    [Fact]
    public void Median_OddCount_ReturnsMiddle()
    {
        var values = new List<decimal> { 1m, 3m, 5m };
        Assert.Equal(3m, CpcvStudyHandler.Median(values));
    }

    [Fact]
    public void Median_EvenCount_ReturnsAverage()
    {
        var values = new List<decimal> { 1m, 2m, 3m, 4m };
        Assert.Equal(2.5m, CpcvStudyHandler.Median(values));
    }

    [Fact]
    public void Median_Empty_ReturnsZero()
    {
        Assert.Equal(0m, CpcvStudyHandler.Median(new List<decimal>()));
    }

    [Fact]
    public void Median_SingleValue_ReturnsThatValue()
    {
        Assert.Equal(42m, CpcvStudyHandler.Median(new List<decimal> { 42m }));
    }

    // --- Helpers ---

    private static TradingResearchEngine.Core.Configuration.ScenarioConfig MakeConfig() =>
        new("test", "Test", TradingResearchEngine.Core.Engine.ReplayMode.Bar, "csv",
            new Dictionary<string, object>
            {
                ["From"] = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ["To"] = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)
            },
            "test", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m,
            null, null, null, null);
}
