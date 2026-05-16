using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using TradingResearchEngine.Core.Indicators;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Descriptor for a parameter of a Skender indicator.
/// </summary>
public sealed record SkenderParamDef(
    string Name, Type ClrType, object DefaultValue, object Min, object Max, string Description);

/// <summary>
/// Catalog entry describing a single Skender indicator with its parameters,
/// output fields, and a pre-compiled invoker factory for zero-reflection hot-path execution.
/// </summary>
public sealed record SkenderCatalogEntry(
    string Key,
    string DisplayName,
    string Description,
    string Category,
    IReadOnlyList<SkenderParamDef> Parameters,
    string PrimaryOutputField,
    IReadOnlyList<string> AllOutputFields,
    Func<IReadOnlyList<Quote>, Dictionary<string, object>, string, decimal?> Invoker,
    int WarmupPeriod = 0);

/// <summary>
/// Static catalog of 40+ Skender.Stock.Indicators with pre-compiled delegate invokers.
/// Zero reflection in the hot path — all indicator calls go through typed delegates.
/// All delegates are pre-compiled at catalog initialization time.
/// </summary>
public static class SkenderIndicatorCatalog
{
    private static readonly List<SkenderCatalogEntry> _entries = BuildCatalog();
    private static List<SkenderCatalogEntry>? _functionalEntries;
    private static bool _registered;
    private static bool _validated;

    /// <summary>All registered indicator catalog entries.</summary>
    public static IReadOnlyList<SkenderCatalogEntry> All => _entries;

    /// <summary>
    /// Only entries whose invoker produces a non-null result with default parameters
    /// and sufficient sample data. Use this property in UI components to ensure users
    /// only see functional indicators.
    /// </summary>
    public static IReadOnlyList<SkenderCatalogEntry> FunctionalEntries
    {
        get
        {
            if (_functionalEntries is null)
            {
                _functionalEntries = _entries.Where(e => IsEntryFunctional(e)).ToList();
            }
            return _functionalEntries;
        }
    }

    /// <summary>Gets a catalog entry by key, or null if not found.</summary>
    public static SkenderCatalogEntry? Get(string key) =>
        _entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Validates all catalog entries by invoking each factory with default parameters
    /// and sample data. Logs warnings for any entries that fail or return null.
    /// Should be called at application startup.
    /// </summary>
    /// <param name="logger">Logger instance for reporting validation results.</param>
    /// <returns>The list of entries that failed validation.</returns>
    public static IReadOnlyList<SkenderCatalogEntry> ValidateAll(ILogger logger)
    {
        if (_validated)
            return _entries.Where(e => !IsEntryFunctional(e)).ToList();

        _validated = true;
        var sampleQuotes = GenerateSampleQuotes(100);
        var failures = new List<SkenderCatalogEntry>();

        foreach (var entry in _entries)
        {
            var defaultParams = entry.Parameters.ToDictionary(
                p => p.Name,
                p => p.DefaultValue);

            try
            {
                var result = entry.Invoker(sampleQuotes, defaultParams, entry.PrimaryOutputField);
                if (result is null)
                {
                    logger.LogWarning(
                        "Indicator catalog validation: entry '{Key}' ({DisplayName}) returned null with default parameters and {BarCount} sample bars",
                        entry.Key, entry.DisplayName, sampleQuotes.Count);
                    failures.Add(entry);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Indicator catalog validation: entry '{Key}' ({DisplayName}) threw an exception with default parameters",
                    entry.Key, entry.DisplayName);
                failures.Add(entry);
            }
        }

        if (failures.Count == 0)
        {
            logger.LogInformation(
                "Indicator catalog validation: all {Count} entries validated successfully",
                _entries.Count);
        }
        else
        {
            logger.LogWarning(
                "Indicator catalog validation: {FailureCount} of {TotalCount} entries failed validation",
                failures.Count, _entries.Count);
        }

        // Rebuild functional entries cache after validation
        _functionalEntries = _entries.Where(e => IsEntryFunctional(e)).ToList();

        return failures;
    }

