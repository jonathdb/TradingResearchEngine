using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Research.Results;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.Research;

// Feature: trading-engine-stories, Property 12: Interpretation Service Threshold Warnings

/// <summary>
/// Property 12: Interpretation Service Threshold Warnings.
/// For any MonteCarloResult where RuinProbability > 0.05m, the interpretation SHALL contain a ruin risk warning.
/// For any CpcvResult where ProbabilityOfOverfitting > 0.50m, the interpretation SHALL contain an overfitting warning.
/// For any WalkForwardResult where OOS Sharpe &lt; 50% of IS Sharpe, the interpretation SHALL contain a degradation warning.
/// **Validates: Requirements 22.2, 22.3, 22.4**
/// </summary>
public class InterpretationServiceProperties
{
    private readonly StudyInterpretationService _service = new();

    /// <summary>
    /// For any MonteCarloResult with RuinProbability > 0.05, the interpretation contains a ruin warning.
    /// **Validates: Requirements 22.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MonteCarlo_RuinAboveThreshold_ContainsRuinWarning(PositiveInt ruinSeed)
    {
        // Generate ruin probability in (0.05, 1.0] range
        decimal ruinProbability = 0.05m + ((ruinSeed.Item % 95) + 1) / 100m;

        var result = CreateMonteCarloResult(ruinProbability);
        var interpretation = _service.InterpretMonteCarlo(result);

        return interpretation.Contains("ELEVATED RUIN RISK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For any MonteCarloResult with RuinProbability &lt;= 0.05, the interpretation does NOT contain a ruin warning.
    /// **Validates: Requirements 22.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool MonteCarlo_RuinBelowOrAtThreshold_NoRuinWarning(NonNegativeInt ruinSeed)
    {
        // Generate ruin probability in [0.00, 0.05] range
        decimal ruinProbability = (ruinSeed.Item % 6) / 100m;

        var result = CreateMonteCarloResult(ruinProbability);
        var interpretation = _service.InterpretMonteCarlo(result);

        return !interpretation.Contains("ELEVATED RUIN RISK", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For any CpcvResult with ProbabilityOfOverfitting > 0.50, the interpretation contains an overfitting warning.
    /// **Validates: Requirements 22.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Cpcv_OverfittingAboveThreshold_ContainsOverfittingWarning(PositiveInt overfitSeed)
    {
        // Generate probability of overfitting in (0.50, 1.0] range
        decimal overfitProbability = 0.50m + ((overfitSeed.Item % 50) + 1) / 100m;

        var result = CreateCpcvResult(overfitProbability);
        var interpretation = _service.InterpretCpcv(result);

        return interpretation.Contains("CRITICAL OVERFITTING", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For any CpcvResult with ProbabilityOfOverfitting &lt;= 0.50, the interpretation does NOT contain an overfitting warning.
    /// **Validates: Requirements 22.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool Cpcv_OverfittingBelowOrAtThreshold_NoOverfittingWarning(NonNegativeInt overfitSeed)
    {
        // Generate probability of overfitting in [0.00, 0.50] range
        decimal overfitProbability = (overfitSeed.Item % 51) / 100m;

        var result = CreateCpcvResult(overfitProbability);
        var interpretation = _service.InterpretCpcv(result);

        return !interpretation.Contains("CRITICAL OVERFITTING", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For any WalkForwardResult where mean OOS Sharpe &lt; 50% of mean IS Sharpe (with IS > 0),
    /// the interpretation contains a degradation warning.
    /// **Validates: Requirements 22.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool WalkForward_OosBelowHalfIs_ContainsDegradationWarning(PositiveInt isSeed, NonNegativeInt ratioSeed)
    {
        // IS Sharpe in [1.0, 5.0] range (positive)
        decimal isSharpe = 1.0m + (isSeed.Item % 401) / 100m;
        // OOS ratio in [0%, 49%] of IS — strictly below 50%
        decimal oosRatio = (ratioSeed.Item % 50) / 100m;
        decimal oosSharpe = isSharpe * oosRatio;

        int windowCount = (isSeed.Item % 5) + 2; // 2 to 6 windows

        var result = CreateWalkForwardResult(isSharpe, oosSharpe, windowCount);
        var interpretation = _service.InterpretWalkForward(result);

        return interpretation.Contains("PERFORMANCE DEGRADATION", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For any WalkForwardResult where mean OOS Sharpe >= 50% of mean IS Sharpe (with IS > 0),
    /// the interpretation does NOT contain a degradation warning.
    /// **Validates: Requirements 22.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool WalkForward_OosAboveOrAtHalfIs_NoDegradationWarning(PositiveInt isSeed, PositiveInt ratioSeed)
    {
        // IS Sharpe in [1.0, 5.0] range (positive)
        decimal isSharpe = 1.0m + (isSeed.Item % 401) / 100m;
        // OOS ratio in [50%, 150%] of IS — at or above 50%
        decimal oosRatio = 0.50m + (ratioSeed.Item % 101) / 100m;
        decimal oosSharpe = isSharpe * oosRatio;

        int windowCount = (isSeed.Item % 5) + 2; // 2 to 6 windows

        var result = CreateWalkForwardResult(isSharpe, oosSharpe, windowCount);
        var interpretation = _service.InterpretWalkForward(result);

        return !interpretation.Contains("PERFORMANCE DEGRADATION", StringComparison.OrdinalIgnoreCase);
    }

    #region Helper Methods

    private static MonteCarloResult CreateMonteCarloResult(decimal ruinProbability)
    {
        return new MonteCarloResult(
            P10EndEquity: 80000m,
            P50EndEquity: 120000m,
            P90EndEquity: 180000m,
            RuinProbability: ruinProbability,
            MedianMaxDrawdown: 0.15m,
            EndEquityDistribution: Array.Empty<decimal>(),
            P90MaxConsecutiveLosses: 5,
            P90MaxConsecutiveWins: 8,
            SampledPaths: Array.Empty<MonteCarloPath>(),
            PercentileBands: Array.Empty<MonteCarloPercentileBand>());
    }

    private static CpcvResult CreateCpcvResult(decimal probabilityOfOverfitting)
    {
        return new CpcvResult(
            MedianOosSharpe: 0.5m,
            ProbabilityOfOverfitting: probabilityOfOverfitting,
            PerformanceDegradation: 0.3m,
            OosSharpeDistribution: Array.Empty<decimal>(),
            TotalCombinations: 50,
            IsSharpeDistribution: Array.Empty<decimal>());
    }

    private static WalkForwardResult CreateWalkForwardResult(decimal isSharpe, decimal oosSharpe, int windowCount)
    {
        var windows = Enumerable.Range(0, windowCount)
            .Select(i => new WalkForwardWindow(
                WindowIndex: i,
                InSampleResult: CreateBacktestResultWithSharpe(isSharpe),
                OutOfSampleResult: CreateBacktestResultWithSharpe(oosSharpe),
                SelectedParameters: new Dictionary<string, object>(),
                EfficiencyRatio: isSharpe > 0 ? oosSharpe / isSharpe : null))
            .ToList();

        return new WalkForwardResult(
            Windows: windows,
            MeanEfficiencyRatio: isSharpe > 0 ? oosSharpe / isSharpe : null);
    }

    private static BacktestResult CreateBacktestResultWithSharpe(decimal sharpe)
    {
        return new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: null!,
            Status: BacktestStatus.Completed,
            EquityCurve: Array.Empty<EquityCurvePoint>(),
            Trades: Array.Empty<ClosedTrade>(),
            StartEquity: 100_000m,
            EndEquity: 110_000m,
            MaxDrawdown: 0.10m,
            SharpeRatio: sharpe,
            SortinoRatio: null,
            CalmarRatio: null,
            ReturnOnMaxDrawdown: null,
            TotalTrades: 50,
            WinRate: 0.55m,
            ProfitFactor: 1.5m,
            AverageWin: 200m,
            AverageLoss: -100m,
            Expectancy: 50m,
            AverageHoldingPeriod: TimeSpan.FromHours(4),
            EquityCurveSmoothness: 0.95m,
            MaxConsecutiveLosses: 3,
            MaxConsecutiveWins: 5,
            RunDurationMs: 1000);
    }

    #endregion
}
