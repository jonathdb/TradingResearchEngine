using System.Text.Json;
using System.Text.Json.Serialization;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingResearchEngine.Application.AI;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Application.Strategies.Composite;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Infrastructure.AI;

namespace TradingResearchEngine.UnitTests.AI;

// Feature: trading-research-engine, Property 1: AI Strategy Draft JSON round-trip
// Feature: trading-research-engine, Property 2: Unknown strategy type triggers exactly one retry
// Feature: trading-engine-stories, Property 11: AIStrategyDraft JSON Round-Trip

/// <summary>
/// Property-based tests for AI Strategy Draft serialization and retry behaviour.
/// </summary>
public class AIStrategyDraftProperties
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new ObjectToInferredTypesConverter(), new JsonStringEnumConverter() }
    };

    private static readonly string[] KnownStrategyTypes =
    {
        "moving-average-crossover", "volatility-scaled-trend",
        "zscore-mean-reversion", "stationary-mean-reversion",
        "donchian-breakout", "macro-regime"
    };

    /// <summary>
    /// Property 1: AI Strategy Draft JSON round-trip.
    /// For any valid AIStrategyDraft, serializing to JSON and deserializing back
    /// produces a semantically equivalent object.
    /// **Validates: Requirements 1.1, 1.2**
    /// </summary>
    [Property(MaxTest = 20)]
    // Feature: trading-research-engine, Property 1: AI Strategy Draft JSON round-trip
    public Property AIStrategyDraft_JsonRoundTrip_ProducesEquivalentObject()
    {
        var gen =
            from nameIdx in Gen.Choose(0, 5)
            from hypothesisIdx in Gen.Choose(0, 4)
            from strategyTypeIdx in Gen.Choose(0, KnownStrategyTypes.Length - 1)
            from paramCount in Gen.Choose(0, 4)
            from initialCashIdx in Gen.Choose(0, 2)
            from riskFreeIdx in Gen.Choose(0, 2)
            from rationaleIdx in Gen.Choose(0, 4)
            from caveatCount in Gen.Choose(0, 3)
            select (nameIdx, hypothesisIdx, strategyTypeIdx, paramCount, initialCashIdx, riskFreeIdx, rationaleIdx, caveatCount);

        return Prop.ForAll(gen.ToArbitrary(), t =>
        {
            var names = new[] { "Alpha Strategy", "Mean Reversion", "Trend Following", "Breakout System", "Momentum Play", "Range Bound" };
            var hypotheses = new[] { "Markets mean-revert", "Trends persist", "Breakouts signal momentum", "Volatility clusters", "Regime changes" };
            var rationales = new[] { "Based on academic research", "Empirical evidence", "Statistical analysis", "Market microstructure", "Behavioural finance" };
            var caveats = new[] { "May underperform in trending markets", "Requires sufficient liquidity", "Sensitive to parameter choice" };
            var initialCashes = new[] { 50_000m, 100_000m, 200_000m };
            var riskFreeRates = new[] { 0.02m, 0.05m, 0.08m };
            var paramKeys = new[] { "period", "fastPeriod", "slowPeriod", "threshold", "lookback" };

            var parameters = new Dictionary<string, object>();
            for (int i = 0; i < t.paramCount && i < paramKeys.Length; i++)
            {
                parameters[paramKeys[i]] = (i + 1) * 10;
            }

            var caveatList = new List<string>();
            for (int i = 0; i < t.caveatCount && i < caveats.Length; i++)
            {
                caveatList.Add(caveats[i]);
            }

            var draft = new AIStrategyDraft(
                StrategyName: names[t.nameIdx],
                Hypothesis: hypotheses[t.hypothesisIdx],
                StrategyType: KnownStrategyTypes[t.strategyTypeIdx],
                Parameters: parameters,
                SuggestedRisk: new RiskConfig(
                    new Dictionary<string, object> { ["maxRiskPercent"] = 2.0 },
                    initialCashes[t.initialCashIdx],
                    riskFreeRates[t.riskFreeIdx]),
                Rationale: rationales[t.rationaleIdx],
                Caveats: caveatList,
                SourceType: SourceType.AIGenerated);

            // Serialize to JSON
            var json = JsonSerializer.Serialize(draft, JsonOptions);

            // Deserialize back
            var deserialized = JsonSerializer.Deserialize<AIStrategyDraftDto>(json, JsonOptions);

            // Verify semantic equivalence
            Assert.NotNull(deserialized);
            Assert.Equal(draft.StrategyName, deserialized!.StrategyName);
            Assert.Equal(draft.Hypothesis, deserialized.Hypothesis);
            Assert.Equal(draft.StrategyType, deserialized.StrategyType);
            Assert.Equal(draft.Rationale, deserialized.Rationale);
            Assert.Equal(draft.SourceType.ToString(), deserialized.SourceType);
            Assert.Equal(draft.Caveats.Count, deserialized.Caveats!.Count);

            for (int i = 0; i < draft.Caveats.Count; i++)
            {
                Assert.Equal(draft.Caveats[i], deserialized.Caveats[i]);
            }

            // Verify parameters round-trip (keys preserved)
            Assert.Equal(draft.Parameters.Count, deserialized.Parameters!.Count);
            foreach (var key in draft.Parameters.Keys)
            {
                Assert.True(deserialized.Parameters.ContainsKey(key),
                    $"Parameter key '{key}' missing after round-trip");
            }

            // Verify risk config
            Assert.NotNull(deserialized.SuggestedRisk);
            Assert.Equal(draft.SuggestedRisk.InitialCash, deserialized.SuggestedRisk!.InitialCash);
            Assert.Equal(draft.SuggestedRisk.AnnualRiskFreeRate, deserialized.SuggestedRisk.AnnualRiskFreeRate);
        });
    }

    /// <summary>
    /// Property 2: Unknown strategy type triggers exactly one retry.
    /// For any strategy type string not in KnownNames, the assistant issues exactly one retry.
    /// **Validates: Requirements 1.5, 1.6**
    /// </summary>
    [Property(MaxTest = 20)]
    // Feature: trading-research-engine, Property 2: Unknown strategy type triggers exactly one retry
    public Property UnknownStrategyType_TriggersExactlyOneRetry()
    {
        var unknownTypes = new[]
        {
            "unknown-type", "fake-strategy", "nonexistent-algo",
            "random-walk", "neural-net-predictor", "quantum-trading",
            "ai-magic", "super-trend-v99", "deep-learning-alpha"
        };

        var gen = from idx in Gen.Choose(0, unknownTypes.Length - 1)
                  select unknownTypes[idx];

        return Prop.ForAll(gen.ToArbitrary(), unknownType =>
        {
            // Arrange
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(tempFile, "System prompt for testing.");

                var mockClient = new Mock<IGeminiClient>();
                var registry = new StrategyRegistry();
                registry.RegisterAssembly(typeof(Core.Strategy.IStrategy).Assembly);

                var callCount = 0;
                var unknownJson = BuildJsonWithStrategyType(unknownType);

                mockClient
                    .Setup(c => c.GenerateJsonAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(() =>
                    {
                        callCount++;
                        return unknownJson;
                    });

                var options = Options.Create(new GeminiOptions
                {
                    ApiKey = "test-key",
                    SystemPromptFilePath = tempFile
                });

                var assistant = new GeminiStrategyAssistant(
                    options, registry,
                    NullLogger<GeminiStrategyAssistant>.Instance,
                    mockClient.Object);

                // Act
                var result = assistant.GenerateStrategyAsync("test prompt", CancellationToken.None)
                    .GetAwaiter().GetResult();

                // Assert: exactly 2 calls (initial + one retry)
                Assert.Equal(2, callCount);

                // The retry prompt should contain known strategy names
                mockClient.Verify(
                    c => c.GenerateJsonAsync(
                        It.IsAny<string>(),
                        It.Is<string>(msg => msg.Contains(unknownType) && msg.Contains("known strategy types")),
                        It.IsAny<CancellationToken>()),
                    Times.Once);

                // Result should have a caveat about unrecognised type
                Assert.Contains(result.Caveats, c => c.Contains("Unrecognised strategy type"));
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        });
    }

    /// <summary>
    /// Property 11: AIStrategyDraft JSON Round-Trip.
    /// For any valid AIStrategyDraft (including RefinementHistory), serializing to JSON
    /// and deserializing back produces an equivalent object with all fields preserved.
    /// **Validates: Requirements 21.6**
    /// </summary>
    [Property(MaxTest = 100)]
    // Feature: trading-engine-stories, Property 11: AIStrategyDraft JSON Round-Trip
    public Property AIStrategyDraft_WithRefinementHistory_JsonRoundTrip_ProducesEquivalentObject()
    {
        var strategyNames = new[] { "Alpha Strategy", "Mean Reversion", "Trend Following", "Breakout System", "Momentum Play", "Range Bound" };
        var hypotheses = new[] { "Markets mean-revert", "Trends persist", "Breakouts signal momentum", "Volatility clusters", "Regime changes" };
        var rationales = new[] { "Based on academic research", "Empirical evidence", "Statistical analysis", "Market microstructure", "Behavioural finance" };
        var caveatOptions = new[] { "May underperform in trending markets", "Requires sufficient liquidity", "Sensitive to parameter choice", "High drawdown risk", "Needs large sample" };
        var refinementOptions = new[] { "Increase lookback period", "Add stop loss", "Reduce position size", "Filter by volatility regime", "Add trend confirmation", "Use tighter entry" };
        var paramKeys = new[] { "period", "fastPeriod", "slowPeriod", "threshold", "lookback" };

        var gen =
            from nameIdx in Gen.Choose(0, strategyNames.Length - 1)
            from hypothesisIdx in Gen.Choose(0, hypotheses.Length - 1)
            from strategyTypeIdx in Gen.Choose(0, KnownStrategyTypes.Length - 1)
            from paramCount in Gen.Choose(0, 4)
            from initialCash in Gen.Elements(50_000m, 100_000m, 200_000m)
            from riskFreeRate in Gen.Elements(0.02m, 0.05m, 0.08m)
            from rationaleIdx in Gen.Choose(0, rationales.Length - 1)
            from caveatCount in Gen.Choose(0, 4)
            from refinementCount in Gen.Choose(0, 5)
            from includeComposite in Gen.Elements(true, false)
            select (nameIdx, hypothesisIdx, strategyTypeIdx, paramCount, initialCash, riskFreeRate, rationaleIdx, caveatCount, refinementCount, includeComposite);

        return Prop.ForAll(gen.ToArbitrary(), t =>
        {
            // Build parameters with simple string/decimal values to avoid JsonElement comparison issues
            var parameters = new Dictionary<string, object>();
            for (int i = 0; i < t.paramCount && i < paramKeys.Length; i++)
            {
                parameters[paramKeys[i]] = ((i + 1) * 10).ToString();
            }

            // Build caveats
            var caveats = new List<string>();
            for (int i = 0; i < t.caveatCount && i < caveatOptions.Length; i++)
            {
                caveats.Add(caveatOptions[i]);
            }

            // Build refinement history
            var refinementHistory = new List<string>();
            for (int i = 0; i < t.refinementCount && i < refinementOptions.Length; i++)
            {
                refinementHistory.Add(refinementOptions[i]);
            }

            // Build risk config with string values in RiskParameters
            var riskParams = new Dictionary<string, object>
            {
                ["maxRiskPercent"] = "2.0",
                ["maxPositions"] = "5"
            };
            var suggestedRisk = new RiskConfig(riskParams, t.initialCash, t.riskFreeRate);

            // Optionally include CompositeConfig
            CompositeStrategyConfig? compositeConfig = null;
            if (t.includeComposite)
            {
                compositeConfig = new CompositeStrategyConfig(
                    Name: "TestComposite",
                    Indicators: new List<IndicatorConfig>
                    {
                        new IndicatorConfig("sma20", "sma", new Dictionary<string, object> { ["period"] = "20" }),
                        new IndicatorConfig("rsi14", "rsi", new Dictionary<string, object> { ["period"] = "14" })
                    },
                    EntryCondition: "sma20 > price",
                    ExitCondition: "rsi14 > 70");
            }

            var draft = new AIStrategyDraft(
                StrategyName: strategyNames[t.nameIdx],
                Hypothesis: hypotheses[t.hypothesisIdx],
                StrategyType: KnownStrategyTypes[t.strategyTypeIdx],
                Parameters: parameters,
                SuggestedRisk: suggestedRisk,
                Rationale: rationales[t.rationaleIdx],
                Caveats: caveats,
                CompositeConfig: compositeConfig,
                SourceType: SourceType.AIGenerated,
                RefinementHistory: refinementHistory);

            // Serialize to JSON
            var json = JsonSerializer.Serialize(draft, JsonOptions);

            // Deserialize back
            var deserialized = JsonSerializer.Deserialize<AIStrategyDraftFullDto>(json, JsonOptions);

            // Verify all fields preserved
            Assert.NotNull(deserialized);
            Assert.Equal(draft.StrategyName, deserialized!.StrategyName);
            Assert.Equal(draft.Hypothesis, deserialized.Hypothesis);
            Assert.Equal(draft.StrategyType, deserialized.StrategyType);
            Assert.Equal(draft.Rationale, deserialized.Rationale);
            Assert.Equal(draft.SourceType.ToString(), deserialized.SourceType);

            // Verify parameters
            Assert.Equal(draft.Parameters.Count, deserialized.Parameters!.Count);
            foreach (var kvp in draft.Parameters)
            {
                Assert.True(deserialized.Parameters.ContainsKey(kvp.Key),
                    $"Parameter key '{kvp.Key}' missing after round-trip");
                Assert.Equal(kvp.Value.ToString(), deserialized.Parameters[kvp.Key]?.ToString());
            }

            // Verify risk config
            Assert.NotNull(deserialized.SuggestedRisk);
            Assert.Equal(draft.SuggestedRisk.InitialCash, deserialized.SuggestedRisk!.InitialCash);
            Assert.Equal(draft.SuggestedRisk.AnnualRiskFreeRate, deserialized.SuggestedRisk.AnnualRiskFreeRate);
            Assert.Equal(draft.SuggestedRisk.RiskParameters.Count, deserialized.SuggestedRisk.RiskParameters!.Count);
            foreach (var kvp in draft.SuggestedRisk.RiskParameters)
            {
                Assert.True(deserialized.SuggestedRisk.RiskParameters.ContainsKey(kvp.Key));
                Assert.Equal(kvp.Value.ToString(), deserialized.SuggestedRisk.RiskParameters[kvp.Key]?.ToString());
            }

            // Verify caveats
            Assert.Equal(draft.Caveats.Count, deserialized.Caveats!.Count);
            for (int i = 0; i < draft.Caveats.Count; i++)
            {
                Assert.Equal(draft.Caveats[i], deserialized.Caveats[i]);
            }

            // Verify refinement history
            Assert.Equal(draft.RefinementHistory.Count, deserialized.RefinementHistory!.Count);
            for (int i = 0; i < draft.RefinementHistory.Count; i++)
            {
                Assert.Equal(draft.RefinementHistory[i], deserialized.RefinementHistory[i]);
            }

            // Verify composite config if present
            if (draft.CompositeConfig is not null)
            {
                Assert.NotNull(deserialized.CompositeConfig);
                Assert.Equal(draft.CompositeConfig.Name, deserialized.CompositeConfig!.Name);
                Assert.Equal(draft.CompositeConfig.EntryCondition, deserialized.CompositeConfig.EntryCondition);
                Assert.Equal(draft.CompositeConfig.ExitCondition, deserialized.CompositeConfig.ExitCondition);
                Assert.Equal(draft.CompositeConfig.DirectionMode.ToString(), deserialized.CompositeConfig.DirectionMode);
                Assert.Equal(draft.CompositeConfig.Indicators.Count, deserialized.CompositeConfig.Indicators!.Count);
                for (int i = 0; i < draft.CompositeConfig.Indicators.Count; i++)
                {
                    Assert.Equal(draft.CompositeConfig.Indicators[i].Id, deserialized.CompositeConfig.Indicators[i].Id);
                    Assert.Equal(draft.CompositeConfig.Indicators[i].Type, deserialized.CompositeConfig.Indicators[i].Type);
                }
            }
            else
            {
                Assert.Null(deserialized.CompositeConfig);
            }
        });
    }

    private static string BuildJsonWithStrategyType(string strategyType)
    {
        var dto = new
        {
            strategyName = "Generated Strategy",
            hypothesis = "Test hypothesis",
            strategyType = strategyType,
            parameters = new Dictionary<string, object> { ["period"] = 20 },
            suggestedRisk = new
            {
                riskParameters = new Dictionary<string, object> { ["maxRisk"] = 2.0 },
                initialCash = 100000m,
                annualRiskFreeRate = 0.05m
            },
            rationale = "Test rationale",
            caveats = new[] { "Test caveat" }
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// DTO for deserialization verification in round-trip test.
    /// </summary>
    private sealed class AIStrategyDraftDto
    {
        public string? StrategyName { get; set; }
        public string? Hypothesis { get; set; }
        public string? StrategyType { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
        public RiskConfigDto? SuggestedRisk { get; set; }
        public string? Rationale { get; set; }
        public List<string>? Caveats { get; set; }
        public string? SourceType { get; set; }
    }

    /// <summary>
    /// Full DTO for Property 11 round-trip test including RefinementHistory and CompositeConfig.
    /// </summary>
    private sealed class AIStrategyDraftFullDto
    {
        public string? StrategyName { get; set; }
        public string? Hypothesis { get; set; }
        public string? StrategyType { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
        public RiskConfigDto? SuggestedRisk { get; set; }
        public string? Rationale { get; set; }
        public List<string>? Caveats { get; set; }
        public string? SourceType { get; set; }
        public CompositeConfigDto? CompositeConfig { get; set; }
        public List<string>? RefinementHistory { get; set; }
    }

    /// <summary>
    /// DTO for CompositeStrategyConfig deserialization in round-trip test.
    /// </summary>
    private sealed class CompositeConfigDto
    {
        public string? Name { get; set; }
        public List<IndicatorConfigDto>? Indicators { get; set; }
        public string? EntryCondition { get; set; }
        public string? ExitCondition { get; set; }
        public string? DirectionMode { get; set; }
    }

    /// <summary>
    /// DTO for IndicatorConfig deserialization in round-trip test.
    /// </summary>
    private sealed class IndicatorConfigDto
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
    }

    private sealed class RiskConfigDto
    {
        public Dictionary<string, object>? RiskParameters { get; set; }
        public decimal InitialCash { get; set; }
        public decimal AnnualRiskFreeRate { get; set; }
    }

    /// <summary>
    /// Custom JSON converter that infers types for object values during deserialization.
    /// Handles the System.Text.Json limitation where object properties deserialize as JsonElement.
    /// </summary>
    private sealed class ObjectToInferredTypesConverter : JsonConverter<object>
    {
        public override object? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.True => true,
                JsonTokenType.False => false,
                JsonTokenType.Number when reader.TryGetInt32(out var i) => i,
                JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
                JsonTokenType.Number => reader.GetDouble(),
                JsonTokenType.String => reader.GetString()!,
                _ => JsonDocument.ParseValue(ref reader).RootElement.Clone()
            };
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }
}
