using System.Text.RegularExpressions;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Generates human-readable, URL-safe strategy IDs with sufficient randomness
/// to prevent collisions.
/// </summary>
/// <remarks>
/// <para>
/// IDs are composed of a slugified strategy name (truncated to 20 characters)
/// followed by an 8-character hexadecimal suffix derived from a new GUID,
/// providing at least 32 bits of randomness.
/// </para>
/// <para>
/// Example output: <c>vol-trend-eurusd-a1b2c3d4</c>.
/// </para>
/// </remarks>
public static partial class StrategyIdGenerator
{
    /// <summary>
    /// Generates a strategy ID from the given name.
    /// </summary>
    /// <param name="strategyName">
    /// The human-readable strategy name to slugify. If null or whitespace,
    /// a generic "strategy-" prefix with a random suffix is used.
    /// </param>
    /// <returns>
    /// A URL-safe identifier matching <c>^[a-z0-9-]+$</c> with at least
    /// 32 bits of randomness (8 hex characters).
    /// </returns>
    public static string Generate(string? strategyName)
    {
        if (string.IsNullOrWhiteSpace(strategyName))
            return $"strategy-{Guid.NewGuid().ToString("N")[..8]}";

        var slug = NonSlugChars().Replace(
            strategyName.ToLowerInvariant().Replace(' ', '-'), "");

        if (slug.Length == 0)
            return $"strategy-{Guid.NewGuid().ToString("N")[..8]}";

        var truncatedSlug = slug[..Math.Min(slug.Length, 20)];
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{truncatedSlug}-{suffix}";
    }

    [GeneratedRegex("[^a-z0-9-]", RegexOptions.Compiled)]
    private static partial Regex NonSlugChars();
}
