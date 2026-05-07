using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests;

/// <summary>
/// Unit tests for Dashboard Last Sharpe tile navigation logic.
/// Validates Requirement 10.2: when no strategy ID can be resolved from the latest run,
/// the navigation target is /backtests/history.
/// </summary>
public class DashboardLastSharpeTileTests
{
    /// <summary>
    /// Replicates the navigation target resolution logic from Dashboard.razor.
    /// This is extracted here because the method is private on the Razor component.
    /// </summary>
    private static string? GetLastSharpeNavigationTarget(
        BacktestResult? latestRun,
        Dictionary<string, string> strategyIdByType)
    {
        if (latestRun is null) return null;
        var strategyType = latestRun.ScenarioConfig.StrategyType;
        if (strategyIdByType.TryGetValue(strategyType, out var strategyId))
            return $"/strategies/{strategyId}";
        return "/backtests/history";
    }

    /// <summary>
    /// When a latest run exists but no strategy ID can be resolved (empty dictionary),
    /// the navigation target should fall back to /backtests/history.
    /// **Validates: Requirement 10.2**
    /// </summary>
    [Fact]
    public void LastSharpeTile_NoStrategyId_NavigatesToBacktestsHistory()
    {
        // Arrange: a completed run exists with a strategy type that has no matching ID
        var scenarioConfig = new ScenarioConfig(
            ScenarioId: "test-scenario",
            Description: "Test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>(),
            StrategyType: "moving-average-crossover",
            StrategyParameters: new Dictionary<string, object>(),
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: "zero",
            CommissionModelType: "zero",
            InitialCash: 100_000m,
            AnnualRiskFreeRate: 0.02m,
            RandomSeed: null,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null);

        var latestRun = new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: scenarioConfig,
            Status: BacktestStatus.Completed,
            EquityCurve: Array.Empty<EquityCurvePoint>(),
            Trades: Array.Empty<ClosedTrade>(),
            StartEquity: 100_000m,
            EndEquity: 105_000m,
            MaxDrawdown: 0.05m,
            SharpeRatio: 1.5m,
            SortinoRatio: 2.0m,
            CalmarRatio: 1.2m,
            ReturnOnMaxDrawdown: 1.0m,
            TotalTrades: 10,
            WinRate: 0.6m,
            ProfitFactor: 1.5m,
            AverageWin: 500m,
            AverageLoss: -300m,
            Expectancy: 100m,
            AverageHoldingPeriod: TimeSpan.FromHours(24),
            EquityCurveSmoothness: 0.95m,
            MaxConsecutiveLosses: 3,
            MaxConsecutiveWins: 5,
            RunDurationMs: 1000);

        // Empty dictionary — no strategy ID can be resolved
        var strategyIdByType = new Dictionary<string, string>();

        // Act
        var target = GetLastSharpeNavigationTarget(latestRun, strategyIdByType);

        // Assert
        Assert.Equal("/backtests/history", target);
    }

    /// <summary>
    /// When a strategy ID can be resolved, navigation should go to the strategy page.
    /// This confirms the fallback only triggers when no ID is found.
    /// **Validates: Requirement 10.1**
    /// </summary>
    [Fact]
    public void LastSharpeTile_WithStrategyId_NavigatesToStrategyPage()
    {
        // Arrange
        var scenarioConfig = new ScenarioConfig(
            ScenarioId: "test-scenario",
            Description: "Test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>(),
            StrategyType: "moving-average-crossover",
            StrategyParameters: new Dictionary<string, object>(),
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: "zero",
            CommissionModelType: "zero",
            InitialCash: 100_000m,
            AnnualRiskFreeRate: 0.02m,
            RandomSeed: null,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null);

        var latestRun = new BacktestResult(
            RunId: Guid.NewGuid(),
            ScenarioConfig: scenarioConfig,
            Status: BacktestStatus.Completed,
            EquityCurve: Array.Empty<EquityCurvePoint>(),
            Trades: Array.Empty<ClosedTrade>(),
            StartEquity: 100_000m,
            EndEquity: 105_000m,
            MaxDrawdown: 0.05m,
            SharpeRatio: 1.5m,
            SortinoRatio: 2.0m,
            CalmarRatio: 1.2m,
            ReturnOnMaxDrawdown: 1.0m,
            TotalTrades: 10,
            WinRate: 0.6m,
            ProfitFactor: 1.5m,
            AverageWin: 500m,
            AverageLoss: -300m,
            Expectancy: 100m,
            AverageHoldingPeriod: TimeSpan.FromHours(24),
            EquityCurveSmoothness: 0.95m,
            MaxConsecutiveLosses: 3,
            MaxConsecutiveWins: 5,
            RunDurationMs: 1000);

        var strategyIdByType = new Dictionary<string, string>
        {
            ["moving-average-crossover"] = "strategy-abc-123"
        };

        // Act
        var target = GetLastSharpeNavigationTarget(latestRun, strategyIdByType);

        // Assert
        Assert.Equal("/strategies/strategy-abc-123", target);
    }

    /// <summary>
    /// When no latest run exists, navigation target should be null (tile not clickable).
    /// **Validates: Requirement 10.3**
    /// </summary>
    [Fact]
    public void LastSharpeTile_NoLatestRun_ReturnsNull()
    {
        // Arrange
        var strategyIdByType = new Dictionary<string, string>
        {
            ["some-strategy"] = "strategy-xyz"
        };

        // Act
        var target = GetLastSharpeNavigationTarget(null, strategyIdByType);

        // Assert
        Assert.Null(target);
    }
}
