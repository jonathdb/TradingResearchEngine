using Skender.Stock.Indicators;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Generic runtime bridge wrapping any Skender indicator via pre-compiled delegates.
/// Zero reflection during bar processing — all indicator calls go through typed delegates
/// resolved at construction time from <see cref="SkenderIndicatorCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// The bridge maintains a bounded internal window of <see cref="Quote"/> (capacity = warmup × 2).
/// On each <see cref="Add"/> call, the bar is converted to a Quote, appended to the window
/// (evicting the oldest if at capacity), and the pre-compiled invoker delegate is called to
/// produce the latest indicator value for the specified output field.
/// </para>
/// <para>
/// All method resolution happens at construction time via <see cref="SkenderIndicatorCatalog"/>.
/// The hot path (per-bar processing) executes only the pre-compiled delegate — no reflection,
/// no dictionary lookups for method resolution, no dynamic dispatch beyond the delegate call.
/// </para>
/// </remarks>
public sealed class SkenderBridgeIndicator : IIndicatorSeries<decimal>
{
    private readonly SkenderCatalogEntry _entry;
    private readonly Dictionary<string, object> _parameters;
    private readonly string _outputField;
    private readonly Queue<Quote> _quotes;
    private readonly int _windowCapacity;
    private readonly List<decimal> _results = new();
    private bool _isWarm;

    /// <summary>
    /// Default warmup multiplier used to size the bounded quote window.
    /// </summary>
    private const int DefaultWarmupMultiplier = 2;

    /// <summary>
    /// Minimum window capacity to ensure indicators with zero or very small warmup periods
    /// still have enough data for valid computation.
    /// </summary>
    private const int MinWindowCapacity = 100;

    /// <summary>
    /// Creates a new bridge indicator instance.
    /// </summary>
    /// <param name="indicatorKey">Key matching a <see cref="SkenderIndicatorCatalog"/> entry (case-insensitive).</param>
    /// <param name="parameters">Parameter values for the indicator.</param>
    /// <param name="outputField">Which output field to extract (null = primary output).</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="indicatorKey"/> does not match any catalog entry.</exception>
    public SkenderBridgeIndicator(
        string indicatorKey,
        Dictionary<string, object> parameters,
        string? outputField = null)
    {
        _entry = SkenderIndicatorCatalog.Get(indicatorKey)
            ?? throw new ArgumentException($"Unknown indicator key: '{indicatorKey}'", nameof(indicatorKey));
        _parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        _outputField = outputField ?? _entry.PrimaryOutputField;

        // Compute bounded window capacity from warmup period.
        // Uses the larger of (warmup * multiplier) or MinWindowCapacity to ensure
        // enough data for indicators that derive warmup from parameters.
        var warmup = ComputeWarmupFromParameters();
        _windowCapacity = Math.Max(warmup * DefaultWarmupMultiplier, MinWindowCapacity);
        _quotes = new Queue<Quote>(_windowCapacity);
    }

    /// <inheritdoc/>
    public IReadOnlyList<decimal> Results => _results;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>true</c> once the indicator has produced at least one non-null value,
    /// indicating that enough bars have been processed for valid computation.
    /// </remarks>
    public bool IsWarm => _isWarm;

    /// <inheritdoc/>
    public void Add(BarRecord bar)
    {
        var quote = new Quote
        {
            Date = bar.Timestamp.UtcDateTime,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Volume = bar.Volume
        };

        if (_quotes.Count >= _windowCapacity)
        {
            _quotes.Dequeue();
        }

        _quotes.Enqueue(quote);

        // Invoke the pre-compiled delegate to get the latest value.
        // The delegate was resolved at construction time — zero reflection here.
        var windowedQuotes = _quotes.ToList();
        var value = _entry.Invoker(windowedQuotes, _parameters, _outputField);
        _results.Add(value ?? 0m);

        // Track warmth: once the indicator produces a non-null result, it is warm.
        if (!_isWarm && value.HasValue)
        {
            _isWarm = true;
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _quotes.Clear();
        _results.Clear();
        _isWarm = false;
    }

    /// <summary>
    /// Computes the effective warmup period from the indicator's parameters.
    /// For indicators with explicit WarmupPeriod in the catalog, uses that value.
    /// Otherwise, derives warmup from the largest period-like parameter.
    /// </summary>
    private int ComputeWarmupFromParameters()
    {
        if (_entry.WarmupPeriod > 0)
            return _entry.WarmupPeriod;

        // Derive warmup from the largest integer parameter (typically the slowest period).
        var maxPeriod = 0;
        foreach (var paramDef in _entry.Parameters)
        {
            if (paramDef.ClrType == typeof(int) && _parameters.TryGetValue(paramDef.Name, out var val))
            {
                var intVal = Convert.ToInt32(val);
                if (intVal > maxPeriod)
                    maxPeriod = intVal;
            }
            else if (paramDef.ClrType == typeof(int))
            {
                var defaultVal = Convert.ToInt32(paramDef.DefaultValue);
                if (defaultVal > maxPeriod)
                    maxPeriod = defaultVal;
            }
        }

        // For multi-period indicators (like MACD), sum the two largest periods
        // to account for cascaded warmup requirements.
        var periods = new List<int>();
        foreach (var paramDef in _entry.Parameters)
        {
            if (paramDef.ClrType == typeof(int))
            {
                var val = _parameters.TryGetValue(paramDef.Name, out var v)
                    ? Convert.ToInt32(v)
                    : Convert.ToInt32(paramDef.DefaultValue);
                periods.Add(val);
            }
        }

        if (periods.Count >= 2)
        {
            periods.Sort();
            // Sum the two largest periods for cascaded indicators
            return periods[^1] + periods[^2];
        }

        return maxPeriod > 0 ? maxPeriod : 50; // Fallback
    }
}
