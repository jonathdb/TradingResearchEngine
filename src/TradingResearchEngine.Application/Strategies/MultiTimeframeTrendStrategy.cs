using TradingResearchEngine.Application.Indicators;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Multi-timeframe trend-following strategy that uses a higher-timeframe SMA to
/// determine trend direction and a lower-timeframe (primary) SMA crossover for
/// entry timing.
/// </summary>
/// <remarks>
/// <para>
/// Higher timeframe (e.g. Daily): A long-period SMA determines the prevailing trend.
/// When price is above the daily SMA, the trend is bullish; below, bearish.
/// </para>
/// <para>
/// Primary timeframe (e.g. H1): A fast/slow SMA crossover generates entry signals,
/// but only in the direction confirmed by the higher-timeframe trend filter.
/// This reduces whipsaw entries during counter-trend moves.
/// </para>
/// <para>
/// Hypothesis: Filtering lower-timeframe crossover signals by higher-timeframe trend
/// direction improves win rate and reduces drawdown compared to unfiltered crossovers.
/// </para>
/// </remarks>
[StrategyName("multi-timeframe-trend")]
public sealed class MultiTimeframeTrendStrategy : IMultiTimeframeStrategy
{
    private readonly int _htfSmaPeriod;
    private readonly int _primaryFastPeriod;
    private readonly int _primarySlowPeriod;
    private readonly string _htfTimeframe;
    private readonly DirectionMode _directionMode;

    private readonly SmaIndicator _htfSma;
    private readonly SmaIndicator _primaryFastSma;
    private readonly SmaIndicator _primarySlowSma;

    private Direction _position = Direction.Flat;
    private decimal? _lastHtfSmaValue;
    private decimal? _lastHtfClose;
    private int _barCount;

    /// <summary>Creates a multi-timeframe trend strategy.</summary>
    /// <param name="htfSmaPeriod">Higher-timeframe SMA period for trend determination (default 20).</param>
    /// <param name="primaryFastPeriod">Primary timeframe fast SMA period (default 5).</param>
    /// <param name="primarySlowPeriod">Primary timeframe slow SMA period (default 15).</param>
    /// <param name="htfTimeframe">The timeframe label for the higher-timeframe data source (default "D1").</param>
    /// <param name="directionMode">Signal direction mode: Long, Short, or Both (default Long).</param>
    public MultiTimeframeTrendStrategy(
        [ParameterMeta(DisplayName = "HTF SMA Period", Description = "Higher-timeframe SMA lookback period for trend filter.",
            SensitivityHint = SensitivityHint.Medium, Group = "Signal", DisplayOrder = 0, Min = 5)]
        int htfSmaPeriod = 20,
        [ParameterMeta(DisplayName = "Fast Period", Description = "Primary timeframe fast SMA period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 1, Min = 2)]
        int primaryFastPeriod = 5,
        [ParameterMeta(DisplayName = "Slow Period", Description = "Primary timeframe slow SMA period.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 2, Min = 5)]
        int primarySlowPeriod = 15,
        [ParameterMeta(DisplayName = "HTF Timeframe", Description = "Timeframe label for the higher-timeframe data source.",
            Group = "Filters", DisplayOrder = 3)]
        string htfTimeframe = "D1",
        [ParameterMeta(DisplayName = "Direction", Description = "Signal direction mode.",
            Group = "Signal", DisplayOrder = 4)]
        DirectionMode directionMode = DirectionMode.Long)
    {
        _htfSmaPeriod = htfSmaPeriod;
        _primaryFastPeriod = primaryFastPeriod;
        _primarySlowPeriod = primarySlowPeriod;
        _htfTimeframe = htfTimeframe;
        _directionMode = directionMode;

        _htfSma = new SmaIndicator(htfSmaPeriod);
        _primaryFastSma = new SmaIndicator(primaryFastPeriod);
        _primarySlowSma = new SmaIndicator(primarySlowPeriod);
    }

    /// <inheritdoc/>
    public void Initialize(StrategyConfig config)
    {
        // Parameters are set via constructor; Initialize is a lifecycle hook.
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _htfSma.Reset();
        _primaryFastSma.Reset();
        _primarySlowSma.Reset();
        _position = Direction.Flat;
        _lastHtfSmaValue = null;
        _lastHtfClose = null;
        _barCount = 0;
    }

    /// <inheritdoc/>
    public void OnSecondaryBar(string timeframe, BarRecord bar)
    {
        if (!string.Equals(timeframe, _htfTimeframe, StringComparison.OrdinalIgnoreCase))
            return;

        _htfSma.Add(bar);
        _lastHtfClose = bar.Close;

        if (_htfSma.IsWarm)
        {
            var latestResult = _htfSma.Results[^1];
            _lastHtfSmaValue = latestResult.Sma is not null ? (decimal)latestResult.Sma.Value : null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        var barRecord = new BarRecord(
            bar.Symbol, bar.Interval, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume, bar.Timestamp);

        _primaryFastSma.Add(barRecord);
        _primarySlowSma.Add(barRecord);
        _barCount++;

        // Need primary SMAs warmed up and higher-timeframe trend established
        if (!_primaryFastSma.IsWarm || !_primarySlowSma.IsWarm)
            return Array.Empty<EngineEvent>();

        if (_lastHtfSmaValue is null || _lastHtfClose is null)
            return Array.Empty<EngineEvent>();

        var fastResult = _primaryFastSma.Results[^1];
        var slowResult = _primarySlowSma.Results[^1];

        if (fastResult.Sma is null || slowResult.Sma is null)
            return Array.Empty<EngineEvent>();

        decimal fastValue = (decimal)fastResult.Sma.Value;
        decimal slowValue = (decimal)slowResult.Sma.Value;

        // Determine higher-timeframe trend direction
        bool htfBullish = _lastHtfClose.Value > _lastHtfSmaValue.Value;
        bool htfBearish = _lastHtfClose.Value < _lastHtfSmaValue.Value;

        // Long entry: primary fast crosses above slow AND higher-timeframe trend is bullish
        if (fastValue > slowValue && _position != Direction.Long && htfBullish
            && _directionMode is DirectionMode.Long or DirectionMode.Both)
        {
            _position = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }

        // Exit long: primary fast crosses below slow (exit regardless of HTF trend)
        if (fastValue <= slowValue && _position == Direction.Long)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        // Short entry: primary fast crosses below slow AND higher-timeframe trend is bearish
        if (fastValue < slowValue && _position != Direction.Short && htfBearish
            && _directionMode is DirectionMode.Short or DirectionMode.Both)
        {
            _position = Direction.Short;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Short, bar.Close, bar.Timestamp)
            };
        }

        // Exit short: primary fast crosses above slow (exit regardless of HTF trend)
        if (fastValue >= slowValue && _position == Direction.Short)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        return Array.Empty<EngineEvent>();
    }
}
