using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Application.PropFirm;
using TradingResearchEngine.Application.PropFirm.Results;
using TradingResearchEngine.Application.Research;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.V3;

public class V3DomainModelTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // --- Strategy Identity ---

    [Fact]
    public void StrategyIdentity_JsonRoundTrip()
    {
        var identity = new StrategyIdentity("test-id", "Test Strategy", "sma-crossover", T0, "A test");
        var json = JsonSerializer.Serialize(identity, JsonOpts);
        var deserialized = JsonSerializer.Deserialize<StrategyIdentity>(json, JsonOpts);
        Assert.NotNull(deserialized);
        Assert.Equal(identity.StrategyId, deserialized.StrategyId);
        Assert.Equal(identity.StrategyName, deserialized.StrategyName);
        Assert.Equal(identity.StrategyType, deserialized.StrategyType);
    }

    [Fact]
    public void StrategyVersion_LinksToParent()
    {
        var config = new ScenarioConfig("s1", "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "sma-crossover", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null);

        var version = new StrategyVersion("v1", "strategy-1", 1,
            new Dictionary<string, object> { ["fastPeriod"] = 10 }, config, T0, "Initial version");

        Assert.Equal("strategy-1", version.StrategyId);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("v1", version.Id);
    }

    // --- Study Record ---

    [Fact]
    public void StudyRecord_StatusTransitions()
    {
        var study = new StudyRecord("study-1", "v1", StudyType.MonteCarlo, StudyStatus.Running, T0);
        Assert.Equal(StudyStatus.Running, study.Status);

        var completed = study with { Status = StudyStatus.Completed };
        Assert.Equal(StudyStatus.Completed, completed.Status);

        var failed = study with { Status = StudyStatus.Failed, ErrorSummary = "Out of memory" };
        Assert.Equal("Out of memory", failed.ErrorSummary);
    }

    // --- Prop Firm Rule Pack ---

    [Fact]
    public void PropFirmRulePack_MultiPhase_Evaluation()
    {
        var pack = new PropFirmRulePack("ftmo-100k", "FTMO", "100k Challenge", 100_000m,
            new[]
            {
                new ChallengePhase("Phase 1", 10m, 5m, 10m, 4),
                new ChallengePhase("Phase 2", 5m, 5m, 10m, 4),
            });

        var result = MakeBacktestResult(endEquity: 112_000m, maxDrawdown: 0.04m, totalTrades: 15);
        var evaluator = new PropFirmEvaluator(new NullLoggerFactory().CreateLogger<PropFirmEvaluator>());
        var phaseResults = evaluator.EvaluateRulePack(result, pack);

        Assert.Equal(2, phaseResults.Count);
        Assert.Equal("Phase 1", phaseResults[0].PhaseName);
        Assert.True(phaseResults[0].Passed); // 12% return > 10% target, 4% DD < 10% limit
    }

    [Fact]
    public void PropFirmRulePack_NearBreach_Detected()
    {
        var pack = new PropFirmRulePack("test", "Test", "Test", 100_000m,
            new[] { new ChallengePhase("Phase 1", 10m, 5m, 10m, 4) });

        // 8.5% drawdown is within 20% of 10% limit → near breach
        var result = MakeBacktestResult(endEquity: 115_000m, maxDrawdown: 0.085m, totalTrades: 10);
        var evaluator = new PropFirmEvaluator(new NullLoggerFactory().CreateLogger<PropFirmEvaluator>());
        var phaseResults = evaluator.EvaluateRulePack(result, pack);

        var ddRule = phaseResults[0].Rules.First(r => r.RuleName == "Max Total Drawdown");
        Assert.Equal(RuleStatus.NearBreach, ddRule.Status);
    }

    // --- Strategy Templates ---

    [Fact]
    public void DefaultTemplates_AllHaveValidStrategyTypes()
    {
        var templates = DefaultStrategyTemplates.All;
        Assert.True(templates.Count >= 6);
        foreach (var t in templates)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.StrategyType));
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.True(t.DefaultParameters.Count > 0);
        }
    }

    // --- BacktestResult StrategyVersionId ---

    [Fact]
    public void BacktestResult_StrategyVersionId_DefaultNull()
    {
        var result = MakeBacktestResult();
        Assert.Null(result.StrategyVersionId);
    }

    [Fact]
    public void BacktestResult_StrategyVersionId_CanBeSet()
    {
        var result = MakeBacktestResult();
        var linked = result with { StrategyVersionId = "v1" };
        Assert.Equal("v1", linked.StrategyVersionId);
    }

    private static BacktestResult MakeBacktestResult(
        decimal endEquity = 110_000m, decimal maxDrawdown = 0.05m, int totalTrades = 10)
    {
        var config = new ScenarioConfig("test", "Test", ReplayMode.Bar, "csv",
            new Dictionary<string, object>(), "test", new Dictionary<string, object>(),
            new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null);

        return new BacktestResult(
            Guid.NewGuid(), config, BacktestStatus.Completed,
            new List<EquityCurvePoint>(),
            new List<ClosedTrade>(),
            100_000m, endEquity, maxDrawdown,
            null, null, null, null, totalTrades,
            null, null, null, null, null, null, null, 0, 0, 100);
    }
}
