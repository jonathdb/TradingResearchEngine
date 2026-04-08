using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Core.Metrics;

/// <summary>Pure static performance metric calculations. No side effects.</summary>
public static class MetricsCalculator
{
    /// <summary>
    /// Computes the maximum peak-to-trough decline in total equity as a decimal fraction.
    /// Returns 0 when the curve has fewer than two points.
    /// </summary>
    public static decimal ComputeMaxDrawdown(IReadOnlyList<EquityCurvePoint> curve)
    {
        if (curve.Count < 2) return 0m;

        decimal peak = curve[0].TotalEquity;
        decimal maxDrawdown = 0m;

        foreach (var point in curve)
        {
            if (point.TotalEquity > peak) peak = point.TotalEquity;
            if (peak > 0m)
            {
                decimal drawdown = (peak - point.TotalEquity) / peak;
                if (drawdown > maxDrawdown) maxDrawdown = drawdown;
            }
        }
        return maxDrawdown;
    }

    /// <summary>
    /// Computes the annualised Sharpe ratio from equity curve period-by-period returns.
    /// Returns <c>null</c> when the curve has fewer than 2 points or zero standard deviation.
    /// </summary>
    public static decimal? ComputeSharpeRatio(
        IReadOnlyList<EquityCurvePoint> curve, decimal annualRiskFreeRate, int barsPerYear)
    {
        if (curve.Count < 2) return null;

        var returns = GetPeriodReturns(curve);
        if (returns.Count == 0) return null;

        decimal stdDev = StdDev(returns);
        if (stdDev == 0m) return null;

        decimal meanReturn = returns.Average();
        decimal periodRiskFree = annualRiskFreeRate / barsPerYear;
        return (meanReturn - periodRiskFree) / stdDev * (decimal)Math.Sqrt(barsPerYear);
    }

    /// <summary>
    /// Computes the annualised Sortino ratio from equity curve period-by-period returns.
    /// Downside deviation uses period returns below the risk-free rate.
    /// Returns <c>null</c> when the curve has fewer than 2 points.
    /// </summary>
    public static decimal? ComputeSortinoRatio(
        IReadOnlyList<EquityCurvePoint> curve, decimal annualRiskFreeRate, int barsPerYear)
    {
        if (curve.Count < 2) return null;

        var returns = GetPeriodReturns(curve);
        if (returns.Count == 0) return null;

        decimal periodRiskFree = annualRiskFreeRate / barsPerYear;
        decimal meanReturn = returns.Average();

        var downsideReturns = returns.Where(r => r < periodRiskFree).ToList();
        if (downsideReturns.Count == 0) return null;

        decimal downsideDev = StdDev(downsideReturns);
        if (downsideDev == 0m) return null;

        return (meanReturn - periodRiskFree) / downsideDev * (decimal)Math.Sqrt(barsPerYear);
    }

    /// <summary>Returns the win rate as a fraction [0,1], or <c>null</c> when there are no trades.</summary>
    public static decimal? ComputeWinRate(IReadOnlyList<ClosedTrade> trades)
    {
        if (trades.Count == 0) return null;
        int wins = trades.Count(t => t.NetPnl > 0);
        return (decimal)wins / trades.Count;
    }

    /// <summary>Returns gross profit divided by gross loss, or <c>null</c> when there are no trades.</summary>
    public static decimal? ComputeProfitFactor(IReadOnlyList<ClosedTrade> trades)
    {
        if (trades.Count == 0) return null;
        decimal grossProfit = trades.Where(t => t.NetPnl > 0).Sum(t => t.NetPnl);
        decimal grossLoss = Math.Abs(trades.Where(t => t.NetPnl < 0).Sum(t => t.NetPnl));
        return grossLoss == 0m ? null : grossProfit / grossLoss;
    }

    /// <summary>Returns the average net P&amp;L of winning trades, or <c>null</c> when there are no trades.</summary>
    public static decimal? ComputeAverageWin(IReadOnlyList<ClosedTrade> trades)
    {
        if (trades.Count == 0) return null;
        var wins = trades.Where(t => t.NetPnl > 0).ToList();
        return wins.Count == 0 ? null : wins.Average(t => t.NetPnl);
    }

