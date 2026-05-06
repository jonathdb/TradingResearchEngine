using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Infrastructure.Export;

namespace TradingResearchEngine.IntegrationTests.Export;

// Feature: trading-research-engine, Property 3: Export produces valid platform-specific structure

/// <summary>
/// Property-based tests verifying that for any valid StrategyVersion with a known built-in
/// strategy type and for any ExportFormat, the exporter produces non-empty Code containing
/// required structural elements.
/// </summary>
/// <remarks>
/// **Validates: Requirements 4.1, 5.1, 6.1**
/// </remarks>
public class StrategyExporterProperties
{
    private static readonly string[] BuiltInStrategies =
    {
        "moving-average-crossover",
        "volatility-scaled-trend",
        "zscore-mean-reversion",
        "stationary-mean-reversion",
        "donchian-breakout",
        "macro-regime"
    };

    private static readonly IStrategyExporter[] Exporters =
    {
        new MQL4StrategyExporter(),
        new MQL5StrategyExporter(),
        new PineScriptExporter()
    };

    private static StrategyVersion CreateVersionForStrategy(string strategyType, Dictionary<string, object> parameters)
    {
        var scenarioConfig = new ScenarioConfig(
            ScenarioId: "prop-test",
            Description: "Property test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>(),
            StrategyType: strategyType,
            StrategyParameters: parameters,
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: "zero",
            CommissionModelType: "zero",
            InitialCash: 100_000m,
            AnnualRiskFreeRate: 0.05m,
            RandomSeed: null,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null);

        return new StrategyVersion(
            StrategyVersionId: $"v-{Guid.NewGuid():N}",
            StrategyId: "s-prop",
            VersionNumber: 1,
            Parameters: parameters,
            BaseScenarioConfig: scenarioConfig,
            CreatedAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// For any valid StrategyVersion with a known built-in strategy type and for any ExportFormat,
    /// the exporter produces non-empty Code containing required structural elements.
    /// MQL4 requires OnInit(), OnTick(), OnDeinit().
    /// MQL5 requires CTrade, OnTick().
    /// PineScript requires strategy(), strategy.entry().
    /// </summary>
    [Property(MaxTest = 20)]
    public Property ExportProducesValidPlatformSpecificStructure()
    {
        var strategyGen = Gen.Elements(BuiltInStrategies);
        var formatGen = Gen.Elements(ExportFormat.MQL4, ExportFormat.MQL5, ExportFormat.PineScript);

        var gen = from strategy in strategyGen
                  from format in formatGen
                  from period in Gen.Choose(5, 200)
                  from fast in Gen.Choose(3, 50)
                  from slow in Gen.Choose(10, 100)
                  select (strategy, format, period, fast, slow);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (strategyType, format, period, fast, slow) = tuple;

            var parameters = new Dictionary<string, object>
            {
                ["period"] = period,
                ["fastPeriod"] = fast,
                ["slowPeriod"] = slow,
                ["atrPeriod"] = 14,
                ["atrMultiplier"] = 2.0,
                ["entryThreshold"] = -2.0,
                ["exitThreshold"] = 0.0,
                ["shortPeriod"] = fast,
                ["longPeriod"] = slow
            };

            var version = CreateVersionForStrategy(strategyType, parameters);
            var exporter = Exporters.First(e => e.Format == format);

            var result = exporter.ExportAsync(version, CancellationToken.None).GetAwaiter().GetResult();

            // Code must be non-empty
            if (string.IsNullOrEmpty(result.Code))
                return false.Label($"Code was empty for {strategyType}/{format}");

            // Verify platform-specific structural elements
            return format switch
            {
                ExportFormat.MQL4 =>
                    (result.Code.Contains("OnInit()") &&
                     result.Code.Contains("OnTick()") &&
                     result.Code.Contains("OnDeinit("))
                    .Label($"MQL4 missing structural elements for {strategyType}"),

                ExportFormat.MQL5 =>
                    (result.Code.Contains("CTrade") &&
                     result.Code.Contains("OnTick()"))
                    .Label($"MQL5 missing structural elements for {strategyType}"),

                ExportFormat.PineScript =>
                    (result.Code.Contains("strategy(") &&
                     result.Code.Contains("strategy.entry("))
                    .Label($"PineScript missing structural elements for {strategyType}"),

                _ => false.Label("Unknown format")
            };
        });
    }
}
