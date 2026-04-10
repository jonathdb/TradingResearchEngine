namespace TradingResearchEngine.Application.Metrics;

/// <summary>
/// Computes the Deflated Sharpe Ratio (DSR) following Bailey &amp; López de Prado (2014).
/// DSR adjusts the observed Sharpe for multiple testing bias: the more trials you run,
/// the more likely you are to find a high Sharpe by chance alone.
/// </summary>
public static class DsrCalculator
{
    /// <summary>Euler-Mascheroni constant used in expected max approximation.</summary>
    private const double EulerMascheroni = 0.5772156649;

    /// <summary>
    /// Computes the Deflated Sharpe Ratio.
    /// </summary>
    /// <param name="observedSharpe">The annualised Sharpe ratio from the backtest.</param>
    /// <param name="trialCount">Number of trials (runs + sweep combinations) on this strategy version.</param>
    /// <param name="skewness">Skewness of the return series.</param>
    /// <param name="kurtosis">Excess kurtosis of the return series.</param>
    /// <param name="barCount">Number of bars in the backtest.</param>
    /// <param name="barsPerYear">Bars per year for annualisation (e.g. 252 for daily).</param>
    /// <returns>The DSR value. Values below 0.95 suggest possible overfitting.</returns>
    public static decimal Compute(
        decimal observedSharpe,
        int trialCount,
        decimal skewness,
        decimal kurtosis,
        int barCount,
        int barsPerYear)
    {
        if (trialCount <= 0) return observedSharpe;
        if (barCount <= 1) return 0m;

        double sr = (double)observedSharpe;
        double n = barCount;
        double t = barsPerYear;
        double skew = (double)skewness;
        double kurt = (double)kurtosis;
        int trials = trialCount;

        // De-annualise the Sharpe to per-bar Sharpe
        double srPerBar = sr / Math.Sqrt(t);

        // Variance of the Sharpe estimator (Lo 2002, adjusted for non-normality)
        double varSr = (1.0 - skew * srPerBar + ((kurt - 1.0) / 4.0) * srPerBar * srPerBar) / n;
        if (varSr <= 0) varSr = 1.0 / n; // fallback to normal case

        // Expected maximum Sharpe from N independent trials under the null (SR=0)
        // Using the approximation: E[max] ≈ sqrt(2 * ln(N)) * (1 - γ/(2*ln(N))) + γ/(2*sqrt(2*ln(N)))
        double expectedMaxSr;
        if (trials <= 1)
        {
            expectedMaxSr = 0;
        }
        else
        {
            double logN = Math.Log(trials);
            double sqrtTwoLogN = Math.Sqrt(2.0 * logN);
            expectedMaxSr = sqrtTwoLogN * (1.0 - EulerMascheroni / (2.0 * logN))
                            + EulerMascheroni / (2.0 * sqrtTwoLogN);
            // Scale by the standard deviation of the SR estimator
            expectedMaxSr *= Math.Sqrt(varSr);
        }

        // DSR = P(SR* < observed SR) using the standard normal CDF
        // Test statistic: (observed - expected_max) / sqrt(var)
        double stdSr = Math.Sqrt(varSr);
        if (stdSr <= 0) return 0m;

        double zStat = (srPerBar - expectedMaxSr) / stdSr;
        double dsr = NormalCdf(zStat);

        return (decimal)Math.Round(dsr, 4);
    }

    /// <summary>Standard normal CDF approximation (Abramowitz &amp; Stegun).</summary>
    private static double NormalCdf(double x)
    {
        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        int sign = x < 0 ? -1 : 1;
        x = Math.Abs(x) / Math.Sqrt(2.0);

        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return 0.5 * (1.0 + sign * y);
    }
}
