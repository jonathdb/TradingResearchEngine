namespace TradingResearchEngine.Application.Metrics;

/// <summary>
/// Computes the Minimum Backtest Length (MinBTL) — the minimum number of bars
/// required for a Sharpe ratio observation to be statistically significant at
/// the 95% confidence level, accounting for non-normality and multiple testing.
/// </summary>
public static class MinBtlCalculator
{
    /// <summary>Z-score for 95% confidence (one-tailed).</summary>
    private const double Z95 = 1.6449;

    /// <summary>
    /// Returns the minimum bar count required for 95% confidence that the
    /// observed Sharpe is not due to chance, given the number of trials,
    /// skewness, and excess kurtosis of the return series.
    /// </summary>
    /// <param name="observedSharpe">Annualised Sharpe ratio.</param>
    /// <param name="trialCount">Number of trials run on this strategy version.</param>
    /// <param name="skewness">Skewness of the return series.</param>
    /// <param name="kurtosis">Excess kurtosis of the return series.</param>
    /// <returns>Minimum bar count. Returns 0 if inputs are degenerate.</returns>
    public static int MinimumBarsRequired(
        decimal observedSharpe,
        int trialCount,
        decimal skewness,
        decimal kurtosis)
    {
        if (observedSharpe <= 0 || trialCount <= 0) return 0;

        double sr = (double)observedSharpe;
        double skew = (double)skewness;
        double kurt = (double)kurtosis;

        // Adjust the required z-score for multiple testing using Bonferroni-like correction
        // For N trials, the effective significance level is alpha/N
        // z_adjusted ≈ z_95 + sqrt(2 * ln(N)) for large N
        double zAdj = Z95;
        if (trialCount > 1)
            zAdj += Math.Sqrt(2.0 * Math.Log(trialCount));

        // MinBTL formula from Bailey & López de Prado:
        // n_min = (z / SR)^2 * (1 - skew*SR + ((kurt-1)/4)*SR^2)
        // where SR is the per-bar Sharpe (we solve for n given annualised SR)
        // Since SR_annual = SR_bar * sqrt(barsPerYear), and we want n in bars:
        // We use the relationship: n_min ≈ (z_adj / SR_bar)^2 * correction
        // Rearranging with SR_bar ≈ SR_annual / sqrt(252) as a rough estimate:
        double srBar = sr / Math.Sqrt(252.0); // approximate with daily
        if (srBar <= 0) return 0;

        double correction = 1.0 - skew * srBar + ((kurt - 1.0) / 4.0) * srBar * srBar;
        if (correction <= 0) correction = 1.0;

        double nMin = (zAdj / srBar) * (zAdj / srBar) * correction;

        return Math.Max(1, (int)Math.Ceiling(nMin));
    }
}
