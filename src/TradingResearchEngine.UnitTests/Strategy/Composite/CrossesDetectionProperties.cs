using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Moq;
using TradingResearchEngine.Application.Strategy.Composite;
using TradingResearchEngine.Application.Strategy.Composite.Conditions;
using TradingResearchEngine.Core.DataHandling;
using ExpressionCompiler = TradingResearchEngine.Application.Strategy.Composite.Conditions.ExpressionCompiler;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Property-based tests for crosses_above and crosses_below detection correctness.
/// **Validates: Requirements 4.6, 14.4**
/// </summary>
public class CrossesDetectionProperties
{
    // Feature: composite-strategy-engine, Property 7: Crosses detection correctness
    /// <summary>
    /// For any pair of indicator value sequences (a, b),
    /// crosses_above(a, b) SHALL be true only when a[current] > b[current] AND a[previous] &lt;= b[previous].
    /// **Validates: Requirements 4.6, 14.4**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property CrossesAbove_TrueOnlyWhenCurrentAboveAndPreviousBelowOrEqual()
    {
        var gen = from aCurrent in Gen.Choose(-1000, 1000).Select(n => (decimal)n / 10m)
                  from bCurrent in Gen.Choose(-1000, 1000).Select(n => (decimal)n / 10m)
                  from aPrevious in Gen.Choose(-1000, 1000).Select(n => (decimal)n / 10m)
                  from bPrevious in Gen.Choose(-1000, 1000).Select(n => (decimal)n / 10m)
                  select (aCurrent, bCurrent, aPrevious, bPrevious);

        return Prop.ForAll(
            gen.ToArbitrary(),
            t =>
            {
                var (aCurrent, bCurrent, aPrevious, bPrevious) = t;

                var crossAboveNode = new CrossNode(
                    new IndicatorRefNode("indA"),
                    new IndicatorRefNode("indB"),
                    CrossDirection.Above);

                var provider = CreateProviderForCross(aCurrent, bCurrent, aPrevious, bPrevious);
                var bar = CreateTestBar();

                var compiled = ExpressionCompiler.Compile(crossAboveNode);
                var result = compiled(provider, bar);

                // Mathematical definition: a[current] > b[current] AND a[previous] <= b[previous]
                var expected = aCurrent > bCurrent && aPrevious <= bPrevious;

                return (result == expected)
                    .Label($"crosses_above: aCurr={aCurrent}, bCurr={bCurrent}, aPrev={aPrevious}, bPrev={bPrevious} → expected={expected}, got={result}");
            });
    }

    // Feature: composite-strategy-engine, Property 7: Crosses detection correctness
    /// <summary>
    /// For any pair of indicator value sequences (a, b),
    /// crosses_below(a, b) SHALL be true only when a[current] &lt; b[current] AND a[previous] >= b[previous].
    /// **Validates: Requirements 4.6, 14.4**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property CrossesBelow_TrueOnlyWhenCurrentBelowAndPreviousAboveOrEqual()
    {
        var gen = from aCurrent in Gen.Choose(-1000, 1000).Select(n => (decimal)n / 10m)
                  from bCurrent in Gen.Choose(-1000, 1000).Select(n => (decimal)n / 10m)
                  from aPrevious in Gen.Choose(-1000, 1000).Select(n => (decimal)n / 10m)
                  from bPrevious in Gen.Choose(-1000, 1000).Select(n => (decimal)n / 10m)
                  select (aCurrent, bCurrent, aPrevious, bPrevious);

        return Prop.ForAll(
            gen.ToArbitrary(),
            t =>
            {
                var (aCurrent, bCurrent, aPrevious, bPrevious) = t;

                var crossBelowNode = new CrossNode(
                    new IndicatorRefNode("indA"),
                    new IndicatorRefNode("indB"),
                    CrossDirection.Below);

                var provider = CreateProviderForCross(aCurrent, bCurrent, aPrevious, bPrevious);
                var bar = CreateTestBar();

                var compiled = ExpressionCompiler.Compile(crossBelowNode);
                var result = compiled(provider, bar);

                // Mathematical definition: a[current] < b[current] AND a[previous] >= b[previous]
                var expected = aCurrent < bCurrent && aPrevious >= bPrevious;

                return (result == expected)
                    .Label($"crosses_below: aCurr={aCurrent}, bCurr={bCurrent}, aPrev={aPrevious}, bPrev={bPrevious} → expected={expected}, got={result}");
            });
    }

    #region Helpers

    private static IndicatorValueProvider CreateProviderForCross(
        decimal aCurrent, decimal bCurrent, decimal aPrevious, decimal bPrevious)
    {
        var provider = new IndicatorValueProvider();

        var indA = new Mock<IIndicatorInstance>();
        indA.Setup(m => m.Id).Returns("indA");
        indA.Setup(m => m.Type).Returns("mock");
        indA.Setup(m => m.IsWarm).Returns(true);
        indA.Setup(m => m.CurrentValue).Returns(aCurrent);
        indA.Setup(m => m.PreviousValue).Returns(aPrevious);

        var indB = new Mock<IIndicatorInstance>();
        indB.Setup(m => m.Id).Returns("indB");
        indB.Setup(m => m.Type).Returns("mock");
        indB.Setup(m => m.IsWarm).Returns(true);
        indB.Setup(m => m.CurrentValue).Returns(bCurrent);
        indB.Setup(m => m.PreviousValue).Returns(bPrevious);

        provider.Update(new List<IIndicatorInstance> { indA.Object, indB.Object });
        return provider;
    }

    private static BarRecord CreateTestBar()
    {
        return new BarRecord(
            Symbol: "TEST",
            Interval: "D1",
            Open: 100m,
            High: 105m,
            Low: 95m,
            Close: 102m,
            Volume: 1000m,
            Timestamp: DateTimeOffset.UtcNow);
    }

    #endregion
}
