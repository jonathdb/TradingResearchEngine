using System.Text.Json;
using TradingResearchEngine.Application.Configuration;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.Research;

/// <summary>
/// Verifies backward compatibility of CompositeParameterGrid persistence on
/// WalkForwardOptions and SweepOptions using System.Text.Json default behaviour.
/// Requirements: 21.1, 21.2, 21.3
/// </summary>
public class CompositeGridPersistenceTests
{
    // Matches JsonFileRepository: WriteIndented = true, no custom converters
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Fact]
    public void WalkForwardOptions_WithoutCompositeGrid_DeserializesAsNull()
    {
        // JSON from an older version that does not include CompositeGrid
        var json = """
        {
            "InSampleLength": "90.00:00:00",
            "OutOfSampleLength": "30.00:00:00",
            "StepSize": "30.00:00:00",
            "AnchoredWindow": false
        }
        """;

        var options = JsonSerializer.Deserialize<WalkForwardOptions>(json, JsonOptions);

        Assert.NotNull(options);
        Assert.Null(options!.CompositeGrid);
    }

    [Fact]
    public void SweepOptions_WithoutCompositeGrid_DeserializesAsNull()
    {
        // JSON from an older version that does not include CompositeGrid
        // SortBy uses numeric enum value (0 = SharpeRatio) matching JsonFileRepository behaviour
        var json = """
        {
            "MaxDegreeOfParallelism": 4,
            "SortBy": 0
        }
        """;

        var options = JsonSerializer.Deserialize<SweepOptions>(json, JsonOptions);

        Assert.NotNull(options);
        Assert.Null(options!.CompositeGrid);
    }

    [Fact]
    public void WalkForwardOptions_WithCompositeGrid_DeserializesCorrectly()
    {
        var json = """
        {
            "InSampleLength": "90.00:00:00",
            "OutOfSampleLength": "30.00:00:00",
            "StepSize": "30.00:00:00",
            "AnchoredWindow": false,
            "CompositeGrid": {
                "Ranges": [
                    {
                        "IndicatorId": "sma-1",
                        "ParameterName": "Period",
                        "Start": 10,
                        "End": 50,
                        "Step": 5
                    }
                ]
            }
        }
        """;

        var options = JsonSerializer.Deserialize<WalkForwardOptions>(json, JsonOptions);

        Assert.NotNull(options);
        Assert.NotNull(options!.CompositeGrid);
        Assert.Single(options.CompositeGrid!.Ranges);
        Assert.Equal("sma-1", options.CompositeGrid.Ranges[0].IndicatorId);
        Assert.Equal("Period", options.CompositeGrid.Ranges[0].ParameterName);
        Assert.Equal(10m, options.CompositeGrid.Ranges[0].Start);
        Assert.Equal(50m, options.CompositeGrid.Ranges[0].End);
        Assert.Equal(5m, options.CompositeGrid.Ranges[0].Step);
    }

    [Fact]
    public void SweepOptions_WithCompositeGrid_DeserializesCorrectly()
    {
        var json = """
        {
            "MaxDegreeOfParallelism": 8,
            "SortBy": 0,
            "CompositeGrid": {
                "Ranges": [
                    {
                        "IndicatorId": "rsi-1",
                        "ParameterName": "Length",
                        "Start": 7,
                        "End": 21,
                        "Step": 7
                    }
                ]
            }
        }
        """;

        var options = JsonSerializer.Deserialize<SweepOptions>(json, JsonOptions);

        Assert.NotNull(options);
        Assert.NotNull(options!.CompositeGrid);
        Assert.Single(options.CompositeGrid!.Ranges);
        Assert.Equal("rsi-1", options.CompositeGrid.Ranges[0].IndicatorId);
    }

    [Fact]
    public void WalkForwardOptions_UnknownProperties_IgnoredOnDeserialization()
    {
        // Simulates an older version loading JSON with the new CompositeGrid field
        // System.Text.Json default behaviour ignores unknown properties
        var json = """
        {
            "InSampleLength": "90.00:00:00",
            "OutOfSampleLength": "30.00:00:00",
            "StepSize": "30.00:00:00",
            "AnchoredWindow": false,
            "CompositeGrid": { "Ranges": [] },
            "SomeCompletelyUnknownField": "should be ignored"
        }
        """;

        var options = JsonSerializer.Deserialize<WalkForwardOptions>(json, JsonOptions);

        Assert.NotNull(options);
        // The unknown field is silently ignored — no exception thrown
        Assert.Equal(TimeSpan.FromDays(90), options!.InSampleLength);
    }

    [Fact]
    public void SweepOptions_UnknownProperties_IgnoredOnDeserialization()
    {
        // Simulates an older version loading JSON with the new CompositeGrid field
        var json = """
        {
            "MaxDegreeOfParallelism": 4,
            "SortBy": 0,
            "CompositeGrid": { "Ranges": [] },
            "FutureField": 42
        }
        """;

        var options = JsonSerializer.Deserialize<SweepOptions>(json, JsonOptions);

        Assert.NotNull(options);
        Assert.Equal(4, options!.MaxDegreeOfParallelism);
    }

    [Fact]
    public void WalkForwardOptions_CompositeGrid_RoundTrips()
    {
        var original = new WalkForwardOptions
        {
            InSampleLength = TimeSpan.FromDays(90),
            OutOfSampleLength = TimeSpan.FromDays(30),
            StepSize = TimeSpan.FromDays(30),
            CompositeGrid = new CompositeParameterGrid(new[]
            {
                new CompositeParameterRange("ema-1", "Period", 5m, 25m, 5m),
                new CompositeParameterRange("atr-1", "Multiplier", 1.5m, 3.0m, 0.5m)
            })
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<WalkForwardOptions>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.CompositeGrid);
        Assert.Equal(2, deserialized.CompositeGrid!.Ranges.Count);
        Assert.Equal("ema-1", deserialized.CompositeGrid.Ranges[0].IndicatorId);
        Assert.Equal("atr-1", deserialized.CompositeGrid.Ranges[1].IndicatorId);
    }

    [Fact]
    public void SweepOptions_CompositeGrid_RoundTrips()
    {
        var original = new SweepOptions
        {
            MaxDegreeOfParallelism = 8,
            SortBy = SweepSortMetric.ProfitFactor,
            CompositeGrid = new CompositeParameterGrid(new[]
            {
                new CompositeParameterRange("bb-1", "StdDev", 1.0m, 3.0m, 0.5m)
            })
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<SweepOptions>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized!.CompositeGrid);
        Assert.Single(deserialized.CompositeGrid!.Ranges);
        Assert.Equal("bb-1", deserialized.CompositeGrid.Ranges[0].IndicatorId);
        Assert.Equal(SweepSortMetric.ProfitFactor, deserialized.SortBy);
    }
}
