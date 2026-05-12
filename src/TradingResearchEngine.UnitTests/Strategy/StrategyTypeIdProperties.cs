using System.Text.Json;
using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategies;

namespace TradingResearchEngine.UnitTests.Strategy;

// Feature: research-platform-v9, Property 2: StrategyTypeId JSON Round-Trip

/// <summary>
/// Property-based tests verifying that <see cref="StrategyTypeId"/> serialises to a plain
/// JSON string and round-trips without data loss.
/// </summary>
public sealed class StrategyTypeIdProperties
{
    /// <summary>
    /// For any non-null, non-empty string value, creating a StrategyTypeId, serializing
    /// to JSON, and deserializing produces an equal StrategyTypeId. The JSON representation
    /// is a plain string token (no wrapper object).
    /// </summary>
    [Property(MaxTest = 100)]
    public bool JsonRoundTrip_ProducesEqualValue(PositiveInt lengthWrap)
    {
        // Generate a kebab-case-like string of length 1–30
        var length = (lengthWrap.Get % 30) + 1;
        var chars = "abcdefghijklmnopqrstuvwxyz0123456789-";
        var value = new string(Enumerable.Range(0, length)
            .Select(i => chars[i % chars.Length])
            .ToArray());

        var original = new StrategyTypeId(value);

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<StrategyTypeId>(json);

        // Must round-trip to equal value
        if (deserialized != original) return false;

        // JSON must be a plain string token (quoted), not a wrapper object
        if (!json.StartsWith("\"") || !json.EndsWith("\"")) return false;

        // The string content must match
        if (json != $"\"{value}\"") return false;

        return true;
    }

    /// <summary>
    /// Implicit conversion from string produces a StrategyTypeId with the same value,
    /// and converting back to string preserves the value.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ImplicitConversion_PreservesValue(PositiveInt lengthWrap)
    {
        var length = (lengthWrap.Get % 20) + 1;
        var chars = "abcdefghijklmnopqrstuvwxyz-";
        var value = new string(Enumerable.Range(0, length)
            .Select(i => chars[i % chars.Length])
            .ToArray());

        StrategyTypeId id = value; // implicit conversion from string
        string back = id;          // implicit conversion to string

        return id.Value == value && back == value;
    }

    /// <summary>
    /// Two StrategyTypeIds with the same string value are equal and have the same hash code.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Equality_SameValue_AreEqual(PositiveInt lengthWrap)
    {
        var length = (lengthWrap.Get % 15) + 1;
        var chars = "abcdefghijklmnopqrstuvwxyz";
        var value = new string(Enumerable.Range(0, length)
            .Select(i => chars[i % chars.Length])
            .ToArray());

        var a = new StrategyTypeId(value);
        var b = new StrategyTypeId(value);

        return a == b && a.Equals(b) && a.GetHashCode() == b.GetHashCode();
    }

    /// <summary>
    /// ToString returns the underlying string value.
    /// </summary>
    [Property(MaxTest = 100)]
    public bool ToString_ReturnsValue(PositiveInt lengthWrap)
    {
        var length = (lengthWrap.Get % 20) + 1;
        var chars = "moving-average-crossover";
        var value = new string(Enumerable.Range(0, length)
            .Select(i => chars[i % chars.Length])
            .ToArray());

        var id = new StrategyTypeId(value);
        return id.ToString() == value;
    }
}