    /// <summary>Returns the average net P&amp;L of losing trades (negative value), or <c>null</c> when there are no trades.</summary>
    public static decimal? ComputeAverageLoss(IReadOnlyList<ClosedTrade> trades)
    {
        if (trades.Count == 0) return null;
        var losses = trades.Where(t => t.NetPnl < 0).ToList();
        return losses.Count == 0 ? null : losses.Average(t => t.NetPnl);
    }

    /// <summary>
    /// Computes expectancy: (WinRate × AvgWin) + (LossRate × AvgLoss).
    /// Returns <c>null</c> when there are no trades.
    /// </summary>
    public static decimal? ComputeExpectancy(IReadOnlyList<ClosedTrade> trades)
    {
        if (trades.Count == 0) return null;
        var wins = trades.Where(t => t.NetPnl > 0).ToList();
        var losses = trades.Where(t => t.NetPnl < 0).ToList();
        decimal winRate = (decimal)wins.Count / trades.Count;
        decimal lossRate = (decimal)losses.Count / trades.Count;
        decimal avgWin = wins.Count > 0 ? wins.Average(t => t.NetPnl) : 0m;
        decimal avgLoss = losses.Count > 0 ? losses.Average(t => t.NetPnl) : 0m;
        return (winRate * avgWin) + (lossRate * avgLoss);
    }

    /// <summary>Returns the longest consecutive losing streak, or 0 when there are no trades.</summary>
    public static int ComputeMaxConsecutiveLosses(IReadOnlyList<ClosedTrade> trades)
    {
        int max = 0, current = 0;
        foreach (var t in trades)
        {
            if (t.NetPnl < 0) { current++; if (current > max) max = current; }
            else current = 0;
        }
        return max;
    }

    /// <summary>Returns the longest consecutive winning streak, or 0 when there are no trades.</summary>
    public static int ComputeMaxConsecutiveWins(IReadOnlyList<ClosedTrade> trades)
    {
        int max = 0, current = 0;
        foreach (var t in trades)
        {
            if (t.NetPnl > 0) { current++; if (current > max) max = current; }
            else current = 0;
        }
        return max;
    }

    /// <summary>
    /// Calmar Ratio: annualized return / max drawdown.
    /// Returns <c>null</c> when max drawdown is zero or there are fewer than 2 equity points.
    /// </summary>
    public static decimal? ComputeCalmarRatio(
        IReadOnlyList<EquityCurvePoint> curve, decimal startEquity, decimal endEquity)
    {
        if (curve.Count < 2 || startEquity == 0m) return null;
        decimal maxDd = ComputeMaxDrawdown(curve);
        if (maxDd == 0m) return null;

        var days = (curve[^1].Timestamp - curve[0].Timestamp).TotalDays;
        if (days <= 0) return null;

        decimal totalReturn = (endEquity - startEquity) / startEquity;
        decimal annualizedReturn = totalReturn * (252m / (decimal)days);
        return annualizedReturn / maxDd;
    }

    /// <summary>
    /// Return on Max Drawdown: total return / max drawdown.
    /// Returns <c>null</c> when max drawdown is zero.
    /// </summary>
    public static decimal? ComputeReturnOnMaxDrawdown(
        IReadOnlyList<EquityCurvePoint> curve, decimal startEquity, decimal endEquity)
    {
        if (curve.Count < 2 || startEquity == 0m) return null;
        decimal maxDd = ComputeMaxDrawdown(curve);
        if (maxDd == 0m) return null;
        decimal totalReturn = (endEquity - startEquity) / startEquity;
        return totalReturn / maxDd;
    }

    /// <summary>
    /// Average holding period across all closed trades.
    /// Returns <c>null</c> when there are no trades.
    /// </summary>
    public static TimeSpan? ComputeAverageHoldingPeriod(IReadOnlyList<ClosedTrade> trades)
    {
        if (trades.Count == 0) return null;
        double avgTicks = trades.Average(t => (t.ExitTime - t.EntryTime).Ticks);
        return TimeSpan.FromTicks((long)avgTicks);
    }

