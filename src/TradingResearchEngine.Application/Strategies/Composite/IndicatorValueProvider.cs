using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Application.Strategies.Composite;

/// <summary>
/// Runtime context providing current indicator values by ID.
/// Updated after all indicators have processed the current bar, before condition evaluation.
/// Supports dot-notation for multi-value indicators (e.g., "macd1.Signal").
/// </summary>
public sealed class IndicatorValueProvider
{
    private readonly Dictionary<string, IIndicatorInstance> _instances = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Updates all indicator values from the current indicator instances.
    /// Must be called after all indicators have processed the current bar.
    /// </summary>
    /// <param name="indicators">The list of indicator instances to extract values from.</param>
    public void Update(IReadOnlyList<IIndicatorInstance> indicators)
    {
        _instances.Clear();
        foreach (var indicator in indicators)
        {
            _instances[indicator.Id] = indicator;
        }
    }

    /// <summary>
    /// Gets the current value for an indicator reference.
    /// Supports dot notation (e.g., "macd1.Signal") for multi-value indicators.
    /// </summary>
    /// <param name="reference">The indicator reference, optionally with dot-notation sub-property.</param>
    /// <returns>The current value, or null if the indicator is not warm or not found.</returns>
    public decimal? GetValue(string reference)
    {
        var (id, subProperty) = ParseReference(reference);

        if (!_instances.TryGetValue(id, out var instance))
            return null;

        if (!instance.IsWarm)
            return null;

        return subProperty is null
            ? instance.CurrentValue
            : instance.GetSubValue(subProperty);
    }

    /// <summary>
    /// Gets the previous value for an indicator reference (for cross-detection).
    /// Supports dot notation (e.g., "macd1.Signal") for multi-value indicators.
    /// </summary>
    /// <param name="reference">The indicator reference, optionally with dot-notation sub-property.</param>
    /// <returns>The previous value, or null if the indicator is not warm or not found.</returns>
    public decimal? GetPreviousValue(string reference)
    {
        var (id, subProperty) = ParseReference(reference);

        if (!_instances.TryGetValue(id, out var instance))
            return null;

        if (!instance.IsWarm)
            return null;

        return subProperty is null
            ? instance.PreviousValue
            : instance.GetPreviousSubValue(subProperty);
    }

    /// <summary>
    /// Gets whether all configured indicators are warm (have received enough bars for valid computation).
    /// </summary>
    public bool AllWarm => _instances.Count > 0 && _instances.Values.All(i => i.IsWarm);

    /// <summary>
    /// Resets the value provider, clearing all cached indicator references.
    /// </summary>
    public void Reset()
    {
        _instances.Clear();
    }

    private static (string Id, string? SubProperty) ParseReference(string reference)
    {
        var dotIndex = reference.IndexOf('.');
        if (dotIndex < 0)
            return (reference, null);

        return (reference[..dotIndex], reference[(dotIndex + 1)..]);
    }
}
