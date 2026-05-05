namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Pure validation helper that checks parameter values against
/// <see cref="StrategyParameterSchema"/> bounds and validates risk profile constraints.
/// Used by the builder's <c>CanAdvance()</c> logic for Steps 3 and 4.
/// </summary>
public static class SchemaValidator
{
    /// <summary>
    /// Validates each parameter value against the corresponding schema definition.
    /// Checks required fields are present and numeric values fall within [Min, Max] bounds.
    /// </summary>
    /// <param name="parameters">The user-supplied parameter values keyed by parameter name.</param>
    /// <param name="schemas">The strategy's parameter schema definitions.</param>
    /// <returns>A list of validation errors; empty if all parameters are valid.</returns>
    public static IReadOnlyList<ValidationError> ValidateParameters(
        Dictionary<string, object> parameters,
        IReadOnlyList<StrategyParameterSchema> schemas)
    {
        var errors = new List<ValidationError>();

        foreach (var schema in schemas)
        {
            if (!parameters.TryGetValue(schema.Name, out var value) || value is null)
            {
                if (schema.IsRequired)
                    errors.Add(new ValidationError(schema.Name, $"{schema.DisplayName} is required."));
                continue;
            }

            if (schema.Min is not null && CompareValues(value, schema.Min) < 0)
                errors.Add(new ValidationError(schema.Name, $"{schema.DisplayName} must be >= {schema.Min}."));

            if (schema.Max is not null && CompareValues(value, schema.Max) > 0)
                errors.Add(new ValidationError(schema.Name, $"{schema.DisplayName} must be <= {schema.Max}."));
        }

        return errors;
    }

    /// <summary>
    /// Validates risk profile constraints: allocation percentages must sum to 100% or less,
    /// and stop-loss must be positive.
    /// </summary>
    /// <param name="riskParams">Risk allocation parameters keyed by name, with numeric percentage values.</param>
    /// <param name="stopLoss">The configured stop-loss value.</param>
    /// <returns>A list of validation errors; empty if the risk profile is valid.</returns>
    public static IReadOnlyList<ValidationError> ValidateRiskProfile(
        Dictionary<string, object> riskParams, decimal stopLoss)
    {
        var errors = new List<ValidationError>();

        if (stopLoss <= 0m)
            errors.Add(new ValidationError("StopLoss", "Stop-loss must be greater than 0."));

        var allocationSum = 0m;
        foreach (var (key, value) in riskParams)
        {
            if (TryConvertToDecimal(value, out var numericValue))
            {
                allocationSum += numericValue;
            }
        }

        if (allocationSum > 100m)
            errors.Add(new ValidationError("Allocation", $"Allocation percentages must sum to 100% or less (current: {allocationSum}%)."));

        return errors;
    }

    /// <summary>
    /// Compares two values numerically. Both values are converted to <see cref="decimal"/>
    /// for comparison. Returns negative if left &lt; right, zero if equal, positive if left &gt; right.
    /// Falls back to 0 (equal) if either value cannot be converted.
    /// </summary>
    private static int CompareValues(object left, object right)
    {
        if (TryConvertToDecimal(left, out var leftDecimal) &&
            TryConvertToDecimal(right, out var rightDecimal))
        {
            return leftDecimal.CompareTo(rightDecimal);
        }

        return 0;
    }

    /// <summary>
    /// Attempts to convert an object to <see cref="decimal"/>, handling common numeric types
    /// and string representations.
    /// </summary>
    private static bool TryConvertToDecimal(object? value, out decimal result)
    {
        result = 0m;
        if (value is null) return false;

        return value switch
        {
            decimal d => Assign(d, out result),
            int i => Assign(i, out result),
            long l => Assign(l, out result),
            double d => Assign((decimal)d, out result),
            float f => Assign((decimal)f, out result),
            string s => decimal.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out result),
            _ => decimal.TryParse(value.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out result)
        };
    }

    private static bool Assign(decimal value, out decimal result)
    {
        result = value;
        return true;
    }
}

/// <summary>
/// Represents a single validation error with the field name and a human-readable message.
/// </summary>
/// <param name="FieldName">The name of the field that failed validation.</param>
/// <param name="Message">A human-readable description of the validation failure.</param>
public sealed record ValidationError(string FieldName, string Message);
