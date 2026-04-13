using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.Application.Strategies;

/// <summary>
/// Stationary Mean Reversion strategy.
/// 
/// Exploits temporary price dislocations in instruments whose return series
/// exhibits statistically stationary behavior.
/// 
/// Each bar:
/// 1. Computes returns over the lookback window
/// 2. Tests for stationarity using a simplified Augmented Dickey-Fuller test
/// 3. If stationary, computes z-score of the latest return
/// 4. Buys when z-score &lt; -entryThreshold (abnormal negative = expect reversion)
/// 5. Sells when z-score &gt; exitThreshold (reversion complete)
/// 
/// Long-only. Based on the QuantConnect stationary z-score approach.
/// </summary>
[StrategyName("stationary-mean-reversion")]
public sealed class StationaryMeanReversionStrategy : IStrategy
{
    private readonly int _lookback;
    private readonly decimal _entryThreshold;
    private readonly decimal _exitThreshold;
    private readonly decimal _adfPValueThreshold;
    private readonly int _adfRecheckInterval;
    private readonly List<decimal> _closes = new();
    private readonly bool _skipStationarityTest;
    private int _barsSinceAdfCheck;
    private bool _cachedStationarity;
    private bool _adfWarmedUp;
    private Direction _position = Direction.Flat;

    /// <param name="lookback">Return lookback window (default 500).</param>
    /// <param name="entryThreshold">Z-score entry threshold, negative (default 1.0 → buy when z &lt; -1).</param>
    /// <param name="exitThreshold">Z-score exit threshold, positive (default 1.0 → sell when z &gt; 1).</param>
    /// <param name="adfPValueThreshold">ADF p-value threshold for stationarity (default 0.05).</param>
    /// <param name="skipStationarityTest">If true, skip ADF test and use z-score only (default false).</param>
    /// <param name="adfRecheckInterval">Re-run ADF test every N bars (default 20). Reduces ADF computation by ~95%.</param>
    public StationaryMeanReversionStrategy(
        [ParameterMeta(DisplayName = "Lookback", Description = "Rolling window for SMA, StdDev, and ADF test.",
            SensitivityHint = SensitivityHint.High, Group = "Signal", DisplayOrder = 0, Min = 50)]
        int lookback = 500,
        [ParameterMeta(DisplayName = "Entry Threshold", Description = "Z-score entry threshold for mean reversion.",
            SensitivityHint = SensitivityHint.High, Group = "Entry", DisplayOrder = 1, Min = 0.5)]
        decimal entryThreshold = 1.0m,
        [ParameterMeta(DisplayName = "Exit Threshold", Description = "Z-score exit threshold.",
            SensitivityHint = SensitivityHint.Medium, Group = "Exit", DisplayOrder = 2)]
        decimal exitThreshold = 1.0m,
        [ParameterMeta(DisplayName = "ADF P-Value Threshold", Description = "ADF p-value threshold for stationarity.",
            SensitivityHint = SensitivityHint.Medium, Group = "Filters", IsAdvanced = true, DisplayOrder = 3)]
        decimal adfPValueThreshold = 0.05m,
        [ParameterMeta(DisplayName = "Skip Stationarity Test", Description = "If true, skip ADF test and use z-score only.",
            SensitivityHint = SensitivityHint.Low, Group = "Filters", IsAdvanced = true, DisplayOrder = 4)]
        bool skipStationarityTest = false,
        [ParameterMeta(DisplayName = "ADF Recheck Interval", Description = "Re-run ADF test every N bars. Reduces computation.",
            SensitivityHint = SensitivityHint.Low, Group = "Filters", IsAdvanced = true, DisplayOrder = 5, Min = 1)]
        int adfRecheckInterval = 20)
    {
        _lookback = lookback;
        _entryThreshold = entryThreshold;
        _exitThreshold = exitThreshold;
        _adfPValueThreshold = adfPValueThreshold;
        _skipStationarityTest = skipStationarityTest;
        _adfRecheckInterval = adfRecheckInterval;
    }