    /// <summary>
    /// Registers all catalog indicators in <see cref="IndicatorRegistry.All"/>.
    /// Safe to call multiple times — only registers once.
    /// </summary>
    public static void RegisterInIndicatorRegistry()
    {
        if (_registered) return;
        _registered = true;

        var descriptors = _entries.Select(e => new IndicatorDescriptor(
            e.Key.ToUpperInvariant(),
            e.Description,
            e.Parameters.Select(p => new IndicatorParameterDescriptor(
                p.Name, p.ClrType.Name.ToLowerInvariant(), p.Min, p.Max, p.DefaultValue)).ToArray(),
            "decimal"));

        IndicatorRegistry.Register(descriptors);
    }

    private static List<SkenderCatalogEntry> BuildCatalog() => new()
    {
        // Trend indicators
        Entry("sma", "Simple Moving Average", "Arithmetic mean of closing prices over N periods.", "Trend",
            new[] { Param("period", typeof(int), 20, 1, 500, "Lookback period") },
            "Sma", new[] { "Sma" },
            (quotes, p, field) => quotes.GetSma(GetInt(p, "period")).LastOrDefault()?.Sma is double v ? (decimal)v : null),

        Entry("ema", "Exponential Moving Average", "Exponentially weighted moving average.", "Trend",
            new[] { Param("period", typeof(int), 20, 1, 500, "Lookback period") },
            "Ema", new[] { "Ema" },
            (quotes, p, field) => quotes.GetEma(GetInt(p, "period")).LastOrDefault()?.Ema is double v ? (decimal)v : null),

        Entry("dema", "Double EMA", "Double exponential moving average for reduced lag.", "Trend",
            new[] { Param("period", typeof(int), 20, 1, 500, "Lookback period") },
            "Dema", new[] { "Dema" },
            (quotes, p, field) => quotes.GetDema(GetInt(p, "period")).LastOrDefault()?.Dema is double v ? (decimal)v : null),

        Entry("tema", "Triple EMA", "Triple exponential moving average.", "Trend",
            new[] { Param("period", typeof(int), 20, 1, 500, "Lookback period") },
            "Tema", new[] { "Tema" },
            (quotes, p, field) => quotes.GetTema(GetInt(p, "period")).LastOrDefault()?.Tema is double v ? (decimal)v : null),

        Entry("wma", "Weighted Moving Average", "Linearly weighted moving average.", "Trend",
            new[] { Param("period", typeof(int), 20, 1, 500, "Lookback period") },
            "Wma", new[] { "Wma" },
            (quotes, p, field) => quotes.GetWma(GetInt(p, "period")).LastOrDefault()?.Wma is double v ? (decimal)v : null),

        Entry("hma", "Hull Moving Average", "Hull moving average for reduced lag.", "Trend",
            new[] { Param("period", typeof(int), 20, 2, 500, "Lookback period") },
            "Hma", new[] { "Hma" },
            (quotes, p, field) => quotes.GetHma(GetInt(p, "period")).LastOrDefault()?.Hma is double v ? (decimal)v : null),

        Entry("kama", "Kaufman Adaptive MA", "Kaufman's adaptive moving average.", "Trend",
            new[] { Param("erPeriod", typeof(int), 10, 1, 200, "Efficiency ratio period"),
                    Param("fastPeriod", typeof(int), 2, 1, 50, "Fast SC period"),
                    Param("slowPeriod", typeof(int), 30, 5, 200, "Slow SC period") },
            "Kama", new[] { "Kama" },
            (quotes, p, field) => quotes.GetKama(GetInt(p, "erPeriod"), GetInt(p, "fastPeriod"), GetInt(p, "slowPeriod")).LastOrDefault()?.Kama is double v ? (decimal)v : null),

        Entry("t3", "T3 Moving Average", "Tillson T3 triple-smoothed EMA.", "Trend",
            new[] { Param("period", typeof(int), 5, 1, 200, "Lookback period"),
                    Param("volumeFactor", typeof(double), 0.7, 0.0, 1.0, "Volume factor") },
            "T3", new[] { "T3" },
            (quotes, p, field) => quotes.GetT3(GetInt(p, "period"), GetDouble(p, "volumeFactor")).LastOrDefault()?.T3 is double v ? (decimal)v : null),

        Entry("supertrend", "SuperTrend", "Trend-following overlay based on ATR.", "Trend",
            new[] { Param("period", typeof(int), 10, 1, 100, "ATR period"),
                    Param("multiplier", typeof(double), 3.0, 0.5, 10.0, "ATR multiplier") },
            "SuperTrend", new[] { "SuperTrend", "UpperBand", "LowerBand" },
            (quotes, p, field) => quotes.GetSuperTrend(GetInt(p, "period"), GetDouble(p, "multiplier")).LastOrDefault()?.SuperTrend is decimal v ? v : null),

        // Momentum indicators
        Entry("rsi", "Relative Strength Index", "Momentum oscillator measuring speed of price changes.", "Momentum",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period") },
            "Rsi", new[] { "Rsi" },
            (quotes, p, field) => quotes.GetRsi(GetInt(p, "period")).LastOrDefault()?.Rsi is double v ? (decimal)v : null),

        Entry("macd", "MACD", "Moving Average Convergence Divergence.", "Momentum",
            new[] { Param("fastPeriod", typeof(int), 12, 1, 100, "Fast EMA period"),
                    Param("slowPeriod", typeof(int), 26, 1, 200, "Slow EMA period"),
                    Param("signalPeriod", typeof(int), 9, 1, 50, "Signal line period") },
            "Macd", new[] { "Macd", "Signal", "Histogram" },
            (quotes, p, field) => {
                var r = quotes.GetMacd(GetInt(p, "fastPeriod"), GetInt(p, "slowPeriod"), GetInt(p, "signalPeriod")).LastOrDefault();
                if (r is null) return null;
                return field switch { "Signal" => r.Signal is double s ? (decimal)s : null,
                                      "Histogram" => r.Histogram is double h ? (decimal)h : null,
                                      _ => r.Macd is double m ? (decimal)m : null };
            }),

        Entry("stochastic", "Stochastic Oscillator", "Compares closing price to price range.", "Momentum",
            new[] { Param("kPeriod", typeof(int), 14, 1, 100, "%K period"),
                    Param("dPeriod", typeof(int), 3, 1, 50, "%D smoothing"),
                    Param("jPeriod", typeof(int), 3, 1, 50, "Signal smoothing") },
            "K", new[] { "K", "D", "J" },
            (quotes, p, field) => {
                var r = quotes.GetStoch(GetInt(p, "kPeriod"), GetInt(p, "dPeriod"), GetInt(p, "jPeriod")).LastOrDefault();
                if (r is null) return null;
                return field switch { "D" => r.D is double d ? (decimal)d : null,
                                      "J" => r.J is double j ? (decimal)j : null,
                                      _ => r.K is double k ? (decimal)k : null };
            }),

        Entry("cci", "Commodity Channel Index", "Measures deviation from statistical mean.", "Momentum",
            new[] { Param("period", typeof(int), 20, 1, 200, "Lookback period") },
            "Cci", new[] { "Cci" },
            (quotes, p, field) => quotes.GetCci(GetInt(p, "period")).LastOrDefault()?.Cci is double v ? (decimal)v : null),

        Entry("williams", "Williams %R", "Momentum oscillator similar to stochastic.", "Momentum",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period") },
            "WilliamsR", new[] { "WilliamsR" },
            (quotes, p, field) => quotes.GetWilliamsR(GetInt(p, "period")).LastOrDefault()?.WilliamsR is double v ? (decimal)v : null),

        Entry("mfi", "Money Flow Index", "Volume-weighted RSI.", "Momentum",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period") },
            "Mfi", new[] { "Mfi" },
            (quotes, p, field) => quotes.GetMfi(GetInt(p, "period")).LastOrDefault()?.Mfi is double v ? (decimal)v : null),

        Entry("roc", "Rate of Change", "Percentage change over N periods.", "Momentum",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period") },
            "Roc", new[] { "Roc" },
            (quotes, p, field) => quotes.GetRoc(GetInt(p, "period")).LastOrDefault()?.Roc is double v ? (decimal)v : null),

        Entry("pmo", "Price Momentum Oscillator", "Double-smoothed ROC.", "Momentum",
            new[] { Param("timePeriod", typeof(int), 35, 1, 200, "Time period"),
                    Param("smoothPeriod", typeof(int), 20, 1, 100, "Smoothing period"),
                    Param("signalPeriod", typeof(int), 10, 1, 50, "Signal period") },
            "Pmo", new[] { "Pmo", "Signal" },
            (quotes, p, field) => {
                var r = quotes.GetPmo(GetInt(p, "timePeriod"), GetInt(p, "smoothPeriod"), GetInt(p, "signalPeriod")).LastOrDefault();
                return field == "Signal" ? (r?.Signal is double s ? (decimal)s : null) : (r?.Pmo is double m ? (decimal)m : null);
            }),

        // Volatility indicators
        Entry("atr", "Average True Range", "Measures market volatility.", "Volatility",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period") },
            "Atr", new[] { "Atr" },
            (quotes, p, field) => quotes.GetAtr(GetInt(p, "period")).LastOrDefault()?.Atr is double v ? (decimal)v : null),

        Entry("bollinger", "Bollinger Bands", "Volatility bands around a moving average.", "Volatility",
            new[] { Param("period", typeof(int), 20, 1, 200, "Lookback period"),
                    Param("stdDev", typeof(double), 2.0, 0.5, 5.0, "Standard deviation multiplier") },
            "Sma", new[] { "Sma", "UpperBand", "LowerBand", "Width", "PercentB" },
            (quotes, p, field) => {
                var r = quotes.GetBollingerBands(GetInt(p, "period"), GetDouble(p, "stdDev")).LastOrDefault();
                if (r is null) return null;
                return field switch { "UpperBand" => r.UpperBand is double u ? (decimal)u : null,
                                      "LowerBand" => r.LowerBand is double l ? (decimal)l : null,
                                      "Width" => r.Width is double w ? (decimal)w : null,
                                      "PercentB" => r.PercentB is double pb ? (decimal)pb : null,
                                      _ => r.Sma is double s ? (decimal)s : null };
            }),

        Entry("keltner", "Keltner Channels", "Volatility channels based on ATR.", "Volatility",
            new[] { Param("emaPeriod", typeof(int), 20, 1, 200, "EMA period"),
                    Param("multiplier", typeof(double), 2.0, 0.5, 5.0, "ATR multiplier"),
                    Param("atrPeriod", typeof(int), 10, 1, 100, "ATR period") },
            "Centerline", new[] { "Centerline", "UpperBand", "LowerBand", "Width" },
            (quotes, p, field) => {
                var r = quotes.GetKeltner(GetInt(p, "emaPeriod"), GetDouble(p, "multiplier"), GetInt(p, "atrPeriod")).LastOrDefault();
                if (r is null) return null;
                return field switch { "UpperBand" => r.UpperBand is double u ? (decimal)u : null,
                                      "LowerBand" => r.LowerBand is double l ? (decimal)l : null,
                                      "Width" => r.Width is double w ? (decimal)w : null,
                                      _ => r.Centerline is double c ? (decimal)c : null };
            }),

        Entry("donchian", "Donchian Channels", "Highest high / lowest low over N periods.", "Volatility",
            new[] { Param("period", typeof(int), 20, 1, 200, "Lookback period") },
            "Centerline", new[] { "Centerline", "UpperBand", "LowerBand", "Width" },
            (quotes, p, field) => {
                var r = quotes.GetDonchian(GetInt(p, "period")).LastOrDefault();
                if (r is null) return null;
                return field switch { "UpperBand" => r.UpperBand is decimal u ? u : null,
                                      "LowerBand" => r.LowerBand is decimal l ? l : null,
                                      "Width" => r.Width is decimal w ? w : null,
                                      _ => r.Centerline is decimal c ? c : null };
            }),

        Entry("stddev", "Standard Deviation", "Statistical volatility measure.", "Volatility",
            new[] { Param("period", typeof(int), 20, 1, 200, "Lookback period") },
            "StdDev", new[] { "StdDev" },
            (quotes, p, field) => quotes.GetStdDev(GetInt(p, "period")).LastOrDefault()?.StdDev is double v ? (decimal)v : null),

        // Volume indicators
        Entry("adl", "Accumulation/Distribution Line", "Volume-based trend indicator.", "Volume",
            Array.Empty<SkenderParamDef>(),
            "Adl", new[] { "Adl" },
            (quotes, p, field) => quotes.GetAdl().LastOrDefault()?.Adl is double v ? (decimal)v : null),

        Entry("obv", "On-Balance Volume", "Cumulative volume flow.", "Volume",
            Array.Empty<SkenderParamDef>(),
            "Obv", new[] { "Obv" },
            (quotes, p, field) => quotes.GetObv().LastOrDefault()?.Obv is double v ? (decimal)v : null),

        Entry("vwap", "VWAP", "Volume-weighted average price.", "Volume",
            Array.Empty<SkenderParamDef>(),
            "Vwap", new[] { "Vwap" },
            (quotes, p, field) => quotes.GetVwap().LastOrDefault()?.Vwap is double v ? (decimal)v : null),

        Entry("cmf", "Chaikin Money Flow", "Volume-weighted accumulation/distribution.", "Volume",
            new[] { Param("period", typeof(int), 20, 1, 200, "Lookback period") },
            "Cmf", new[] { "Cmf" },
            (quotes, p, field) => quotes.GetCmf(GetInt(p, "period")).LastOrDefault()?.Cmf is double v ? (decimal)v : null),

        // Trend strength
        Entry("adx", "Average Directional Index", "Measures trend strength.", "Trend Strength",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period") },
            "Adx", new[] { "Adx", "Pdi", "Mdi" },
            (quotes, p, field) => {
                var r = quotes.GetAdx(GetInt(p, "period")).LastOrDefault();
                if (r is null) return null;
                return field switch { "Pdi" => r.Pdi is double pdi ? (decimal)pdi : null,
                                      "Mdi" => r.Mdi is double mdi ? (decimal)mdi : null,
                                      _ => r.Adx is double adx ? (decimal)adx : null };
            }),

        Entry("aroon", "Aroon", "Identifies trend changes and strength.", "Trend Strength",
            new[] { Param("period", typeof(int), 25, 1, 200, "Lookback period") },
            "Oscillator", new[] { "Oscillator", "AroonUp", "AroonDown" },
            (quotes, p, field) => {
                var r = quotes.GetAroon(GetInt(p, "period")).LastOrDefault();
                if (r is null) return null;
                return field switch { "AroonUp" => r.AroonUp is double u ? (decimal)u : null,
                                      "AroonDown" => r.AroonDown is double d ? (decimal)d : null,
                                      _ => r.Oscillator is double o ? (decimal)o : null };
            }),

        // Oscillators
        Entry("awesome", "Awesome Oscillator", "Difference between 5 and 34 period SMA of midpoints.", "Oscillator",
            Array.Empty<SkenderParamDef>(),
            "Oscillator", new[] { "Oscillator" },
            (quotes, p, field) => quotes.GetAwesome().LastOrDefault()?.Oscillator is double v ? (decimal)v : null),

        Entry("trix", "TRIX", "Triple-smoothed EMA rate of change.", "Oscillator",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period"),
                    Param("signalPeriod", typeof(int), 9, 1, 50, "Signal period") },
            "Trix", new[] { "Trix", "Signal" },
            (quotes, p, field) => {
                var r = quotes.GetTrix(GetInt(p, "period"), GetInt(p, "signalPeriod")).LastOrDefault();
                return field == "Signal" ? (r?.Signal is double s ? (decimal)s : null) : (r?.Trix is double t ? (decimal)t : null);
            }),

