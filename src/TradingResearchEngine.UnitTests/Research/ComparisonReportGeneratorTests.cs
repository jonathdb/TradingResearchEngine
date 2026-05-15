using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.UnitTests.Research;

public class ComparisonReportGeneratorTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _tempDir;
    private readonly ComparisonReportGenerator _generator;

    public ComparisonReportGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"comparison_test_{Guid.NewGuid():N}");
        var options = Options.Create(new ComparisonReportOptions
        {
            OutputDirectory = _tempDir,
            EnableHtml = false
        });
        _generator = new ComparisonReportGenerator(options, NullLogger<ComparisonReportGenerator>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task GenerateAsync_FewerThanTwoResults_ThrowsArgumentException()
    {
        var single = new List<BacktestResult> { MakeResult("scenario-a") };

        await Assert.ThrowsAsync<ArgumentException>(() => _generator.GenerateAsync(single));
    }

    [Fact]
    public async Task GenerateAsync_TwoResults_PersistsMarkdownFile()
    {
        var results = new List<BacktestResult>
        {
            MakeResult("scenario-a", sharpe: 1.5m, maxDd: 0.10m),
            MakeResult("scenario-b", sharpe: 2.0m, maxDd: 0.20m)
        };

        var artifact = await _generator.GenerateAsync(results);

        Assert.True(File.Exists(artifact.OutputPath));
        Assert.Contains("Strategy Comparison Report", artifact.MarkdownContent);
        Assert.Null(artifact.HtmlContent);
    }

    [Fact]
    public async Task GenerateAsync_WithHtmlEnabled_ProducesHtmlContent()
    {
        var options = Options.Create(new ComparisonReportOptions
        {
            OutputDirectory = _tempDir,
            EnableHtml = true
        });
        var generator = new ComparisonReportGenerator(options, NullLogger<ComparisonReportGenerator>.Instance);

        var results = new List<BacktestResult>
        {
            MakeResult("scenario-a"),
            MakeResult("scenario-b")
        };

        var artifact = await generator.GenerateAsync(results);

        Assert.NotNull(artifact.HtmlContent);
        Assert.Contains("<!DOCTYPE html>", artifact.HtmlContent);
        Assert.Contains("Strategy Comparison Report", artifact.HtmlContent);
    }

    [Fact]
    public void RenderMarkdown_ContainsAllScenarioIds()
    {
        var results = new List<BacktestResult>
        {
            MakeResult("alpha-strategy"),
            MakeResult("beta-strategy"),
            MakeResult("gamma-strategy")
        };

        var markdown = _generator.RenderMarkdown(results);

        Assert.Contains("alpha-strategy", markdown);
        Assert.Contains("beta-strategy", markdown);
        Assert.Contains("gamma-strategy", markdown);
    }

    [Fact]
    public void RenderMarkdown_ContainsKeyMetricsSections()
    {
        var results = new List<BacktestResult>
        {
            MakeResult("a", sharpe: 1.2m, maxDd: 0.08m),
            MakeResult("b", sharpe: 0.9m, maxDd: 0.15m)
        };

        var markdown = _generator.RenderMarkdown(results);

        Assert.Contains("## Key Metrics", markdown);
        Assert.Contains("## Extended Statistics", markdown);
        Assert.Contains("## Equity Curve Summary", markdown);
        Assert.Contains("## Configuration", markdown);
        Assert.Contains("## Summary", markdown);
    }

    [Fact]
    public void RenderMarkdown_IdentifiesBestBySharpeAndDrawdown()
    {
        var results = new List<BacktestResult>
        {
            MakeResult("low-sharpe", sharpe: 0.5m, maxDd: 0.05m),
            MakeResult("high-sharpe", sharpe: 2.5m, maxDd: 0.25m)
        };

        var markdown = _generator.RenderMarkdown(results);

        Assert.Contains("**Best by Sharpe:** high-sharpe", markdown);
        Assert.Contains("**Best by Drawdown:** low-sharpe", markdown);
    }

    [Fact]
    public async Task GenerateAsync_OutputPathContainsTimestamp()
    {
        var results = new List<BacktestResult>
        {
            MakeResult("a"),
            MakeResult("b")
        };

        var artifact = await _generator.GenerateAsync(results);

        Assert.Contains("comparison_", Path.GetFileName(artifact.OutputPath));
        Assert.EndsWith(".md", artifact.OutputPath);
    }

    [Fact]
    public void RenderMarkdown_IncludesEquityCurvePointCount()
    {
        var equityCurve = new List<EquityCurvePoint>
        {
            new(T0, 100_000m),
            new(T0.AddDays(1), 101_000m),
            new(T0.AddDays(2), 102_000m)
        };

        var results = new List<BacktestResult>
        {
            MakeResult("with-curve", equityCurve: equityCurve),
            MakeResult("other")
        };

        var markdown = _generator.RenderMarkdown(results);

        Assert.Contains("3", markdown); // 3 equity curve points
    }

    private static BacktestResult MakeResult(
        string scenarioId = "test",
        decimal sharpe = 1.0m,
        decimal maxDd = 0.05m,
        IReadOnlyList<EquityCurvePoint>? equityCurve = null) =>
        new(Guid.NewGuid(),
            new ScenarioConfig(scenarioId, "Test", ReplayMode.Bar, "csv",
                new Dictionary<string, object>(), "test-strategy", new Dictionary<string, object>(),
                new Dictionary<string, object>(), "Zero", "Zero", 100_000m, 0.02m, null, null, null, null),
            BacktestStatus.Completed,
            equityCurve ?? new List<EquityCurvePoint> { new(T0, 100_000m) },
            new List<ClosedTrade>(),
            100_000m, 105_000m, maxDd, sharpe, sharpe, null, null, 10, 0.6m, 1.5m, 200m, -100m, 10m, null, null, 3, 5, 50);
}
