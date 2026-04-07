using System.Text.Json;
using TradingResearchEngine.Application.PropFirm;

namespace TradingResearchEngine.Infrastructure.Validation;

/// <summary>
/// Validates FirmRuleSet JSON deserialization, returning structured errors for missing fields.
/// </summary>
public static class FirmRuleSetValidator
{
    private static readonly string[] RequiredFields =
    {
        "FirmName", "MaxDailyDrawdownPercent", "MaxTotalDrawdownPercent",
        "MinTradingDays", "CustomRules"
    };

    /// <summary>
    /// Attempts to deserialize and validate a FirmRuleSet from JSON.
    /// Returns a list of validation errors (empty if valid).
    /// </summary>
    public static (FirmRuleSet? Result, IReadOnlyList<string> Errors) Validate(string json)
    {
        var errors = new List<string>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
            return (null, errors);
        }

        var root = doc.RootElement;
        foreach (var field in RequiredFields)
        {
            if (!root.TryGetProperty(field, out _))
                errors.Add($"Missing required field: {field}");
        }

        if (errors.Count > 0) return (null, errors);

        try
        {
            var result = JsonSerializer.Deserialize<FirmRuleSet>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return (result, errors);
        }
        catch (JsonException ex)
        {
            errors.Add($"Deserialization failed: {ex.Message}");
            return (null, errors);
        }
    }
}