        Entry("ultimate", "Ultimate Oscillator", "Multi-timeframe momentum oscillator.", "Oscillator",
            new[] { Param("short", typeof(int), 7, 1, 50, "Short period"),
                    Param("mid", typeof(int), 14, 1, 100, "Medium period"),
                    Param("long", typeof(int), 28, 1, 200, "Long period") },
            "Ultimate", new[] { "Ultimate" },
            (quotes, p, field) => quotes.GetUltimate(GetInt(p, "short"), GetInt(p, "mid"), GetInt(p, "long")).LastOrDefault()?.Ultimate is double v ? (decimal)v : null),

        // Pivot / Support-Resistance
        Entry("pivotpoints", "Pivot Points", "Standard pivot point levels.", "Support/Resistance",
            Array.Empty<SkenderParamDef>(),
            "PP", new[] { "PP", "S1", "S2", "R1", "R2" },
            (quotes, p, field) => {
                var r = quotes.GetPivotPoints(Skender.Stock.Indicators.PeriodSize.Day).LastOrDefault();
                if (r is null) return null;
                return field switch { "S1" => r.S1 is decimal s1 ? s1 : null,
                                      "S2" => r.S2 is decimal s2 ? s2 : null,
                                      "R1" => r.R1 is decimal r1 ? r1 : null,
                                      "R2" => r.R2 is decimal r2 ? r2 : null,
                                      _ => r.PP is decimal pp ? pp : null };
            }),

