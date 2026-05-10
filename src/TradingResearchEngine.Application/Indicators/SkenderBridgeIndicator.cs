using Skender.Stock.Indicators;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Generic runtime bridge wrapping any Skender indicator via pre-compiled delegates.
/// Zero reflection during bar processing — all indicator calls go through typed delegates
/// resolved at construction time from <see cref="SkenderIndicatorCatalog"/>.
/// </summary>
public sealed class SkenderBridgeIndicator : IIndicatorSeries<decimal>
{
    private readonly SkenderCatalogEntry _entry;
    private readonly Dictionary<string, object> _parameters;
    private readonly string _outputField;
    private readonly List<Quote> _quotes = new();
    private readonly List<decimal> _results = new();

    /// <summary>
    /// Creates a new bridge indicator instance.
    /// </summary>
    /// <param name="indicatorKey">Key matching a <see cref="SkenderIndicatorCatalog"/> entry.</param>
    /// <param name="parameters">Parameter values for the indicator.</param>
    /// <param name="outputField">Which output field to extract (null = primary output).</param>
    public SkenderBridgeIndicator(
        string indicatorKey,
        Dictionary<string, object> parameters,
        string? outputField = null)
    {
        _entry = SkenderIndicatorCatalog.Get(indicatorKey)
            ?? throw new ArgumentException($"Unknown indicator key: '{indicatorKey}'", nameof(indicatorKey));
        _parameters = parameters;
        _outputField = outputField ?? _entry.PrimaryOutputField;
    }

    /// <inheritdoc/>
    public IReadOnlyList<decimal> Results => _results;

    /// <inheritdoc/>
    public bool IsWarm => _results.Count > 0 && _results[^1] != 0m;

    /// <inheritdoc/>
    public void Add(BarRecord bar)
    {
        _quotes.Add(new Quote
        {
            Date = bar.Timestamp.UtcDateTime,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Volume = bar.Volume
        });

        // Invoke the pre-compiled delegate to get the latest value
        var value = _entry.Invoker(_quotes, _parameters, _outputField);
        _results.Add(value ?? 0m);
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _quotes.Clear();
        _results.Clear();
    }
}
