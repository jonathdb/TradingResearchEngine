using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.PropFirm;

public class PropFirmEvaluatorTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly PropFirmEvaluator Evaluator =
        new(NullLoggerFactory.Instance.CreateLogger<PropFirmEvaluator>());

    [Fact]
    public void ComputeChallengeProbability_CorrectFormula()
    {
        var config = new ChallengeConfig(80m, 50m, 500m, 100_000m, 5m, 10m,
            MakeRuleSet());

        // 80 * 50 / 10000 = 0.4
        Assert.Equal(0.4m, Evaluator.ComputeChallengeProbability(config));
    }

    [Fact]
    public void ComputeEconomics_BreakevenMonths_NullWhenPayoutZero()
    {
        var config = new InstantFundingConfig(
            80m, 500m, 100_000m,
            0m, // zero gross return → zero payout
            80m, 0.9m, 12, MakeRuleSet());

        var result = Evaluator.ComputeEconomics(config);
        Assert.Null(result.BreakevenMonths);
    }

    [Fact]
    public void ComputeEconomics_BreakevenMonths_CeilFormula()
    {
        // MonthlyPayout = 100000 * (5/100) * (80/100) * 0.9 = 3600
        // Breakeven = ceil(500 / 3600) = 1
        var config = new InstantFundingConfig(
            80m, 500m, 100_000m,
            5m, 80m, 0.9m, 12, MakeRuleSet());

        var result = Evaluator.ComputeEconomics(config);
        Assert.Equal(1, result.BreakevenMonths);
    }

    [Fact]
    public void Evaluate_DrawdownViolation_FailsChallenge()
    {
        var rules = MakeRuleSet(maxTotalDd: 5m);
        var result = MakeBacktestResult(maxDd: 0.10m); // 10% > 5%

        var report = Evaluator.Evaluate(result, rules);
        Assert.False(report.Passed);
        Assert.Equal("Failed", report.ChallengeOutcome);
        Assert.NotEmpty(report.ViolatedRules);
    }

    [Fact]
    public void Evaluate_AllRulesPassed_ReturnsPassedReport()
    {
        var rules = MakeRuleSet(maxTotalDd: 20m, minDays: 1);
        var result = MakeBacktestResult(maxDd: 0.05m, totalTrades: 10);

        var report = Evaluator.Evaluate(result, rules);
        Assert.True(report.Passed);
        Assert.Equal("Passed", report.ChallengeOutcome);
        Assert.Empty(report.ViolatedRules);
    }

    private static FirmRuleSet MakeRuleSet(
        decimal maxTotalDd = 10m, int minDays = 5) =>
        new("TestFirm", 5m, maxTotalDd, minDays, null, null, new List<string>());

    private static BacktestResult MakeBacktestResult(
        decimal maxDd = 0.05m, int totalTrades = 10) =>
        new(Guid.NewGuid(),
            new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            new List<EquityCurvePoint> { new(T0, 100_000m) },
            new List<ClosedTrade>(),
            100_000m, 105_000m, maxDd, 1.0m, 1.0m, null, null, totalTrades, 0.6m, 1.5m, 200m, -100m, 10m, null, null, 3, 5, 50);
}