        // Statistical
        Entry("slope", "Slope (Linear Regression)", "Linear regression slope.", "Statistical",
            new[] { Param("period", typeof(int), 20, 2, 500, "Lookback period") },
            "Slope", new[] { "Slope", "Intercept", "RSquared" },
            (quotes, p, field) => {
                var r = quotes.GetSlope(GetInt(p, "period")).LastOrDefault();
                if (r is null) return null;
                return field switch { "Intercept" => r.Intercept is double i ? (decimal)i : null,
                                      "RSquared" => r.RSquared is double rs ? (decimal)rs : null,
                                      _ => r.Slope is double s ? (decimal)s : null };
            }),

        // Ichimoku
        Entry("ichimoku", "Ichimoku Cloud", "Multi-component trend system.", "Trend",
            new[] { Param("tenkanPeriod", typeof(int), 9, 1, 100, "Tenkan-sen period"),
                    Param("kijunPeriod", typeof(int), 26, 1, 200, "Kijun-sen period"),
                    Param("senkouPeriod", typeof(int), 52, 1, 500, "Senkou Span B period") },
            "TenkanSen", new[] { "TenkanSen", "KijunSen", "SenkouSpanA", "SenkouSpanB", "ChikouSpan" },
            (quotes, p, field) => {
                var r = quotes.GetIchimoku(GetInt(p, "tenkanPeriod"), GetInt(p, "kijunPeriod"), GetInt(p, "senkouPeriod")).LastOrDefault();
                if (r is null) return null;
                return field switch { "KijunSen" => r.KijunSen,
                                      "SenkouSpanA" => r.SenkouSpanA,
                                      "SenkouSpanB" => r.SenkouSpanB,
                                      "ChikouSpan" => r.ChikouSpan,
                                      _ => r.TenkanSen };
            }),

