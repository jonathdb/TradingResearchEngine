using Skender.Stock.Indicators;
using TradingResearchEngine.Core.DataHandling;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Generic abstract base class that adapts Skender.Stock.Indicators batch methods
/// into a streaming, warm-up-aware <see cref="IIndicatorSeries{TResult}"/> implementation.
/// </summary>
/// <remarks>
/// Maintains a bounded internal <see cref="Queue{T}"/> of <see cref="Quote"/> with capacity
/// <c>WarmupPeriod × 2</c>. On each <see cref="Add"/> call, the adapter converts the
/// <see cref="BarRecord"/> to a <see cref="Quote"/>, enqueues it (dequeuing the oldest if
/// at capacity), and calls the abstract <see cref="Compute"/> method on the windowed contents.
/// This ensures O(WarmupPeriod) per-bar cost rather than O(n) where n is total bars processed.
/// </remarks>
/// <typeparam name="TResult">The Skender indicator result type.</typeparam>
public abstract class SkenderIndicatorAdapter<TResult> : IIndicatorSeries<TResult>
{
    private Queue<Quote>? _quotes;
    private readonly List<TResult> _results = new();
    private int _capacity;

    /// <summary>
    /// Initializes the adapter. Queue capacity is lazily determined on first <see cref="Add"/>
    /// call to avoid accessing the abstract <see cref="WarmupPeriod"/> property before
    /// derived class fields are initialized.
    /// </summary>
    protected SkenderIndicatorAdapter()
    {
    }

    /// <summary>
    /// The number of bars required before the indicator produces valid results.
    /// Subclasses define this based on their specific indicator parameters.
    /// </summary>
    protected abstract int WarmupPeriod { get; }

    /// <summary>
    /// Computes the indicator on the given windowed quote collection.
    /// Implementations call the appropriate Skender extension method.
    /// </summary>
    /// <param name="quotes">The bounded window of quotes (at most WarmupPeriod × 2).</param>
    /// <returns>The computed indicator results for the window.</returns>
    protected abstract IReadOnlyList<TResult> Compute(IReadOnlyList<Quote> quotes);

    /// <inheritdoc />
    public IReadOnlyList<TResult> Results => _results;

    /// <inheritdoc />
    public bool IsWarm => _results.Count >= WarmupPeriod;

    /// <inheritdoc />
    public void Add(BarRecord bar)
    {
        if (_quotes is null)
        {
            _capacity = WarmupPeriod * 2;
            _quotes = new Queue<Quote>(_capacity);
        }

        var quote = new Quote
        {
            Date = bar.Timestamp.UtcDateTime,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Volume = bar.Volume
        };

        if (_quotes.Count >= _capacity)
        {
            _quotes.Dequeue();
        }

        _quotes.Enqueue(quote);

        var windowed = _quotes.ToList();
        var computed = Compute(windowed);

        if (computed.Count > 0)
        {
            _results.Add(computed[^1]);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        _quotes?.Clear();
        _results.Clear();
    }
}
