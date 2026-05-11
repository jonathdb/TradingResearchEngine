using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Moq;
using TradingResearchEngine.Application.Strategies.Composite;
using TradingResearchEngine.Application.Strategies.Composite.Conditions;
using TradingResearchEngine.Core.DataHandling;
using ExpressionCompiler = TradingResearchEngine.Application.Strategies.Composite.Conditions.ExpressionCompiler;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Property-based tests for condition evaluation short-circuit semantics.
/// **Validates: Requirement 6.4**
/// </summary>
public class ConditionShortCircuitProperties
{
    // Feature: composite-strategy-engine, Property 6: Condition evaluation short-circuit
    /// <summary>
    /// For any AND expression where the left operand evaluates to false,
    /// the right operand's indicator SHALL NOT be accessed.
    /// For any OR expression where the left operand evaluates to true,
    /// the right operand's indicator SHALL NOT be accessed.
    /// **Validates: Requirement 6.4**
    /// </summary>
    [Property(MaxTest = 20)]
    public Property AndWithFalseLeft_RightIndicatorNotAccessed()
    {
        // Generate a right-side indicator reference that we can track
        var gen = from rightId in Gen.Elements("trackA", "trackB", "trackC")
                  from rightValue in Gen.Choose(1, 100).Select(n => (decimal)n)
                  from threshold in Gen.Choose(101, 200).Select(n => (decimal)n)
                  select (rightId, rightValue, threshold);

        return Prop.ForAll(
            gen.ToArbitrary(),
            t =>
            {
                var (rightId, rightValue, threshold) = t;

                // Left condition: "falseInd > 9999" — always false because falseInd = 1
                // Right condition: "{rightId} > 0" — would be true, but should not be evaluated
                var leftNode = new ComparisonNode(
                    new IndicatorRefNode("falseInd"),
                    ComparisonOperator.GreaterThan,
                    new LiteralNode(9999));

                var rightNode = new ComparisonNode(
                    new IndicatorRefNode(rightId),
                    ComparisonOperator.GreaterThan,
                    new LiteralNode(0));

                var andNode = new LogicalNode(leftNode, LogicalOperator.And, rightNode);

                var (provider, tracker) = CreateTrackingProvider(rightId, rightValue);
                var bar = CreateTestBar();

                var compiled = ExpressionCompiler.Compile(andNode);
                var result = compiled(provider, bar);

                // Result should be false (left is false)
                // Right indicator should NOT have been accessed
                return (!result).Label("AND with false left should return false")
                    .And((!tracker.WasAccessed).Label($"Right indicator '{rightId}' should not be accessed"));
            });
    }

    [Property(MaxTest = 20)]
    public Property OrWithTrueLeft_RightIndicatorNotAccessed()
    {
        var gen = from rightId in Gen.Elements("trackX", "trackY", "trackZ")
                  from rightValue in Gen.Choose(1, 100).Select(n => (decimal)n)
                  select (rightId, rightValue);

        return Prop.ForAll(
            gen.ToArbitrary(),
            t =>
            {
                var (rightId, rightValue) = t;

                // Left condition: "trueInd > 0" — always true because trueInd = 100
                // Right condition: "{rightId} > 0" — would be true, but should not be evaluated
                var leftNode = new ComparisonNode(
                    new IndicatorRefNode("trueInd"),
                    ComparisonOperator.GreaterThan,
                    new LiteralNode(0));

                var rightNode = new ComparisonNode(
                    new IndicatorRefNode(rightId),
                    ComparisonOperator.GreaterThan,
                    new LiteralNode(0));

                var orNode = new LogicalNode(leftNode, LogicalOperator.Or, rightNode);

                var (provider, tracker) = CreateTrackingProvider(rightId, rightValue);
                var bar = CreateTestBar();

                var compiled = ExpressionCompiler.Compile(orNode);
                var result = compiled(provider, bar);

                // Result should be true (left is true)
                // Right indicator should NOT have been accessed
                return result.Label("OR with true left should return true")
                    .And((!tracker.WasAccessed).Label($"Right indicator '{rightId}' should not be accessed"));
            });
    }

    #region Helpers

    private static (IndicatorValueProvider Provider, AccessTracker Tracker) CreateTrackingProvider(
        string trackedId, decimal trackedValue)
    {
        var tracker = new AccessTracker();
        var provider = new IndicatorValueProvider();

        // Create a "falseInd" that returns 1 (for AND false-left tests)
        var falseIndMock = new Mock<IIndicatorInstance>();
        falseIndMock.Setup(m => m.Id).Returns("falseInd");
        falseIndMock.Setup(m => m.Type).Returns("mock");
        falseIndMock.Setup(m => m.IsWarm).Returns(true);
        falseIndMock.Setup(m => m.CurrentValue).Returns(1m);
        falseIndMock.Setup(m => m.PreviousValue).Returns(1m);

        // Create a "trueInd" that returns 100 (for OR true-left tests)
        var trueIndMock = new Mock<IIndicatorInstance>();
        trueIndMock.Setup(m => m.Id).Returns("trueInd");
        trueIndMock.Setup(m => m.Type).Returns("mock");
        trueIndMock.Setup(m => m.IsWarm).Returns(true);
        trueIndMock.Setup(m => m.CurrentValue).Returns(100m);
        trueIndMock.Setup(m => m.PreviousValue).Returns(100m);

        // Create the tracked indicator that records access
        var trackedMock = new Mock<IIndicatorInstance>();
        trackedMock.Setup(m => m.Id).Returns(trackedId);
        trackedMock.Setup(m => m.Type).Returns("mock");
        trackedMock.Setup(m => m.IsWarm).Returns(true);
        trackedMock.Setup(m => m.CurrentValue)
            .Returns(() => { tracker.WasAccessed = true; return trackedValue; });
        trackedMock.Setup(m => m.PreviousValue)
            .Returns(() => { tracker.WasAccessed = true; return trackedValue; });

        provider.Update(new List<IIndicatorInstance>
        {
            falseIndMock.Object,
            trueIndMock.Object,
            trackedMock.Object
        });

        return (provider, tracker);
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

    private sealed class AccessTracker
    {
        public bool WasAccessed { get; set; }
    }

    #endregion
}
