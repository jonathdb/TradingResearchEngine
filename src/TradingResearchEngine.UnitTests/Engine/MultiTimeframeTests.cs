using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Core.DataHandling;
using TradingResearchEngine.Core.Engine;
using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Exceptions;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Results;
using TradingResearchEngine.Core.Risk;
using TradingResearchEngine.Core.Strategy;

namespace TradingResearchEngine.UnitTests.Engine;

/// <summary>
/// Tests for multi-timeframe strategy support including:
/// - Secondary timeframe bars delivered chronologically before primary bars
/// - Validation of secondary data sources before execution
/// - Structured error when secondary source is unavailable
/// </summary>
public class MultiTimeframeTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    #region Test Helpers

    private static ScenarioConfig CreateConfig(
        IReadOnlyList<SecondaryTimeframeConfig>? secondaryTimeframes = null) => new(
        ScenarioId: "multi-tf-test",
        Description: "Multi-timeframe test",
        ReplayMode: ReplayMode.Bar,
        DataProviderType: "Mock",
        DataProviderOptions: new Dictionary<string, object>
        {
            ["Symbol"] = "TEST",
            ["Interval"] = "1D",
            ["From"] = T0.ToString("o"),
            ["To"] = T0.AddDays(30).ToString("o")
        },
        StrategyType: "test",
        StrategyParameters: new Dictionary<string, object>(),
        RiskParameters: new Dictionary<string, object>(),
        SlippageModelType: "Zero",
        CommissionModelType: "Zero",
        InitialCash: 100_000m,
        AnnualRiskFreeRate: 0m,
        RandomSeed: null,
        ResearchWorkflowType: null,
        ResearchWorkflowOptions: null,
        PropFirmOptions: null,
        FillMode: FillMode.NextBarOpen,
        SecondaryTimeframes: secondaryTimeframes);

    private static IDataProvider CreateDataProvider(params BarRecord[] bars)
    {
        var mock = new Mock<IDataProvider>();
        mock.Setup(p => p.GetBars(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerable(bars));
        return mock.Object;
    }

    private static async IAsyncEnumerable<BarRecord> ToAsyncEnumerable(BarRecord[] bars)
    {
        foreach (var bar in bars)
        {
            yield return bar;
            await Task.CompletedTask;
        }
    }

    private static IRiskLayer CreatePassThroughRiskLayer()
    {
        var mock = new Mock<IRiskLayer>();
        mock.Setup(r => r.EvaluateOrder(It.IsAny<OrderEvent>(), It.IsAny<PortfolioSnapshot>()))
            .Returns((OrderEvent o, PortfolioSnapshot _) => o);
        mock.Setup(r => r.ConvertSignal(It.IsAny<SignalEvent>(), It.IsAny<PortfolioSnapshot>()))
            .Returns((SignalEvent s, PortfolioSnapshot _) => null);
        return mock.Object;
    }

    private static IExecutionHandler CreateZeroSlippageExecutionHandler()
    {
        var mock = new Mock<IExecutionHandler>();
        mock.Setup(h => h.Execute(It.IsAny<OrderEvent>(), It.IsAny<MarketDataEvent>()))
            .Returns((OrderEvent order, MarketDataEvent mde) =>
            {
                decimal price = mde is BarEvent bar ? bar.Close : 0m;
                var fill = new FillEvent(
                    order.Symbol,
                    order.Direction,
                    order.Quantity,
                    price,
                    Commission: 0m,
                    SlippageAmount: 0m,
                    mde.Timestamp);
                return new ExecutionResult(ExecutionOutcome.Filled, fill);
            });
        return mock.Object;
    }

    private static IDataProviderFactory CreateDataProviderFactory(
        Dictionary<string, BarRecord[]>? timeframeBars = null)
    {
        var mock = new Mock<IDataProviderFactory>();
        mock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns((string providerType, Dictionary<string, object> options) =>
            {
                if (timeframeBars is not null)
                {
                    // Use the Interval from options or the providerType as key
                    var interval = options.TryGetValue("Interval", out var i) ? i?.ToString() ?? providerType : providerType;
                    if (timeframeBars.TryGetValue(interval, out var bars))
                        return CreateDataProvider(bars);
                }
                return CreateDataProvider();
            });
        return mock.Object;
    }

    private static IDataProviderFactory CreateFailingDataProviderFactory(string failingTimeframe)
    {
        var mock = new Mock<IDataProviderFactory>();
        mock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>()))
            .Returns((string providerType, Dictionary<string, object> options) =>
            {
                var interval = options.TryGetValue("Interval", out var i) ? i?.ToString() : null;
                if (interval == failingTimeframe || providerType == failingTimeframe)
                    throw new InvalidOperationException($"Data source '{failingTimeframe}' is unavailable.");
                return CreateDataProvider();
            });
        return mock.Object;
    }

    private static BacktestEngine CreateEngine(
        IDataProvider dataProvider,
        IStrategy strategy,
        IDataProviderFactory? dataProviderFactory = null)
    {
        var logger = NullLoggerFactory.Instance.CreateLogger<BacktestEngine>();
        return new BacktestEngine(
            dataProvider, strategy, CreatePassThroughRiskLayer(),
            CreateZeroSlippageExecutionHandler(), logger,
            NullLoggerFactory.Instance,
            dataProviderFactory: dataProviderFactory);
    }

    #endregion

    #region Test Strategies

    /// <summary>
    /// A multi-timeframe strategy that records all secondary bars received
    /// and the order in which they arrive relative to primary bars.
    /// </summary>
    private sealed class RecordingMultiTimeframeStrategy : IMultiTimeframeStrategy
    {
        public List<(string Timeframe, BarRecord Bar)> SecondaryBarsReceived { get; } = new();
        public List<(string EventType, DateTimeOffset Timestamp)> EventOrder { get; } = new();

        public void OnSecondaryBar(string timeframe, BarRecord bar)
        {
            SecondaryBarsReceived.Add((timeframe, bar));
            EventOrder.Add(($"Secondary:{timeframe}", bar.Timestamp));
        }

        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            EventOrder.Add(("Primary", evt.Timestamp));
            return Array.Empty<EngineEvent>();
        }
    }

    /// <summary>
    /// A simple non-multi-timeframe strategy for testing that the engine
    /// works normally when SecondaryTimeframes is configured but strategy doesn't implement IMultiTimeframeStrategy.
    /// </summary>
    private sealed class SimpleStrategy : IStrategy
    {
        public IReadOnlyList<EngineEvent> OnMarketData(MarketDataEvent evt)
        {
            return Array.Empty<EngineEvent>();
        }
    }

    #endregion

    #region Multi-Timeframe Data Handler Tests

    [Fact]
    public void ValidateDataSources_EmptyTimeframe_ReturnsError()
    {
        // Arrange
        var factory = CreateDataProviderFactory();
        var handler = new MultiTimeframeDataHandler(factory, NullLoggerFactory.Instance.CreateLogger<MultiTimeframeDataHandler>());
        var configs = new List<SecondaryTimeframeConfig>
        {
            new("", "csv", new Dictionary<string, object>())
        };

        // Act
        var errors = handler.ValidateDataSources(configs);

        // Assert
        Assert.Single(errors);
        Assert.Contains("empty Timeframe", errors[0]);
    }

    [Fact]
    public void ValidateDataSources_EmptyProviderType_ReturnsError()
    {
        // Arrange
        var factory = CreateDataProviderFactory();
        var handler = new MultiTimeframeDataHandler(factory, NullLoggerFactory.Instance.CreateLogger<MultiTimeframeDataHandler>());
        var configs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "", new Dictionary<string, object>())
        };

        // Act
        var errors = handler.ValidateDataSources(configs);

        // Assert
        Assert.Single(errors);
        Assert.Contains("H4", errors[0]);
        Assert.Contains("empty DataProviderType", errors[0]);
    }

    [Fact]
    public void ValidateDataSources_UnavailableProvider_ReturnsError()
    {
        // Arrange
        var factory = CreateFailingDataProviderFactory("H4");
        var handler = new MultiTimeframeDataHandler(factory, NullLoggerFactory.Instance.CreateLogger<MultiTimeframeDataHandler>());
        var configs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "csv", new Dictionary<string, object> { ["Interval"] = "H4" })
        };

        // Act
        var errors = handler.ValidateDataSources(configs);

        // Assert
        Assert.Single(errors);
        Assert.Contains("H4", errors[0]);
        Assert.Contains("unavailable", errors[0]);
    }

    [Fact]
    public void ValidateDataSources_ValidConfig_ReturnsNoErrors()
    {
        // Arrange
        var factory = CreateDataProviderFactory();
        var handler = new MultiTimeframeDataHandler(factory, NullLoggerFactory.Instance.CreateLogger<MultiTimeframeDataHandler>());
        var configs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "csv", new Dictionary<string, object> { ["Interval"] = "H4" })
        };

        // Act
        var errors = handler.ValidateDataSources(configs);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GetSecondaryBarsBeforeAsync_DeliversBarsChronologically()
    {
        // Arrange: secondary H4 bars at T0, T0+6h, T0+12h, T0+18h
        var h4Bars = new[]
        {
            new BarRecord("TEST", "H4", 100m, 101m, 99m, 100.5m, 1000m, T0),
            new BarRecord("TEST", "H4", 100.5m, 102m, 100m, 101m, 1000m, T0.AddHours(6)),
            new BarRecord("TEST", "H4", 101m, 103m, 100.5m, 102m, 1000m, T0.AddHours(12)),
            new BarRecord("TEST", "H4", 102m, 104m, 101m, 103m, 1000m, T0.AddHours(18)),
        };

        var timeframeBars = new Dictionary<string, BarRecord[]> { ["H4"] = h4Bars };
        var factory = CreateDataProviderFactory(timeframeBars);
        var handler = new MultiTimeframeDataHandler(factory, NullLoggerFactory.Instance.CreateLogger<MultiTimeframeDataHandler>());

        var configs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "csv", new Dictionary<string, object> { ["Interval"] = "H4" })
        };

        handler.ValidateDataSources(configs);
        await handler.InitializeAsync(configs, new Dictionary<string, object>
        {
            ["Symbol"] = "TEST",
            ["From"] = T0.ToString("o"),
            ["To"] = T0.AddDays(1).ToString("o")
        });

        // Act: get bars before T0+1 day (should get all 4 H4 bars)
        var bars = await handler.GetSecondaryBarsBeforeAsync(T0.AddDays(1));

        // Assert
        Assert.Equal(4, bars.Count);
        for (int i = 1; i < bars.Count; i++)
        {
            Assert.True(bars[i].Bar.Timestamp >= bars[i - 1].Bar.Timestamp,
                "Secondary bars must be in chronological order");
        }
    }

    [Fact]
    public async Task GetSecondaryBarsBeforeAsync_OnlyDeliversBarsBeforeTimestamp()
    {
        // Arrange: secondary bars at T0, T0+6h, T0+12h, T0+18h
        var h4Bars = new[]
        {
            new BarRecord("TEST", "H4", 100m, 101m, 99m, 100.5m, 1000m, T0),
            new BarRecord("TEST", "H4", 100.5m, 102m, 100m, 101m, 1000m, T0.AddHours(6)),
            new BarRecord("TEST", "H4", 101m, 103m, 100.5m, 102m, 1000m, T0.AddHours(12)),
            new BarRecord("TEST", "H4", 102m, 104m, 101m, 103m, 1000m, T0.AddHours(18)),
        };

        var timeframeBars = new Dictionary<string, BarRecord[]> { ["H4"] = h4Bars };
        var factory = CreateDataProviderFactory(timeframeBars);
        var handler = new MultiTimeframeDataHandler(factory, NullLoggerFactory.Instance.CreateLogger<MultiTimeframeDataHandler>());

        var configs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "csv", new Dictionary<string, object> { ["Interval"] = "H4" })
        };

        handler.ValidateDataSources(configs);
        await handler.InitializeAsync(configs, new Dictionary<string, object>
        {
            ["Symbol"] = "TEST",
            ["From"] = T0.ToString("o"),
            ["To"] = T0.AddDays(1).ToString("o")
        });

        // Act: get bars before T0+10h (should get only first 2 bars: T0 and T0+6h)
        var bars = await handler.GetSecondaryBarsBeforeAsync(T0.AddHours(10));

        // Assert
        Assert.Equal(2, bars.Count);
        Assert.Equal(T0, bars[0].Bar.Timestamp);
        Assert.Equal(T0.AddHours(6), bars[1].Bar.Timestamp);
    }

    #endregion

    #region Engine Integration Tests

    [Fact]
    public async Task RunAsync_MultiTimeframeStrategy_ReceivesSecondaryBarsBeforePrimary()
    {
        // Arrange: Primary daily bars at T0, T0+1d, T0+2d
        // Secondary H4 bars at T0, T0+6h, T0+12h, T0+18h, T0+1d, T0+1d+6h
        var primaryBars = new[]
        {
            new BarRecord("TEST", "1D", 100m, 105m, 95m, 102m, 1000m, T0),
            new BarRecord("TEST", "1D", 102m, 107m, 97m, 104m, 1000m, T0.AddDays(1)),
            new BarRecord("TEST", "1D", 104m, 109m, 99m, 106m, 1000m, T0.AddDays(2)),
        };

        var h4Bars = new[]
        {
            new BarRecord("TEST", "H4", 100m, 101m, 99m, 100.5m, 500m, T0),
            new BarRecord("TEST", "H4", 100.5m, 102m, 100m, 101m, 500m, T0.AddHours(6)),
            new BarRecord("TEST", "H4", 101m, 103m, 100.5m, 102m, 500m, T0.AddHours(12)),
            new BarRecord("TEST", "H4", 102m, 104m, 101m, 103m, 500m, T0.AddHours(18)),
            new BarRecord("TEST", "H4", 103m, 105m, 102m, 104m, 500m, T0.AddDays(1)),
            new BarRecord("TEST", "H4", 104m, 106m, 103m, 105m, 500m, T0.AddDays(1).AddHours(6)),
        };

        var timeframeBars = new Dictionary<string, BarRecord[]> { ["H4"] = h4Bars };
        var factory = CreateDataProviderFactory(timeframeBars);

        var strategy = new RecordingMultiTimeframeStrategy();
        var secondaryConfigs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "csv", new Dictionary<string, object> { ["Interval"] = "H4" })
        };

        var config = CreateConfig(secondaryConfigs);
        var engine = CreateEngine(CreateDataProvider(primaryBars), strategy, factory);

        // Act
        var result = await engine.RunAsync(config);

        // Assert
        Assert.Equal(BacktestStatus.Completed, result.Status);

        // Strategy should have received secondary bars
        Assert.True(strategy.SecondaryBarsReceived.Count > 0,
            "Multi-timeframe strategy should receive secondary bars");

        // All secondary bars should have timeframe "H4"
        Assert.All(strategy.SecondaryBarsReceived, sb => Assert.Equal("H4", sb.Timeframe));

        // Verify chronological ordering: secondary bars before their corresponding primary bar
        // For each primary bar event, all preceding secondary events should have timestamps <= primary timestamp
        var events = strategy.EventOrder;
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].EventType == "Primary")
            {
                // All preceding secondary events should have timestamps <= this primary timestamp
                for (int j = 0; j < i; j++)
                {
                    if (events[j].EventType.StartsWith("Secondary"))
                    {
                        Assert.True(events[j].Timestamp <= events[i].Timestamp,
                            $"Secondary bar at {events[j].Timestamp} should be <= primary bar at {events[i].Timestamp}");
                    }
                }
            }
        }
    }

    [Fact]
    public async Task RunAsync_NonMultiTimeframeStrategy_WithSecondaryConfig_CompletesNormally()
    {
        // Arrange: A regular strategy with secondary timeframes configured
        // The engine should still work — it just won't deliver secondary bars
        var primaryBars = new[]
        {
            new BarRecord("TEST", "1D", 100m, 105m, 95m, 102m, 1000m, T0),
            new BarRecord("TEST", "1D", 102m, 107m, 97m, 104m, 1000m, T0.AddDays(1)),
        };

        var h4Bars = new[]
        {
            new BarRecord("TEST", "H4", 100m, 101m, 99m, 100.5m, 500m, T0),
        };

        var timeframeBars = new Dictionary<string, BarRecord[]> { ["H4"] = h4Bars };
        var factory = CreateDataProviderFactory(timeframeBars);

        var strategy = new SimpleStrategy();
        var secondaryConfigs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "csv", new Dictionary<string, object> { ["Interval"] = "H4" })
        };

        var config = CreateConfig(secondaryConfigs);
        var engine = CreateEngine(CreateDataProvider(primaryBars), strategy, factory);

        // Act
        var result = await engine.RunAsync(config);

        // Assert: Completes without error even though strategy doesn't implement IMultiTimeframeStrategy
        Assert.Equal(BacktestStatus.Completed, result.Status);
    }

    [Fact]
    public async Task RunAsync_MissingSecondaryDataSource_ThrowsConfigurationException()
    {
        // Arrange: Secondary timeframe configured but provider factory throws
        var primaryBars = new[]
        {
            new BarRecord("TEST", "1D", 100m, 105m, 95m, 102m, 1000m, T0),
        };

        var factory = CreateFailingDataProviderFactory("H4");

        var strategy = new RecordingMultiTimeframeStrategy();
        var secondaryConfigs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "csv", new Dictionary<string, object> { ["Interval"] = "H4" })
        };

        var config = CreateConfig(secondaryConfigs);
        var engine = CreateEngine(CreateDataProvider(primaryBars), strategy, factory);

        // Act & Assert: Should throw ConfigurationException with structured error
        var ex = await Assert.ThrowsAsync<ConfigurationException>(() => engine.RunAsync(config));
        Assert.Contains("H4", ex.Message);
        Assert.Contains("unavailable", ex.Message);
    }

    [Fact]
    public async Task RunAsync_NoSecondaryTimeframes_WorksNormally()
    {
        // Arrange: No secondary timeframes configured
        var primaryBars = new[]
        {
            new BarRecord("TEST", "1D", 100m, 105m, 95m, 102m, 1000m, T0),
            new BarRecord("TEST", "1D", 102m, 107m, 97m, 104m, 1000m, T0.AddDays(1)),
        };

        var strategy = new SimpleStrategy();
        var config = CreateConfig(secondaryTimeframes: null);
        var engine = CreateEngine(CreateDataProvider(primaryBars), strategy);

        // Act
        var result = await engine.RunAsync(config);

        // Assert
        Assert.Equal(BacktestStatus.Completed, result.Status);
    }

    [Fact]
    public async Task RunAsync_SecondaryTimeframesWithNoFactory_ThrowsConfigurationException()
    {
        // Arrange: Secondary timeframes configured but no IDataProviderFactory provided
        var primaryBars = new[]
        {
            new BarRecord("TEST", "1D", 100m, 105m, 95m, 102m, 1000m, T0),
        };

        var strategy = new RecordingMultiTimeframeStrategy();
        var secondaryConfigs = new List<SecondaryTimeframeConfig>
        {
            new("H4", "csv", new Dictionary<string, object> { ["Interval"] = "H4" })
        };

        var config = CreateConfig(secondaryConfigs);
        // No dataProviderFactory passed
        var engine = CreateEngine(CreateDataProvider(primaryBars), strategy, dataProviderFactory: null);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ConfigurationException>(() => engine.RunAsync(config));
        Assert.Contains("IDataProviderFactory", ex.Message);
    }

    #endregion
}
