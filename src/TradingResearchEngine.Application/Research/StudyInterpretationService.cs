using System.Text;
using TradingResearchEngine.Application.Research.Results;

namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Generates result-aware textual interpretations with quantitative threshold warnings.
/// Unit-testable via DI — no inline Razor logic.
/// </summary>
public sealed class StudyInterpretationService
{
    /// <summary>Warning threshold: Monte Carlo ruin probability above 5%.</summary>
    private const decimal RuinWarningThreshold = 0.05m;

    /// <summary>Warning threshold: CPCV probability of overfitting above 50%.</summary>
    private const decimal OverfitWarningThreshold = 0.50m;

    /// <summary>Warning threshold: OOS Sharpe below 50% of IS Sharpe.</summary>
    private const decimal DegradationRatioThreshold = 0.50m;

    /// <summary>Warning threshold: Parameter sweep with less than 20% positive-Sharpe cells.</summary>
    private const decimal FragilePeakThreshold = 0.20m;

    /// <summary>Interprets Monte Carlo simulation results.</summary>
    public string InterpretMonteCarlo(MonteCarloResult result)
    {
        var sb = new StringBuilder();
        sb.Append($"Median end equity is ${result.P50EndEquity:F0} ");
        sb.Append($"(P10: ${result.P10EndEquity:F0}, P90: ${result.P90EndEquity:F0}). ");
        sb.Append($"Median max drawdown is {result.MedianMaxDrawdown:P1}. ");

        if (result.RuinProbability > RuinWarningThreshold)
        {
            sb.Append($"⚠ ELEVATED RUIN RISK: Ruin probability is {result.RuinProbability:P1}, " +
                      $"exceeding the {RuinWarningThreshold:P0} threshold. Consider reducing position size or tightening stops.");
        }
        else
        {
            sb.Append($"Ruin probability is {result.RuinProbability:P1} — within acceptable bounds.");
        }

        return sb.ToString();
    }

    /// <summary>Interprets walk-forward analysis results.</summary>
    public string InterpretWalkForward(WalkForwardResult result)
    {
        var sb = new StringBuilder();

        if (result.Windows.Count == 0)
        {
            sb.Append("No walk-forward windows were completed.");
            return sb.ToString();
        }

        var isSharpes = result.Windows
            .Where(w => w.InSampleResult.SharpeRatio.HasValue)
            .Select(w => w.InSampleResult.SharpeRatio!.Value)
            .ToList();
        var oosSharpes = result.Windows
            .Where(w => w.OutOfSampleResult.SharpeRatio.HasValue)
            .Select(w => w.OutOfSampleResult.SharpeRatio!.Value)
            .ToList();

        if (isSharpes.Count > 0 && oosSharpes.Count > 0)
        {
            decimal meanIs = isSharpes.Average();
            decimal meanOos = oosSharpes.Average();
            sb.Append($"Mean IS Sharpe: {meanIs:F2}, Mean OOS Sharpe: {meanOos:F2}. ");

            if (meanIs > 0 && meanOos < meanIs * DegradationRatioThreshold)
            {
                sb.Append($"⚠ PERFORMANCE DEGRADATION: OOS Sharpe is less than 50% of IS Sharpe, " +
                          $"suggesting significant overfitting to in-sample data.");
            }
            else if (result.MeanEfficiencyRatio.HasValue)
            {
                sb.Append($"Mean efficiency ratio: {result.MeanEfficiencyRatio.Value:P0}.");
            }
        }

        return sb.ToString();
    }

    /// <summary>Interprets CPCV results.</summary>
    public string InterpretCpcv(CpcvResult result)
    {
        var sb = new StringBuilder();
        sb.Append($"Median OOS Sharpe: {result.MedianOosSharpe:F3}. ");
        sb.Append($"Performance degradation: {result.PerformanceDegradation:P1}. ");

        if (result.ProbabilityOfOverfitting > OverfitWarningThreshold)
        {
            sb.Append($"⚠ CRITICAL OVERFITTING: Probability of overfitting is {result.ProbabilityOfOverfitting:P0}, " +
                      $"exceeding the {OverfitWarningThreshold:P0} threshold. " +
                      $"The strategy's in-sample performance is unlikely to persist out-of-sample.");
        }
        else
        {
            sb.Append($"Probability of overfitting: {result.ProbabilityOfOverfitting:P0} — acceptable.");
        }

        return sb.ToString();
    }

    /// <summary>Interprets parameter sweep results.</summary>
    public string InterpretParameterSweep(SweepResult result)
    {
        var sb = new StringBuilder();

        if (result.RankedBySharpe.Count == 0)
        {
            sb.Append("No parameter combinations produced results.");
            return sb.ToString();
        }

        var best = result.RankedBySharpe.First();
        sb.Append($"Best Sharpe: {best.SharpeRatio?.ToString("F2") ?? "N/A"} ");
        sb.Append($"across {result.RankedBySharpe.Count} combinations. ");

        int positiveCount = result.RankedBySharpe.Count(r => r.SharpeRatio > 0);
        decimal positiveRatio = (decimal)positiveCount / result.RankedBySharpe.Count;

        if (positiveRatio < FragilePeakThreshold)
        {
            sb.Append($"⚠ FRAGILE PEAK: Only {positiveRatio:P0} of parameter combinations produce positive Sharpe. " +
                      $"The optimal parameters may be a narrow peak rather than a robust plateau.");
        }
        else
        {
            sb.Append($"{positiveRatio:P0} of combinations produce positive Sharpe — parameter surface appears stable.");
        }

        return sb.ToString();
    }

    /// <summary>Interprets realism sensitivity results.</summary>
    public string InterpretRealism(RealismSensitivityResult result)
    {
        var sb = new StringBuilder();
        sb.Append($"Tested across {result.ProfileResults.Count} realism profiles. ");

        if (result.ProfileResults.Count > 0)
        {
            var sharpes = result.ProfileResults
                .Where(p => p.Result.SharpeRatio.HasValue)
                .Select(p => p.Result.SharpeRatio!.Value)
                .ToList();

            if (sharpes.Count > 0)
            {
                sb.Append($"Sharpe range: {sharpes.Min():F2} to {sharpes.Max():F2}. ");
                if (sharpes.Min() < 0)
                    sb.Append("⚠ Strategy becomes unprofitable under conservative execution assumptions.");
            }
        }

        return sb.ToString();
    }

    /// <summary>Interprets benchmark comparison results.</summary>
    public string InterpretBenchmark(BenchmarkComparisonResult result)
    {
        var sb = new StringBuilder();
        sb.Append($"Strategy return: {result.StrategyReturn:P2}, Benchmark return: {result.BenchmarkReturn:P2}. ");
        sb.Append($"Alpha: {result.Alpha:P2}. ");

        if (result.Alpha < 0)
            sb.Append("⚠ Strategy underperforms the benchmark on a risk-adjusted basis.");
        else
            sb.Append("Strategy generates positive alpha over the benchmark.");

        return sb.ToString();
    }
}