        // Parabolic SAR
        Entry("psar", "Parabolic SAR", "Trailing stop and reversal indicator.", "Trend",
            new[] { Param("step", typeof(double), 0.02, 0.001, 0.1, "Acceleration step"),
                    Param("maxFactor", typeof(double), 0.2, 0.05, 1.0, "Maximum acceleration") },
            "Sar", new[] { "Sar" },
            (quotes, p, field) => quotes.GetParabolicSar(GetDouble(p, "step"), GetDouble(p, "maxFactor")).LastOrDefault()?.Sar is double v ? (decimal)v : null),

        // Choppiness
        Entry("chop", "Choppiness Index", "Measures market choppiness vs trending.", "Trend Strength",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period") },
            "Chop", new[] { "Chop" },
            (quotes, p, field) => quotes.GetChop(GetInt(p, "period")).LastOrDefault()?.Chop is double v ? (decimal)v : null),

        // Elder Force Index
        Entry("elderray", "Elder-Ray Index", "Bull and bear power indicators.", "Momentum",
            new[] { Param("period", typeof(int), 13, 1, 200, "EMA period") },
            "BullPower", new[] { "BullPower", "BearPower" },
            (quotes, p, field) => {
                var r = quotes.GetElderRay(GetInt(p, "period")).LastOrDefault();
                if (r is null) return null;
                return field == "BearPower" ? (r.BearPower is double b ? (decimal)b : null) : (r.BullPower is double bp ? (decimal)bp : null);
            }),