    /// <summary>
    /// K-Ratio (Zephyr/Kestner definition): measures the consistency of equity curve growth.
    /// Computed as: (OLS slope of log-equity curve) / (standard error of slope × √n).
    /// Positive = consistent upward progression; negative = consistent decline.
    /// Higher absolute value = more linear progression.
    /// Returns <c>null</c> when fewer than 3 points or when equity contains non-positive values.
    /// </summary>
    public static decimal? ComputeEquityCurveSmoothness(IReadOnlyList<EquityCurvePoint> curve)
    {
        if (curve.Count < 3) return null;

        int n = curve.Count;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

        for (int i = 0; i < n; i++)
        {
            if (curve[i].TotalEquity <= 0m) return null;
            double x = i;
            double y = Math.Log((double)curve[i].TotalEquity);
            sumX += x;
            sumY += y;
            sumXY += x * y;
            sumX2 += x * x;
        }

        double meanX = sumX / n;
        double meanY = sumY / n;
        double sxx = sumX2 - n * meanX * meanX;
        if (sxx == 0) return null;

        double slope = (sumXY - n * meanX * meanY) / sxx;

        // Standard error of slope
        double ssResidual = 0;
        for (int i = 0; i < n; i++)
        {
            double predicted = meanY + slope * (i - meanX);
            double actual = Math.Log((double)curve[i].TotalEquity);
            ssResidual += (actual - predicted) * (actual - predicted);
        }

        double seSlope = Math.Sqrt(ssResidual / ((n - 2) * sxx));
        if (seSlope == 0) return null;

        return (decimal)(slope / (seSlope * Math.Sqrt(n)));
    }

    // --- helpers ---

    /// <summary>Computes period-by-period returns from the equity curve.</summary>
    private static List<decimal> GetPeriodReturns(IReadOnlyList<EquityCurvePoint> curve)
    {
        var returns = new List<decimal>(curve.Count - 1);
        for (int i = 1; i < curve.Count; i++)
        {
            decimal prev = curve[i - 1].TotalEquity;
            if (prev != 0m)
                returns.Add((curve[i].TotalEquity - prev) / prev);
        }
        return returns;
    }

    /// <summary>
    /// Recovery factor: net profit / (max drawdown * start equity).
    /// Returns null when max drawdown is zero.
    /// </summary>
    public static decimal? ComputeRecoveryFactor(
        IReadOnlyList<EquityCurvePoint> curve, decimal startEquity, decimal endEquity)
    {
        if (curve.Count < 2 || startEquity == 0m) return null;
        decimal maxDd = ComputeMaxDrawdown(curve);
        if (maxDd == 0m) return null;
        decimal netProfit = endEquity - startEquity;
        return netProfit / (maxDd * startEquity);
    }

    /// <summary>
    /// Longest flat period: maximum number of bars between consecutive trades.
    /// Returns 0 when there are fewer than 2 trades.
    /// </summary>
    public static int ComputeLongestFlatPeriod(
        IReadOnlyList<ClosedTrade> trades, IReadOnlyList<EquityCurvePoint> curve)
    {
        if (trades.Count < 2 || curve.Count == 0) return 0;

        int maxGap = 0;
        for (int i = 1; i < trades.Count; i++)
        {
            var gap = trades[i].EntryTime - trades[i - 1].ExitTime;
            // Approximate bars from equity curve timestamps
            int barGap = curve.Count(p => p.Timestamp > trades[i - 1].ExitTime && p.Timestamp <= trades[i].EntryTime);
            if (barGap > maxGap) maxGap = barGap;
        }
        return maxGap;
    }

    private static decimal StdDev(IEnumerable<decimal> values)
    {
        var list = values.ToList();
        if (list.Count < 2) return 0m;
        decimal mean = list.Average();
        decimal variance = list.Sum(v => (v - mean) * (v - mean)) / (list.Count - 1);
        return (decimal)Math.Sqrt((double)variance);
    }
}
