using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Application.Strategy.Composite;

namespace TradingResearchEngine.UnitTests.Strategy.Composite;

/// <summary>
/// Unit tests for CompositeStrategyConfigValidator covering valid configs,
/// duplicate IDs, unknown types, undefined references, unparseable expressions,
/// and multi-violation reporting.
/// Requirements: 13.1, 13.2, 13.3, 13.4, 13.5
/// </summary>
public class CompositeStrategyConfigValidatorTests
{
    #region Valid Config

    [Fact]
    public void Validate_ValidConfig_ReturnsEmptyErrorList()
    {
        var config = new CompositeStrategyConfig(
            "Valid Strategy",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 }),
                new("sma30", "sma", new Dictionary<string, object> { ["period"] = 30 })
            },
            "sma10 > sma30",
            "sma10 < sma30",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidConfigWithMultipleTypes_ReturnsEmptyErrorList()
    {
        var config = new CompositeStrategyConfig(
            "Multi-Type Strategy",
            new List<IndicatorConfig>
            {
                new("sma20", "sma", new Dictionary<string, object> { ["period"] = 20 }),
                new("rsi14", "rsi", new Dictionary<string, object> { ["period"] = 14 }),
                new("ema10", "ema", new Dictionary<string, object> { ["period"] = 10 })
            },
            "sma20 > ema10 AND rsi14 > 50",
            "sma20 < ema10 OR rsi14 < 30",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.Empty(errors);
    }

    #endregion

    #region Duplicate Indicator IDs

    [Fact]
    public void Validate_DuplicateIndicatorIds_DetectsViolation()
    {
        var config = new CompositeStrategyConfig(
            "Duplicate IDs",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 }),
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 30 })
            },
            "sma10 > 50",
            "sma10 < 30",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
                                     && e.Contains("sma10"));
    }

    #endregion

    #region Unknown Indicator Type

    [Fact]
    public void Validate_UnknownIndicatorType_DetectsViolation()
    {
        var config = new CompositeStrategyConfig(
            "Unknown Type",
            new List<IndicatorConfig>
            {
                new("vwap1", "vwap", new Dictionary<string, object> { ["period"] = 20 })
            },
            "vwap1 > 50",
            "vwap1 < 30",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("vwap", StringComparison.OrdinalIgnoreCase)
                                     && e.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Undefined Indicator Reference in Expression

    [Fact]
    public void Validate_UndefinedIndicatorReferenceInExpression_DetectsViolation()
    {
        var config = new CompositeStrategyConfig(
            "Undefined Reference",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 })
            },
            "sma10 > undefined_indicator",
            "sma10 < 30",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("undefined", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UndefinedReferenceInExitExpression_DetectsViolation()
    {
        var config = new CompositeStrategyConfig(
            "Undefined Exit Reference",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 })
            },
            "sma10 > 50",
            "nonexistent > sma10",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("nonexistent", StringComparison.OrdinalIgnoreCase)
                                     || e.Contains("undefined", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region Unparseable Expression

    [Fact]
    public void Validate_UnparseableEntryExpression_DetectsViolation()
    {
        var config = new CompositeStrategyConfig(
            "Bad Expression",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 })
            },
            "!!! not a valid expression !!!",
            "sma10 < 30",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("entry", StringComparison.OrdinalIgnoreCase)
                                     && e.Contains("syntax", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UnparseableExitExpression_DetectsViolation()
    {
        var config = new CompositeStrategyConfig(
            "Bad Exit Expression",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 })
            },
            "sma10 > 50",
            "@@@ garbage @@@",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("exit", StringComparison.OrdinalIgnoreCase)
                                     && e.Contains("syntax", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region All Violations Returned

    [Fact]
    public void Validate_MultipleViolations_ReturnsAllNotJustFirst()
    {
        var config = new CompositeStrategyConfig(
            "Multiple Violations",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 }),
                new("sma10", "vwap", new Dictionary<string, object> { ["period"] = 20 }) // duplicate ID + unknown type
            },
            "!!! bad entry !!!",
            "!!! bad exit !!!",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        // Should have at least 3 violations: duplicate ID, unknown type, and bad expressions
        Assert.True(errors.Count >= 3,
            $"Expected at least 3 violations but got {errors.Count}: [{string.Join("; ", errors)}]");
    }

    [Fact]
    public void Validate_EmptyExpressions_ReturnsViolationsForBoth()
    {
        var config = new CompositeStrategyConfig(
            "Empty Expressions",
            new List<IndicatorConfig>
            {
                new("sma10", "sma", new Dictionary<string, object> { ["period"] = 10 })
            },
            "",
            "",
            DirectionMode.Long);

        var errors = CompositeStrategyConfigValidator.Validate(config);

        // Should detect both empty entry and exit conditions
        Assert.True(errors.Count >= 2,
            $"Expected at least 2 violations for empty expressions but got {errors.Count}: [{string.Join("; ", errors)}]");
    }

    #endregion
}
