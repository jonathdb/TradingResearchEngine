using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Application.Strategy.Composite;

/// <summary>
/// Base adapter wrapping an <see cref="IIndicatorSeries{TResult}"/> into an
/// <see cref="IIndicatorInstance"/> with typed value extraction and previous-value tracking.
/// </summary>
/// <typeparam name="TResult">The Skender indicator result type.</typeparam>
public abstract class IndicatorInstanceAdapter<TResult> : IIndicatorInstance
{
    private readonly IIndicatorSeries<TResult> _series;

    /// <summary>
    /// Initializes a new adapter wrapping the specified indicator series.
    /// </summary>
    /// <param name="id">The unique identifier for this indicator instance.</param>
    /// <param name="type">The indicator type (e.g., "sma", "rsi").</param>
    /// <param name="series">The underlying indicator series to wrap.</param>
    protected IndicatorInstanceAdapter(string id, string type, IIndicatorSeries<TResult> series)
    {
        Id = id;
        Type = type;
        _series = series;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <inheritdoc />
    public string Type { get; }

    /// <inheritdoc />
    public bool IsWarm => _series.IsWarm;

    /// <inheritdoc />
    public void Add(BarRecord bar) => _series.Add(bar);

    /// <inheritdoc />
    public void Reset() => _series.Reset();

    /// <inheritdoc />
    public decimal? CurrentValue => ExtractPrimaryValue(CurrentResult);

    /// <inheritdoc />
    public decimal? PreviousValue => ExtractPrimaryValue(PreviousResult);

    /// <inheritdoc />
    public decimal? GetSubValue(string subProperty) => ExtractSubValue(CurrentResult, subProperty);

    /// <inheritdoc />
    public decimal? GetPreviousSubValue(string subProperty) => ExtractSubValue(PreviousResult, subProperty);

    /// <summary>
    /// Extracts the primary numeric value from an indicator result.
    /// </summary>
    /// <param name="result">The indicator result, or default if unavailable.</param>
    /// <returns>The primary value as decimal, or null if unavailable.</returns>
    protected abstract decimal? ExtractPrimaryValue(TResult? result);

    /// <summary>
    /// Extracts a named sub-property value from an indicator result.
    /// Returns null for single-value indicators or unknown sub-property names.
    /// </summary>
    /// <param name="result">The indicator result, or default if unavailable.</param>
    /// <param name="subProperty">The sub-property name (case-insensitive).</param>
    /// <returns>The sub-property value as decimal, or null if unavailable.</returns>
    protected virtual decimal? ExtractSubValue(TResult? result, string subProperty) => null;

    /// <summary>
    /// Gets the most recent (current) result from the series, or default if no results exist.
    /// </summary>
    private TResult? CurrentResult =>
        _series.Results.Count > 0 ? _series.Results[^1] : default;

    /// <summary>
    /// Gets the second-most-recent (previous) result from the series, or default if fewer than 2 results exist.
    /// </summary>
    private TResult? PreviousResult =>
        _series.Results.Count > 1 ? _series.Results[^2] : default;

    /// <summary>
    /// Converts a nullable double to a nullable decimal.
    /// </summary>
    /// <param name="value">The nullable double value.</param>
    /// <returns>The equivalent nullable decimal value.</returns>
    protected static decimal? ToDecimal(double? value) =>
        value.HasValue ? (decimal)value.Value : null;
}

/// <summary>
/// Adapter for single-value indicators that produce a result with one primary numeric property.
/// Used for SMA, EMA, RSI, and ATR indicators.
/// </summary>
/// <typeparam name="TResult">The Skender indicator result type.</typeparam>
internal sealed class SingleValueIndicatorAdapter<TResult> : IndicatorInstanceAdapter<TResult>
{
    private readonly Func<TResult, double?> _valueExtractor;

    /// <summary>
    /// Initializes a new single-value indicator adapter.
    /// </summary>
    /// <param name="id">The unique identifier for this indicator instance.</param>
    /// <param name="type">The indicator type.</param>
    /// <param name="series">The underlying indicator series.</param>
    /// <param name="valueExtractor">Function to extract the primary value from a result.</param>
    public SingleValueIndicatorAdapter(
        string id,
        string type,
        IIndicatorSeries<TResult> series,
        Func<TResult, double?> valueExtractor)
        : base(id, type, series)
    {
        _valueExtractor = valueExtractor;
    }

    /// <inheritdoc />
    protected override decimal? ExtractPrimaryValue(TResult? result) =>
        result is null ? null : ToDecimal(_valueExtractor(result));
}

/// <summary>
/// Adapter for multi-value indicators that produce results with multiple named sub-properties.
/// Used for MACD, Bollinger Bands, Stochastic, and Donchian indicators.
/// </summary>
/// <typeparam name="TResult">The Skender indicator result type.</typeparam>
internal sealed class MultiValueIndicatorAdapter<TResult> : IndicatorInstanceAdapter<TResult>
{
    private readonly Func<TResult, double?> _primaryExtractor;
    private readonly IReadOnlyDictionary<string, Func<TResult, double?>> _subExtractors;

    /// <summary>
    /// Initializes a new multi-value indicator adapter.
    /// </summary>
    /// <param name="id">The unique identifier for this indicator instance.</param>
    /// <param name="type">The indicator type.</param>
    /// <param name="series">The underlying indicator series.</param>
    /// <param name="primaryExtractor">Function to extract the primary value from a result.</param>
    /// <param name="subExtractors">Dictionary mapping sub-property names to extraction functions (case-insensitive).</param>
    public MultiValueIndicatorAdapter(
        string id,
        string type,
        IIndicatorSeries<TResult> series,
        Func<TResult, double?> primaryExtractor,
        IReadOnlyDictionary<string, Func<TResult, double?>> subExtractors)
        : base(id, type, series)
    {
        _primaryExtractor = primaryExtractor;
        _subExtractors = subExtractors;
    }

    /// <inheritdoc />
    protected override decimal? ExtractPrimaryValue(TResult? result) =>
        result is null ? null : ToDecimal(_primaryExtractor(result));

    /// <inheritdoc />
    protected override decimal? ExtractSubValue(TResult? result, string subProperty)
    {
        if (result is null)
            return null;

        // Case-insensitive sub-property lookup
        foreach (var kvp in _subExtractors)
        {
            if (string.Equals(kvp.Key, subProperty, StringComparison.OrdinalIgnoreCase))
                return ToDecimal(kvp.Value(result));
        }

        return null;
    }
}
