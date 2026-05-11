using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Infrastructure.Export;

namespace TradingResearchEngine.IntegrationTests.Export;

/// <summary>
/// Unit tests for all 3 strategy exporters × 6 built-in strategies.
/// Verifies non-empty code, unsupported type handling, and missing parameter defaults.
/// </summary>
public class StrategyExporterTests
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

    private static StrategyVersion CreateVersion(string strategyType, Dictionary<string, object>? parameters = null)
    {
        var scenarioConfig = new ScenarioConfig(
            ScenarioId: "test-scenario",
            Description: "Test",
            ReplayMode: ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: new Dictionary<string, object>(),
            StrategyType: strategyType,
            StrategyParameters: parameters ?? new Dictionary<string, object>(),
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
            StrategyVersionId: "v1",
            StrategyId: "s1",
            VersionNumber: 1,
            Parameters: parameters ?? new Dictionary<string, object>(),
            BaseScenarioConfig: scenarioConfig,
            CreatedAt: DateTimeOffset.UtcNow);
    }

    #region MQL4 Exporter Tests

    [Theory]
    [InlineData("moving-average-crossover")]
    [InlineData("volatility-scaled-trend")]
    [InlineData("zscore-mean-reversion")]
    [InlineData("stationary-mean-reversion")]
    [InlineData("donchian-breakout")]
    [InlineData("macro-regime")]
    public async Task MQL4Exporter_BuiltInStrategy_ProducesNonEmptyCode(string strategyType)
    {
        var exporter = new MQL4StrategyExporter();
        var version = CreateVersion(strategyType);

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Equal(ExportFormat.MQL4, result.Format);
        Assert.NotEmpty(result.Code);
        Assert.NotEmpty(result.FileName);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("moving-average-crossover")]
    [InlineData("volatility-scaled-trend")]
    [InlineData("zscore-mean-reversion")]
    [InlineData("stationary-mean-reversion")]
    [InlineData("donchian-breakout")]
    [InlineData("macro-regime")]
    public async Task MQL4Exporter_BuiltInStrategy_ContainsRequiredStructure(string strategyType)
    {
        var exporter = new MQL4StrategyExporter();
        var version = CreateVersion(strategyType);

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Contains("OnInit()", result.Code);
        Assert.Contains("OnTick()", result.Code);
        Assert.Contains("OnDeinit(", result.Code);
    }

    [Fact]
    public async Task MQL4Exporter_UnsupportedStrategy_ReturnsEmptyCodeWithWarning()
    {
        var exporter = new MQL4StrategyExporter();
        var version = CreateVersion("custom-unknown-strategy");

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Equal(ExportFormat.MQL4, result.Format);
        Assert.Empty(result.Code);
        Assert.Single(result.Warnings);
        Assert.Contains("Unsupported", result.Warnings[0]);
    }

    [Fact]
    public async Task MQL4Exporter_MissingParameters_UsesDefaultsWithoutException()
    {
        var exporter = new MQL4StrategyExporter();
        var version = CreateVersion("moving-average-crossover", new Dictionary<string, object>());

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.NotEmpty(result.Code);
        Assert.Contains("FastPeriod = 12", result.Code);
        Assert.Contains("SlowPeriod = 26", result.Code);
    }

    #endregion

    #region MQL5 Exporter Tests

    [Theory]
    [InlineData("moving-average-crossover")]
    [InlineData("volatility-scaled-trend")]
    [InlineData("zscore-mean-reversion")]
    [InlineData("stationary-mean-reversion")]
    [InlineData("donchian-breakout")]
    [InlineData("macro-regime")]
    public async Task MQL5Exporter_BuiltInStrategy_ProducesNonEmptyCode(string strategyType)
    {
        var exporter = new MQL5StrategyExporter();
        var version = CreateVersion(strategyType);

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Equal(ExportFormat.MQL5, result.Format);
        Assert.NotEmpty(result.Code);
        Assert.NotEmpty(result.FileName);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("moving-average-crossover")]
    [InlineData("volatility-scaled-trend")]
    [InlineData("zscore-mean-reversion")]
    [InlineData("stationary-mean-reversion")]
    [InlineData("donchian-breakout")]
    [InlineData("macro-regime")]
    public async Task MQL5Exporter_BuiltInStrategy_ContainsRequiredStructure(string strategyType)
    {
        var exporter = new MQL5StrategyExporter();
        var version = CreateVersion(strategyType);

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Contains("CTrade", result.Code);
        Assert.Contains("OnTick()", result.Code);
    }

    [Fact]
    public async Task MQL5Exporter_UnsupportedStrategy_ReturnsEmptyCodeWithWarning()
    {
        var exporter = new MQL5StrategyExporter();
        var version = CreateVersion("custom-unknown-strategy");

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Equal(ExportFormat.MQL5, result.Format);
        Assert.Empty(result.Code);
        Assert.Single(result.Warnings);
        Assert.Contains("Unsupported", result.Warnings[0]);
    }

    [Fact]
    public async Task MQL5Exporter_MissingParameters_UsesDefaultsWithoutException()
    {
        var exporter = new MQL5StrategyExporter();
        var version = CreateVersion("moving-average-crossover", new Dictionary<string, object>());

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.NotEmpty(result.Code);
        Assert.Contains("FastPeriod = 12", result.Code);
        Assert.Contains("SlowPeriod = 26", result.Code);
    }

    #endregion

    #region PineScript Exporter Tests

    [Theory]
    [InlineData("moving-average-crossover")]
    [InlineData("volatility-scaled-trend")]
    [InlineData("zscore-mean-reversion")]
    [InlineData("stationary-mean-reversion")]
    [InlineData("donchian-breakout")]
    [InlineData("macro-regime")]
    public async Task PineScriptExporter_BuiltInStrategy_ProducesNonEmptyCode(string strategyType)
    {
        var exporter = new PineScriptExporter();
        var version = CreateVersion(strategyType);

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Equal(ExportFormat.PineScript, result.Format);
        Assert.NotEmpty(result.Code);
        Assert.NotEmpty(result.FileName);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("moving-average-crossover")]
    [InlineData("volatility-scaled-trend")]
    [InlineData("zscore-mean-reversion")]
    [InlineData("stationary-mean-reversion")]
    [InlineData("donchian-breakout")]
    [InlineData("macro-regime")]
    public async Task PineScriptExporter_BuiltInStrategy_ContainsRequiredStructure(string strategyType)
    {
        var exporter = new PineScriptExporter();
        var version = CreateVersion(strategyType);

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Contains("strategy(", result.Code);
        Assert.Contains("strategy.entry(", result.Code);
    }

    [Fact]
    public async Task PineScriptExporter_UnsupportedStrategy_ReturnsEmptyCodeWithWarning()
    {
        var exporter = new PineScriptExporter();
        var version = CreateVersion("custom-unknown-strategy");

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.Equal(ExportFormat.PineScript, result.Format);
        Assert.Empty(result.Code);
        Assert.Single(result.Warnings);
        Assert.Contains("Unsupported", result.Warnings[0]);
    }

    [Fact]
    public async Task PineScriptExporter_MissingParameters_UsesDefaultsWithoutException()
    {
        var exporter = new PineScriptExporter();
        var version = CreateVersion("moving-average-crossover", new Dictionary<string, object>());

        var result = await exporter.ExportAsync(version, CancellationToken.None);

        Assert.NotEmpty(result.Code);
        Assert.Contains("strategy(", result.Code);
    }

    #endregion
}