        // Connors RSI
        Entry("connorsrsi", "ConnorsRSI", "Composite RSI with streak and percentile rank.", "Momentum",
            new[] { Param("rsiPeriod", typeof(int), 3, 1, 50, "RSI period"),
                    Param("streakPeriod", typeof(int), 2, 1, 50, "Streak RSI period"),
                    Param("rankPeriod", typeof(int), 100, 10, 500, "Percentile rank period") },
            "ConnorsRsi", new[] { "ConnorsRsi" },
            (quotes, p, field) => quotes.GetConnorsRsi(GetInt(p, "rsiPeriod"), GetInt(p, "streakPeriod"), GetInt(p, "rankPeriod")).LastOrDefault()?.ConnorsRsi is double v ? (decimal)v : null),

        // Additional indicators to reach 40+

        Entry("stochrsi", "Stochastic RSI", "Stochastic oscillator applied to RSI values.", "Momentum",
            new[] { Param("rsiPeriod", typeof(int), 14, 1, 100, "RSI period"),
                    Param("stochPeriod", typeof(int), 14, 1, 100, "Stochastic period"),
                    Param("signalPeriod", typeof(int), 3, 1, 50, "Signal smoothing"),
                    Param("smoothPeriod", typeof(int), 3, 1, 50, "K smoothing") },
            "StochRsi", new[] { "StochRsi", "Signal" },
            (quotes, p, field) => {
                var r = quotes.GetStochRsi(GetInt(p, "rsiPeriod"), GetInt(p, "stochPeriod"), GetInt(p, "signalPeriod"), GetInt(p, "smoothPeriod")).LastOrDefault();
                if (r is null) return null;
                return field == "Signal" ? (r.Signal is double s ? (decimal)s : null) : (r.StochRsi is double k ? (decimal)k : null);
            }),

