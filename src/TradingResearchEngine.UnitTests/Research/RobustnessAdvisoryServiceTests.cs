using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.Research;

public class RobustnessAdvisoryServiceTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RobustnessAdvisoryService CreateService(RobustnessThresholds? thresholds = null)
    {
        var options = Options.Create(thresholds ?? new RobustnessThresholds());
        return new RobustnessAdvisoryService(options);
    }

    [Fact]
    public void GetWarnings_AllMetricsWithinThresholds_ReturnsEmpty()
    {
        var service = CreateService();
        var result = MakeResult(sharpe: 1.5m, totalTrades: 50, kRatio: 0.5m, maxDrawdown: 0.10m);

        var warnings = service.GetWarnings(result);

        Assert.Empty(warnings);
    }

    [Fact]
    public void GetWarnings_SharpeExceedsThreshold_ReturnsWarning()
    {
        var service = CreateService();
        var result = MakeResult(sharpe: 3.5m, totalTrades: 50, kRatio: 0.5m, maxDrawdown: 0.10m);

        var warnings = service.GetWarnings(result);

        Assert.Single(warnings);
        Assert.Contains("Sharpe > 3.0", warnings[0]);
    }

    [Fact]
    public void GetWarnings_TotalTradesBelowThreshold_ReturnsWarning()
    {
        var service = CreateService();
        var result = MakeResult(sharpe: 1.0m, totalTrades: 10, kRatio: 0.5m, maxDrawdown: 0.10m);

        var warnings = service.GetWarnings(result);

        Assert.Single(warnings);
        Assert.Contains("10 trades", warnings[0]);
    }

    [Fact]
    public void GetWarnings_KRatioBelowThreshold_ReturnsWarning()
    {
        var service = CreateService();
        var result = MakeResult(sharpe: 1.0m, totalTrades: 50, kRatio: -0.5m, maxDrawdown: 0.10m);

        var warnings = service.GetWarnings(result);

        Assert.Single(warnings);
        Assert.Contains("K-Ratio < 0", warnings[0]);
    }

    [Fact]
    public void GetWarnings_MaxDrawdownExceedsThreshold_ReturnsWarning()
    {
        var service = CreateService();
        var result = MakeResult(sharpe: 1.0m, totalTrades: 50, kRatio: 0.5m, maxDrawdown: 0.30m);

        var warnings = service.GetWarnings(result);

        Assert.Single(warnings);
        Assert.Contains("DD", warnings[0]);
    }

    [Fact]
    public void GetWarnings_AllMetricsViolated_ReturnsFourWarnings()
    {
        var service = CreateService();
        var result = MakeResult(sharpe: 5.0m, totalTrades: 5, kRatio: -1.0m, maxDrawdown: 0.50m);

        var warnings = service.GetWarnings(result);

        Assert.Equal(4, warnings.Count);
    }

    [Fact]
    public void GetWarnings_CustomThresholds_UsesConfiguredValues()
    {
        var thresholds = new RobustnessThresholds
        {
            MaxSharpeRatio = 2.0m,
            MinTotalTrades = 50,
            MinKRatio = 0.5m,
            MaxDrawdownPercent = 0.10m
        };
        var service = CreateService(thresholds);
        // Values that pass default thresholds but fail custom ones
        var result = MakeResult(sharpe: 2.5m, totalTrades: 40, kRatio: 0.3m, maxDrawdown: 0.15m);

        var warnings = service.GetWarnings(result);

        Assert.Equal(4, warnings.Count);
    }

    [Fact]
    public void GetWarnings_NullSharpeRatio_DoesNotWarn()
    {
        var service = CreateService();
        var result = MakeResult(sharpe: null, totalTrades: 50, kRatio: 0.5m, maxDrawdown: 0.10m);

        var warnings = service.GetWarnings(result);

        Assert.Empty(warnings);
    }

    [Fact]
    public void GetWarnings_NullKRatio_DoesNotWarn()
    {
        var service = CreateService();
        var result = MakeResult(sharpe: 1.0m, totalTrades: 50, kRatio: null, maxDrawdown: 0.10m);

        var warnings = service.GetWarnings(result);

        Assert.Empty(warnings);
    }

    private static BacktestResult MakeResult(
        decimal? sharpe = 1.0m,
        int totalTrades = 50,
        decimal? kRatio = 0.5m,
        decimal maxDrawdown = 0.10m) =>
        new(Guid.NewGuid(),
            new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint> { new(T0, 100_000m) },
            new List<ClosedTrade>(),
            100_000m, 105_000m, maxDrawdown, sharpe, null, null, null, totalTrades,
            0.6m, 1.5m, 200m, -100m, 10m, null, kRatio, 3, 5, 50);
}