    /// <inheritdoc/>
    public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
    {
        if (evt is not BarEvent bar) return Array.Empty<EngineEvent>();

        _closes.Add(bar.Close);
        if (_closes.Count < _lookback + 1) return Array.Empty<EngineEvent>();

        // Compute returns over lookback window
        var returns = ComputeReturns(_closes, _lookback);

        // Test stationarity with caching — only re-run every _adfRecheckInterval bars
        if (!_skipStationarityTest)
        {
            _barsSinceAdfCheck++;
            if (_barsSinceAdfCheck >= _adfRecheckInterval || !_adfWarmedUp)
            {
                _cachedStationarity = IsStationary(returns, (double)_adfPValueThreshold);
                _barsSinceAdfCheck = 0;
                _adfWarmedUp = true;
            }
        }
        bool isStationary = _skipStationarityTest || _cachedStationarity;

        if (!isStationary)
        {
            // Non-stationary: if we have a position, close it (regime changed)
            if (_position == Direction.Long)
            {
                _position = Direction.Flat;
                return new EngineEvent[]
                {
                    new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
                };
            }
            return Array.Empty<EngineEvent>();
        }

        // Compute z-score of latest return
        decimal mean = returns.Average();
        decimal stdDev = StdDev(returns);
        if (stdDev == 0m) return Array.Empty<EngineEvent>();

        decimal latestReturn = returns[^1];
        decimal zScore = (latestReturn - mean) / stdDev;

        // Entry: z-score < -threshold → buy (abnormally negative, expect reversion up)
        if (zScore < -_entryThreshold && _position != Direction.Long)
        {
            _position = Direction.Long;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Long, bar.Close, bar.Timestamp)
            };
        }

        // Exit: z-score > +threshold → sell (reversion complete)
        if (zScore > _exitThreshold && _position == Direction.Long)
        {
            _position = Direction.Flat;
            return new EngineEvent[]
            {
                new SignalEvent(bar.Symbol, Direction.Flat, bar.Close, bar.Timestamp)
            };
        }

        return Array.Empty<EngineEvent>();
    }

    /// <summary>Computes percentage returns from close prices.</summary>
    private static List<decimal> ComputeReturns(List<decimal> closes, int lookback)
    {
        var returns = new List<decimal>(lookback);
        int start = closes.Count - lookback;
        for (int i = start; i < closes.Count; i++)
        {
            if (closes[i - 1] != 0)
                returns.Add((closes[i] - closes[i - 1]) / closes[i - 1]);
        }
        return returns;
    }

    /// <summary>
    /// Simplified Augmented Dickey-Fuller stationarity test.
    /// Tests H0: unit root exists (non-stationary) vs H1: stationary.
    /// Returns true if the series appears stationary (reject H0).
    /// 
    /// Implementation: computes the t-statistic of the coefficient on y(t-1)
    /// in the regression: Δy(t) = α + β*y(t-1) + ε(t)
    /// Compares against critical values for the ADF distribution.
    /// </summary>
    private static bool IsStationary(List<decimal> series, double pValueThreshold)
    {
        if (series.Count < 20) return false;

        int n = series.Count;
        // Compute first differences: Δy(t) = y(t) - y(t-1)
        var dy = new double[n - 1];
        var yLag = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            dy[i] = (double)(series[i + 1] - series[i]);
            yLag[i] = (double)series[i];
        }

        // OLS regression: Δy = α + β * y_lag + ε
        // β = Cov(Δy, y_lag) / Var(y_lag)
        int m = dy.Length;
        double sumDy = 0, sumYl = 0, sumDyYl = 0, sumYl2 = 0;
        for (int i = 0; i < m; i++)
        {
            sumDy += dy[i];
            sumYl += yLag[i];
            sumDyYl += dy[i] * yLag[i];
            sumYl2 += yLag[i] * yLag[i];
        }

        double meanDy = sumDy / m;
        double meanYl = sumYl / m;
        double cov = sumDyYl / m - meanDy * meanYl;
        // Unbiased sample variance (Bessel's correction)
        double varYl = (sumYl2 - m * meanYl * meanYl) / (m - 1);

        if (varYl == 0) return false;

        double beta = cov / varYl;
        double alpha = meanDy - beta * meanYl;

        // Compute residuals and standard error of beta
        double ssResidual = 0;
        for (int i = 0; i < m; i++)
        {
            double predicted = alpha + beta * yLag[i];
            double residual = dy[i] - predicted;
            ssResidual += residual * residual;
        }

        double sigmaSquared = ssResidual / (m - 2);
        double seBeta = Math.Sqrt(sigmaSquared / (varYl * m));

        if (seBeta == 0) return false;

        // t-statistic for beta
        double tStat = beta / seBeta;

        // ADF critical values (approximate, for n > 100):
        // 1% : -3.43, 5% : -2.86, 10% : -2.57
        // We reject H0 (unit root) if t-stat < critical value
        double criticalValue = pValueThreshold <= 0.01 ? -3.43
            : pValueThreshold <= 0.05 ? -2.86
            : -2.57;

        return tStat < criticalValue;
    }

    private static decimal StdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0m;
        decimal mean = values.Average();
        decimal variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return (decimal)Math.Sqrt((double)variance);
    }
}
