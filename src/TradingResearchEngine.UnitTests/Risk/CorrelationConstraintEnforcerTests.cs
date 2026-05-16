using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Risk;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Portfolio;
using TradingResearchEngine.Core.Risk;

namespace TradingResearchEngine.UnitTests.Risk;

public class CorrelationConstraintEnforcerTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static CorrelationConstraintEnforcer CreateEnforcer(ICorrelationDataProvider provider) =>
        new(provider, NullLoggerFactory.Instance.CreateLogger<CorrelationConstraintEnforcer>());

    [Fact]
    public void Evaluate_NoExistingPositions_ReturnsAllowed()
    {
        var provider = new FakeCorrelationProvider(0.9m);
        var enforcer = CreateEnforcer(provider);

        var result = enforcer.Evaluate(
            "AAPL",
            new Dictionary<string, Position>(),
            maxPairwiseCorrelation: 0.7m,
            lookbackBars: 60);

        Assert.True(result.IsAllowed);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Evaluate_CorrelationWithinLimit_ReturnsAllowed()
    {
        var provider = new FakeCorrelationProvider(0.5m);
        var enforcer = CreateEnforcer(provider);

        var positions = new Dictionary<string, Position>
        {
            ["MSFT"] = new("MSFT", 10m, 150m, 0m, 0m)
        };

        var result = enforcer.Evaluate(
            "AAPL",
            positions,
            maxPairwiseCorrelation: 0.7m,
            lookbackBars: 60);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_CorrelationExceedsLimit_ReturnsRejected()
    {
        var provider = new FakeCorrelationProvider(0.85m);
        var enforcer = CreateEnforcer(provider);

        var positions = new Dictionary<string, Position>
        {
            ["MSFT"] = new("MSFT", 10m, 150m, 0m, 0m)
        };

        var result = enforcer.Evaluate(
            "AAPL",
            positions,
            maxPairwiseCorrelation: 0.7m,
            lookbackBars: 60);

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Reason);
        Assert.Contains("AAPL", result.Reason);
        Assert.Contains("MSFT", result.Reason);
        Assert.Equal("AAPL", result.CandidateSymbol);
        Assert.Equal("MSFT", result.ViolatingSymbol);
        Assert.Equal(0.85m, result.CorrelationValue);
    }

    [Fact]
    public void Evaluate_NegativeCorrelationExceedsLimit_ReturnsRejected()
    {
        var provider = new FakeCorrelationProvider(-0.85m);
        var enforcer = CreateEnforcer(provider);

        var positions = new Dictionary<string, Position>
        {
            ["MSFT"] = new("MSFT", 10m, 150m, 0m, 0m)
        };

        var result = enforcer.Evaluate(
            "AAPL",
            positions,
            maxPairwiseCorrelation: 0.7m,
            lookbackBars: 60);

        Assert.False(result.IsAllowed);
        Assert.Equal(-0.85m, result.CorrelationValue);
    }

    [Fact]
    public void Evaluate_SameSymbolAsCandidate_SkipsCorrelationCheck()
    {
        // If the candidate symbol is already in positions, it should be skipped
        var provider = new FakeCorrelationProvider(0.99m);
        var enforcer = CreateEnforcer(provider);

        var positions = new Dictionary<string, Position>
        {
            ["AAPL"] = new("AAPL", 10m, 150m, 0m, 0m)
        };

        var result = enforcer.Evaluate(
            "AAPL",
            positions,
            maxPairwiseCorrelation: 0.7m,
            lookbackBars: 60);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public void Evaluate_MultiplePositions_RejectsOnFirstViolation()
    {
        var provider = new SymbolPairCorrelationProvider(new Dictionary<(string, string), decimal>
        {
            [("AAPL", "MSFT")] = 0.3m,
            [("AAPL", "GOOG")] = 0.9m
        });
        var enforcer = CreateEnforcer(provider);

        var positions = new Dictionary<string, Position>
        {
            ["MSFT"] = new("MSFT", 10m, 150m, 0m, 0m),
            ["GOOG"] = new("GOOG", 5m, 200m, 0m, 0m)
        };

        var result = enforcer.Evaluate(
            "AAPL",
            positions,
            maxPairwiseCorrelation: 0.7m,
            lookbackBars: 60);

        Assert.False(result.IsAllowed);
        Assert.Equal("GOOG", result.ViolatingSymbol);
    }

    [Fact]
    public void DefaultRiskLayer_WithCorrelationEnforcer_RejectsHighCorrelation()
    {
        var provider = new FakeCorrelationProvider(0.9m);
        var enforcer = CreateEnforcer(provider);
        var portfolioRiskConfig = new PortfolioRiskConfig(
            MaxPairwiseCorrelation: 0.7m,
            CorrelationLookbackBars: 60);

        var layer = new DefaultRiskLayer(
            Options.Create(new RiskOptions { MaxExposurePercent = 100m }),
            NullLoggerFactory.Instance.CreateLogger<DefaultRiskLayer>(),
            correlationEnforcer: enforcer,
            portfolioRiskConfig: portfolioRiskConfig);

        var positions = new Dictionary<string, Position>
        {
            ["MSFT"] = new("MSFT", 10m, 150m, 0m, 0m)
        };
        var snapshot = new PortfolioSnapshot(positions, 90_000m, 100_000m);

        var order = new OrderEvent("AAPL", Direction.Long, 10m, OrderType.Market, null, T0);
        var result = layer.EvaluateOrder(order, snapshot);

        Assert.Null(result);
    }

    [Fact]
    public void DefaultRiskLayer_WithCorrelationEnforcer_AllowsLowCorrelation()
    {
        var provider = new FakeCorrelationProvider(0.3m);
        var enforcer = CreateEnforcer(provider);
        var portfolioRiskConfig = new PortfolioRiskConfig(
            MaxPairwiseCorrelation: 0.7m,
            CorrelationLookbackBars: 60);

        var layer = new DefaultRiskLayer(
            Options.Create(new RiskOptions { MaxExposurePercent = 100m }),
            NullLoggerFactory.Instance.CreateLogger<DefaultRiskLayer>(),
            correlationEnforcer: enforcer,
            portfolioRiskConfig: portfolioRiskConfig);

        var positions = new Dictionary<string, Position>
        {
            ["MSFT"] = new("MSFT", 10m, 150m, 0m, 0m)
        };
        var snapshot = new PortfolioSnapshot(positions, 90_000m, 100_000m);

        var order = new OrderEvent("AAPL", Direction.Long, 10m, OrderType.Market, null, T0);
        var result = layer.EvaluateOrder(order, snapshot);

        Assert.NotNull(result);
    }

    [Fact]
    public void DefaultRiskLayer_WithoutCorrelationEnforcer_SkipsCorrelationCheck()
    {
        // No enforcer injected — correlation check should be skipped
        var layer = new DefaultRiskLayer(
            Options.Create(new RiskOptions { MaxExposurePercent = 100m }),
            NullLoggerFactory.Instance.CreateLogger<DefaultRiskLayer>());

        var positions = new Dictionary<string, Position>
        {
            ["MSFT"] = new("MSFT", 10m, 150m, 0m, 0m)
        };
        var snapshot = new PortfolioSnapshot(positions, 90_000m, 100_000m);

        var order = new OrderEvent("AAPL", Direction.Long, 10m, OrderType.Market, null, T0);
        var result = layer.EvaluateOrder(order, snapshot);

        Assert.NotNull(result);
    }

    [Fact]
    public void DefaultRiskLayer_FlatDirection_SkipsCorrelationCheck()
    {
        var provider = new FakeCorrelationProvider(0.99m);
        var enforcer = CreateEnforcer(provider);
        var portfolioRiskConfig = new PortfolioRiskConfig(
            MaxPairwiseCorrelation: 0.5m,
            CorrelationLookbackBars: 60);

        var layer = new DefaultRiskLayer(
            Options.Create(new RiskOptions { MaxExposurePercent = 100m }),
            NullLoggerFactory.Instance.CreateLogger<DefaultRiskLayer>(),
            correlationEnforcer: enforcer,
            portfolioRiskConfig: portfolioRiskConfig);

        var positions = new Dictionary<string, Position>
        {
            ["AAPL"] = new("AAPL", 10m, 150m, 0m, 0m)
        };
        var snapshot = new PortfolioSnapshot(positions, 90_000m, 100_000m);

        // Flat orders should not be checked for correlation
        var order = new OrderEvent("AAPL", Direction.Flat, 10m, OrderType.Market, null, T0, true);
        var result = layer.EvaluateOrder(order, snapshot);

        Assert.NotNull(result);
    }

    /// <summary>Fake provider that returns a fixed correlation for all pairs.</summary>
    private sealed class FakeCorrelationProvider(decimal fixedCorrelation) : ICorrelationDataProvider
    {
        public decimal GetPairwiseCorrelation(string symbolA, string symbolB, int lookbackBars) =>
            fixedCorrelation;
    }

    /// <summary>Provider that returns specific correlations for specific symbol pairs.</summary>
    private sealed class SymbolPairCorrelationProvider(
        Dictionary<(string, string), decimal> correlations) : ICorrelationDataProvider
    {
        public decimal GetPairwiseCorrelation(string symbolA, string symbolB, int lookbackBars)
        {
            if (correlations.TryGetValue((symbolA, symbolB), out var corr))
                return corr;
            if (correlations.TryGetValue((symbolB, symbolA), out corr))
                return corr;
            return 0m;
        }
    }
}