        Entry("vortex", "Vortex Indicator", "Identifies trend direction and strength using positive/negative movement.", "Trend Strength",
            new[] { Param("period", typeof(int), 14, 1, 200, "Lookback period") },
            "Pvi", new[] { "Pvi", "Nvi" },
            (quotes, p, field) => {
                var r = quotes.GetVortex(GetInt(p, "period")).LastOrDefault();
                if (r is null) return null;
                return field == "Nvi" ? (r.Nvi is double n ? (decimal)n : null) : (r.Pvi is double pv ? (decimal)pv : null);
            }),

        Entry("smma", "Smoothed Moving Average", "Smoothed (modified) moving average with reduced noise.", "Trend",
            new[] { Param("period", typeof(int), 20, 1, 500, "Lookback period") },
            "Smma", new[] { "Smma" },
            (quotes, p, field) => quotes.GetSmma(GetInt(p, "period")).LastOrDefault()?.Smma is double v ? (decimal)v : null),

        Entry("epma", "Endpoint Moving Average", "Endpoint moving average using linear regression.", "Trend",
            new[] { Param("period", typeof(int), 14, 1, 500, "Lookback period") },
            "Epma", new[] { "Epma" },
            (quotes, p, field) => quotes.GetEpma(GetInt(p, "period")).LastOrDefault()?.Epma is double v ? (decimal)v : null),

