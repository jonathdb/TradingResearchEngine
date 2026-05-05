using TradingResearchEngine.Application.Strategy;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

/// <summary>
/// Unit tests for <see cref="SchemaValidator"/> covering parameter validation
/// against schema bounds and risk profile constraint checks.
/// </summary>
public class SchemaValidatorTests
{
    // ── Helper factories ──

    private static StrategyParameterSchema MakeSchema(
        string name = "period",
        string displayName = "Period",
        string type = "int",
        object defaultValue = null!,
        bool isRequired = true,
        object? min = null,
        object? max = null) =>
        new(name, displayName, type, defaultValue ?? 14, isRequired, min, max,
            null, "Test parameter", SensitivityHint.Low, "Signal", false, 0);

    // ── ValidateParameters: Required field checks ──

    [Fact]
    public void ValidateParameters_RequiredFieldMissing_ReturnsError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", isRequired: true)
        };
        var parameters = new Dictionary<string, object>();

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Single(errors);
        Assert.Equal("period", errors[0].FieldName);
        Assert.Contains("Period is required", errors[0].Message);
    }

    [Fact]
    public void ValidateParameters_OptionalFieldMissing_ReturnsNoError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", isRequired: false)
        };
        var parameters = new Dictionary<string, object>();

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateParameters_RequiredFieldPresent_ReturnsNoError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", isRequired: true)
        };
        var parameters = new Dictionary<string, object> { ["period"] = 14 };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Empty(errors);
    }

    // ── ValidateParameters: Min/Max bound checks ──

    [Fact]
    public void ValidateParameters_ValueBelowMin_ReturnsError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", min: 5, max: 200)
        };
        var parameters = new Dictionary<string, object> { ["period"] = 3 };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Single(errors);
        Assert.Equal("period", errors[0].FieldName);
        Assert.Contains("must be >= 5", errors[0].Message);
    }

    [Fact]
    public void ValidateParameters_ValueAboveMax_ReturnsError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", min: 5, max: 200)
        };
        var parameters = new Dictionary<string, object> { ["period"] = 250 };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Single(errors);
        Assert.Equal("period", errors[0].FieldName);
        Assert.Contains("must be <= 200", errors[0].Message);
    }

    [Fact]
    public void ValidateParameters_ValueAtMin_ReturnsNoError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", min: 5, max: 200)
        };
        var parameters = new Dictionary<string, object> { ["period"] = 5 };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateParameters_ValueAtMax_ReturnsNoError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", min: 5, max: 200)
        };
        var parameters = new Dictionary<string, object> { ["period"] = 200 };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateParameters_ValueWithinBounds_ReturnsNoError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", min: 5, max: 200)
        };
        var parameters = new Dictionary<string, object> { ["period"] = 50 };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Empty(errors);
    }

    // ── ValidateParameters: Decimal values ──

    [Fact]
    public void ValidateParameters_DecimalValueBelowMin_ReturnsError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "threshold", displayName: "Threshold", type: "decimal", min: 0.5m, max: 3.0m)
        };
        var parameters = new Dictionary<string, object> { ["threshold"] = 0.1m };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Single(errors);
        Assert.Contains("must be >= 0.5", errors[0].Message);
    }

    [Fact]
    public void ValidateParameters_DecimalValueWithinBounds_ReturnsNoError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "threshold", displayName: "Threshold", type: "decimal", min: 0.5m, max: 3.0m)
        };
        var parameters = new Dictionary<string, object> { ["threshold"] = 1.5m };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Empty(errors);
    }

    // ── ValidateParameters: String numeric values ──

    [Fact]
    public void ValidateParameters_StringNumericValue_ValidatesCorrectly()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", min: 5, max: 200)
        };
        var parameters = new Dictionary<string, object> { ["period"] = "3" };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Single(errors);
        Assert.Contains("must be >= 5", errors[0].Message);
    }

    // ── ValidateParameters: Multiple schemas ──

    [Fact]
    public void ValidateParameters_MultipleErrors_ReturnsAll()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", isRequired: true, min: 5, max: 200),
            MakeSchema(name: "threshold", displayName: "Threshold", isRequired: true, min: 0.5m, max: 3.0m)
        };
        var parameters = new Dictionary<string, object>
        {
            ["period"] = 1,
            ["threshold"] = 5.0m
        };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.FieldName == "period");
        Assert.Contains(errors, e => e.FieldName == "threshold");
    }

    // ── ValidateParameters: No bounds defined ──

    [Fact]
    public void ValidateParameters_NoBoundsOnSchema_ReturnsNoError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", min: null, max: null)
        };
        var parameters = new Dictionary<string, object> { ["period"] = 999 };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Empty(errors);
    }

    // ── ValidateParameters: Null value for required field ──

    [Fact]
    public void ValidateParameters_NullValueForRequiredField_ReturnsError()
    {
        var schemas = new List<StrategyParameterSchema>
        {
            MakeSchema(name: "period", displayName: "Period", isRequired: true)
        };
        var parameters = new Dictionary<string, object> { ["period"] = null! };

        var errors = SchemaValidator.ValidateParameters(parameters, schemas);

        Assert.Single(errors);
        Assert.Contains("is required", errors[0].Message);
    }

    // ── ValidateRiskProfile: Stop-loss checks ──

    [Fact]
    public void ValidateRiskProfile_StopLossZero_ReturnsError()
    {
        var riskParams = new Dictionary<string, object>();

        var errors = SchemaValidator.ValidateRiskProfile(riskParams, 0m);

        Assert.Single(errors);
        Assert.Equal("StopLoss", errors[0].FieldName);
        Assert.Contains("greater than 0", errors[0].Message);
    }

    [Fact]
    public void ValidateRiskProfile_StopLossNegative_ReturnsError()
    {
        var riskParams = new Dictionary<string, object>();

        var errors = SchemaValidator.ValidateRiskProfile(riskParams, -1m);

        Assert.Single(errors);
        Assert.Equal("StopLoss", errors[0].FieldName);
    }

    [Fact]
    public void ValidateRiskProfile_StopLossPositive_ReturnsNoStopLossError()
    {
        var riskParams = new Dictionary<string, object>();

        var errors = SchemaValidator.ValidateRiskProfile(riskParams, 2.5m);

        Assert.Empty(errors);
    }

    // ── ValidateRiskProfile: Allocation sum checks ──

    [Fact]
    public void ValidateRiskProfile_AllocationSumExceeds100_ReturnsError()
    {
        var riskParams = new Dictionary<string, object>
        {
            ["equities"] = 60m,
            ["bonds"] = 30m,
            ["commodities"] = 20m
        };

        var errors = SchemaValidator.ValidateRiskProfile(riskParams, 1m);

        Assert.Single(errors);
        Assert.Equal("Allocation", errors[0].FieldName);
        Assert.Contains("100%", errors[0].Message);
    }

    [Fact]
    public void ValidateRiskProfile_AllocationSumExactly100_ReturnsNoError()
    {
        var riskParams = new Dictionary<string, object>
        {
            ["equities"] = 60m,
            ["bonds"] = 40m
        };

        var errors = SchemaValidator.ValidateRiskProfile(riskParams, 1m);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateRiskProfile_AllocationSumBelow100_ReturnsNoError()
    {
        var riskParams = new Dictionary<string, object>
        {
            ["equities"] = 40m,
            ["bonds"] = 30m
        };

        var errors = SchemaValidator.ValidateRiskProfile(riskParams, 1m);

        Assert.Empty(errors);
    }

    // ── ValidateRiskProfile: Combined errors ──

    [Fact]
    public void ValidateRiskProfile_StopLossZeroAndAllocationOver100_ReturnsBothErrors()
    {
        var riskParams = new Dictionary<string, object>
        {
            ["equities"] = 80m,
            ["bonds"] = 30m
        };

        var errors = SchemaValidator.ValidateRiskProfile(riskParams, 0m);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.FieldName == "StopLoss");
        Assert.Contains(errors, e => e.FieldName == "Allocation");
    }

    // ── ValidateRiskProfile: Non-numeric values in risk params ──

    [Fact]
    public void ValidateRiskProfile_NonNumericValuesIgnored_ReturnsNoAllocationError()
    {
        var riskParams = new Dictionary<string, object>
        {
            ["equities"] = 50m,
            ["description"] = "some text"
        };

        var errors = SchemaValidator.ValidateRiskProfile(riskParams, 1m);

        Assert.Empty(errors);
    }

    // ── ValidationError record ──

    [Fact]
    public void ValidationError_RecordEquality_WorksCorrectly()
    {
        var error1 = new ValidationError("field", "message");
        var error2 = new ValidationError("field", "message");

        Assert.Equal(error1, error2);
    }

    [Fact]
    public void ValidationError_RecordInequality_WorksCorrectly()
    {
        var error1 = new ValidationError("field1", "message");
        var error2 = new ValidationError("field2", "message");

        Assert.NotEqual(error1, error2);
    }
}