        Entry("mama", "MESA Adaptive MA", "MESA Adaptive Moving Average by John Ehlers.", "Trend",
            new[] { Param("fastLimit", typeof(double), 0.5, 0.01, 1.0, "Fast limit"),
                    Param("slowLimit", typeof(double), 0.05, 0.01, 0.5, "Slow limit") },
            "Mama", new[] { "Mama", "Fama" },
            (quotes, p, field) => {
                var r = quotes.GetMama(GetDouble(p, "fastLimit"), GetDouble(p, "slowLimit")).LastOrDefault();
                if (r is null) return null;
                return field == "Fama" ? (r.Fama is double f ? (decimal)f : null) : (r.Mama is double m ? (decimal)m : null);
            }),

        Entry("fisher", "Fisher Transform", "Normalizes prices into a Gaussian distribution for clearer turning points.", "Momentum",
            new[] { Param("period", typeof(int), 10, 1, 200, "Lookback period") },
            "Fisher", new[] { "Fisher", "Trigger" },
            (quotes, p, field) => {
                var r = quotes.GetFisherTransform(GetInt(p, "period")).LastOrDefault();
                if (r is null) return null;
                return field == "Trigger" ? (r.Trigger is double t ? (decimal)t : null) : (r.Fisher is double f ? (decimal)f : null);
            }),
    };

    private static SkenderCatalogEntry Entry(
        string key, string displayName, string description, string category,
        SkenderParamDef[] parameters, string primaryOutput, string[] allOutputs,
        Func<IReadOnlyList<Quote>, Dictionary<string, object>, string, decimal?> invoker,
        int warmup = 0) =>
        new(key, displayName, description, category, parameters, primaryOutput, allOutputs, invoker, warmup);

    private static SkenderParamDef Param(string name, Type type, object defaultValue, object min, object max, string desc) =>
        new(name, type, defaultValue, min, max, desc);

    private static int GetInt(Dictionary<string, object> p, string key) =>
        p.TryGetValue(key, out var v) ? Convert.ToInt32(v) : 14;

    private static double GetDouble(Dictionary<string, object> p, string key) =>
        p.TryGetValue(key, out var v) ? Convert.ToDouble(v) : 0.0;

    /// <summary>
    /// Tests whether a catalog entry produces a non-null result with default parameters
    /// and sufficient sample data.
    /// </summary>
    private static bool IsEntryFunctional(SkenderCatalogEntry entry)
    {
        var sampleQuotes = GenerateSampleQuotes(100);
        var defaultParams = entry.Parameters.ToDictionary(
            p => p.Name,
            p => p.DefaultValue);

        try
        {
            var result = entry.Invoker(sampleQuotes, defaultParams, entry.PrimaryOutputField);
            return result is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Generates synthetic OHLCV quote data for validation purposes.
    /// Produces a simple uptrending series with realistic price relationships.
    /// </summary>
    private static IReadOnlyList<Quote> GenerateSampleQuotes(int count)
    {
        var quotes = new List<Quote>(count);
        var basePrice = 100m;
        var date = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int i = 0; i < count; i++)
        {
            var open = basePrice + (i * 0.5m);
            var close = open + (i % 3 == 0 ? -0.3m : 0.4m);
            var high = Math.Max(open, close) + 0.5m;
            var low = Math.Min(open, close) - 0.5m;
            var volume = 1000000m + (i * 10000m);

            quotes.Add(new Quote
            {
                Date = date.AddDays(i),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }

        return quotes;
    }
}
