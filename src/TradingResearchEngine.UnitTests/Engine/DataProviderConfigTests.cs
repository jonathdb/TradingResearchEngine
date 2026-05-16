using System.Text.Json;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.UnitTests.Engine;

/// <summary>
/// Tests for the <see cref="DataProviderConfig"/> discriminated union type,
/// <see cref="DataProviderConfigConverter"/> JSON serialization, and
/// <see cref="DataProviderConfigAdapter"/> backward compatibility.
/// </summary>
public sealed class DataProviderConfigTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void CsvConfig_RoundTrips_ThroughJson()
    {
        var config = new CsvDataProviderConfig
        {
            Symbol = "SPY",
            Interval = "1D",
            FilePath = "samples/data/spy-daily.csv",
            DateFormat = "yyyy-MM-dd",
            HasHeader = true
        };

        var json = JsonSerializer.Serialize<DataProviderConfig>(config, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DataProviderConfig>(json, JsonOptions);

        Assert.NotNull(deserialized);
        var csv = Assert.IsType<CsvDataProviderConfig>(deserialized);
        Assert.Equal("SPY", csv.Symbol);
        Assert.Equal("1D", csv.Interval);
        Assert.Equal("samples/data/spy-daily.csv", csv.FilePath);
        Assert.Equal("yyyy-MM-dd", csv.DateFormat);
        Assert.True(csv.HasHeader);
    }

    [Fact]
    public void HttpConfig_RoundTrips_ThroughJson()
    {
        var config = new HttpDataProviderConfig
        {
            Symbol = "AAPL",
            Interval = "H4",
            BaseUrl = "https://api.example.com/data",
            ApiKey = "test-key",
            TimeoutSeconds = 60
        };

        var json = JsonSerializer.Serialize<DataProviderConfig>(config, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DataProviderConfig>(json, JsonOptions);

        Assert.NotNull(deserialized);
        var http = Assert.IsType<HttpDataProviderConfig>(deserialized);
        Assert.Equal("AAPL", http.Symbol);
        Assert.Equal("H4", http.Interval);
        Assert.Equal("https://api.example.com/data", http.BaseUrl);
        Assert.Equal("test-key", http.ApiKey);
        Assert.Equal(60, http.TimeoutSeconds);
    }

    [Fact]
    public void DukascopyConfig_RoundTrips_ThroughJson()
    {
        var config = new DukascopyDataProviderConfig
        {
            Symbol = "EURUSD",
            Interval = "M15",
            CacheDirectory = "data/custom-cache",
            CacheTtlHours = 48
        };

        var json = JsonSerializer.Serialize<DataProviderConfig>(config, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DataProviderConfig>(json, JsonOptions);

        Assert.NotNull(deserialized);
        var dukascopy = Assert.IsType<DukascopyDataProviderConfig>(deserialized);
        Assert.Equal("EURUSD", dukascopy.Symbol);
        Assert.Equal("M15", dukascopy.Interval);
        Assert.Equal("data/custom-cache", dukascopy.CacheDirectory);
        Assert.Equal(48, dukascopy.CacheTtlHours);
    }

    [Fact]
    public void LegacyDictionary_WithFilePath_DeserializesAsCsv()
    {
        // Simulates legacy JSON format without $type discriminator
        var legacyJson = """
        {
            "Symbol": "SPY",
            "Interval": "1D",
            "FilePath": "samples/data/spy-daily.csv"
        }
        """;

        var deserialized = JsonSerializer.Deserialize<DataProviderConfig>(legacyJson, JsonOptions);

        Assert.NotNull(deserialized);
        var csv = Assert.IsType<CsvDataProviderConfig>(deserialized);
        Assert.Equal("SPY", csv.Symbol);
        Assert.Equal("1D", csv.Interval);
        Assert.Equal("samples/data/spy-daily.csv", csv.FilePath);
    }

    [Fact]
    public void LegacyDictionary_WithBaseUrl_DeserializesAsHttp()
    {
        var legacyJson = """
        {
            "Symbol": "AAPL",
            "BaseUrl": "https://api.example.com/data",
            "Timeout": 45
        }
        """;

        var deserialized = JsonSerializer.Deserialize<DataProviderConfig>(legacyJson, JsonOptions);

        Assert.NotNull(deserialized);
        var http = Assert.IsType<HttpDataProviderConfig>(deserialized);
        Assert.Equal("AAPL", http.Symbol);
        Assert.Equal("https://api.example.com/data", http.BaseUrl);
        Assert.Equal(45, http.TimeoutSeconds);
    }

    [Fact]
    public void LegacyDictionary_WithCacheDirectory_DeserializesAsDukascopy()
    {
        var legacyJson = """
        {
            "Symbol": "EURUSD",
            "CacheDirectory": "data/my-cache",
            "CacheTtl": 12
        }
        """;

        var deserialized = JsonSerializer.Deserialize<DataProviderConfig>(legacyJson, JsonOptions);

        Assert.NotNull(deserialized);
        var dukascopy = Assert.IsType<DukascopyDataProviderConfig>(deserialized);
        Assert.Equal("EURUSD", dukascopy.Symbol);
        Assert.Equal("data/my-cache", dukascopy.CacheDirectory);
        Assert.Equal(12, dukascopy.CacheTtlHours);
    }

    [Fact]
    public void Adapter_FromDictionary_Csv_ConvertsCorrectly()
    {
        var dict = new Dictionary<string, object>
        {
            ["Symbol"] = "SPY",
            ["Interval"] = "1D",
            ["FilePath"] = "data/spy.csv",
            ["DateFormat"] = "MM/dd/yyyy",
            ["HasHeader"] = true
        };

        var config = DataProviderConfigAdapter.FromDictionary("csv", dict);

        var csv = Assert.IsType<CsvDataProviderConfig>(config);
        Assert.Equal("SPY", csv.Symbol);
        Assert.Equal("1D", csv.Interval);
        Assert.Equal("data/spy.csv", csv.FilePath);
        Assert.Equal("MM/dd/yyyy", csv.DateFormat);
        Assert.True(csv.HasHeader);
    }

    [Fact]
    public void Adapter_FromDictionary_Http_ConvertsCorrectly()
    {
        var dict = new Dictionary<string, object>
        {
            ["Symbol"] = "AAPL",
            ["BaseUrl"] = "https://api.example.com",
            ["ApiKey"] = "secret",
            ["Timeout"] = 45
        };

        var config = DataProviderConfigAdapter.FromDictionary("http", dict);

        var http = Assert.IsType<HttpDataProviderConfig>(config);
        Assert.Equal("AAPL", http.Symbol);
        Assert.Equal("https://api.example.com", http.BaseUrl);
        Assert.Equal("secret", http.ApiKey);
        Assert.Equal(45, http.TimeoutSeconds);
    }

    [Fact]
    public void Adapter_FromDictionary_Dukascopy_ConvertsCorrectly()
    {
        var dict = new Dictionary<string, object>
        {
            ["Symbol"] = "EURUSD",
            ["CacheDirectory"] = "data/cache",
            ["CacheTtl"] = 48
        };

        var config = DataProviderConfigAdapter.FromDictionary("dukascopy", dict);

        var dukascopy = Assert.IsType<DukascopyDataProviderConfig>(config);
        Assert.Equal("EURUSD", dukascopy.Symbol);
        Assert.Equal("data/cache", dukascopy.CacheDirectory);
        Assert.Equal(48, dukascopy.CacheTtlHours);
    }

    [Fact]
    public void Adapter_ToDictionary_Csv_ProducesExpectedKeys()
    {
        var config = new CsvDataProviderConfig
        {
            Symbol = "SPY",
            Interval = "1D",
            FilePath = "data/spy.csv",
            DateFormat = "MM/dd/yyyy",
            HasHeader = false
        };

        var dict = DataProviderConfigAdapter.ToDictionary(config);

        Assert.Equal("SPY", dict["Symbol"]);
        Assert.Equal("1D", dict["Interval"]);
        Assert.Equal("data/spy.csv", dict["FilePath"]);
        Assert.Equal("MM/dd/yyyy", dict["DateFormat"]);
        Assert.Equal(false, dict["HasHeader"]);
    }

    [Fact]
    public void Adapter_ToDictionary_Http_ProducesExpectedKeys()
    {
        var config = new HttpDataProviderConfig
        {
            Symbol = "AAPL",
            BaseUrl = "https://api.example.com",
            ApiKey = "key",
            TimeoutSeconds = 60
        };

        var dict = DataProviderConfigAdapter.ToDictionary(config);

        Assert.Equal("AAPL", dict["Symbol"]);
        Assert.Equal("https://api.example.com", dict["BaseUrl"]);
        Assert.Equal("key", dict["ApiKey"]);
        Assert.Equal(60, dict["Timeout"]);
    }

    [Fact]
    public void Adapter_RoundTrip_DictionaryToConfigToDictionary_PreservesValues()
    {
        var originalDict = new Dictionary<string, object>
        {
            ["Symbol"] = "SPY",
            ["Interval"] = "1D",
            ["FilePath"] = "samples/data/spy-daily.csv"
        };

        var config = DataProviderConfigAdapter.FromDictionary("csv", originalDict);
        var roundTripped = DataProviderConfigAdapter.ToDictionary(config);

        Assert.Equal("SPY", roundTripped["Symbol"]);
        Assert.Equal("1D", roundTripped["Interval"]);
        Assert.Equal("samples/data/spy-daily.csv", roundTripped["FilePath"]);
    }

    [Fact]
    public void DataConfig_EffectiveTypedConfig_ReturnsTypedWhenPresent()
    {
        var typedConfig = new CsvDataProviderConfig
        {
            Symbol = "SPY",
            FilePath = "data/spy.csv"
        };

#pragma warning disable CS0618
        var dataConfig = new DataConfig("csv", new Dictionary<string, object>(), "Daily", 252, typedConfig);
#pragma warning restore CS0618

        Assert.Same(typedConfig, dataConfig.EffectiveTypedConfig);
    }

    [Fact]
    public void DataConfig_EffectiveTypedConfig_ConvertsFromDictionaryWhenTypedIsNull()
    {
        var dict = new Dictionary<string, object>
        {
            ["FilePath"] = "data/spy.csv",
            ["Symbol"] = "SPY"
        };

#pragma warning disable CS0618
        var dataConfig = new DataConfig("csv", dict, "Daily", 252);
#pragma warning restore CS0618

        var effective = dataConfig.EffectiveTypedConfig;
        var csv = Assert.IsType<CsvDataProviderConfig>(effective);
        Assert.Equal("data/spy.csv", csv.FilePath);
        Assert.Equal("SPY", csv.Symbol);
    }

    [Fact]
    public void ProviderType_Property_ReturnsCorrectDiscriminator()
    {
        Assert.Equal("csv", new CsvDataProviderConfig().ProviderType);
        Assert.Equal("http", new HttpDataProviderConfig().ProviderType);
        Assert.Equal("dukascopy", new DukascopyDataProviderConfig().ProviderType);
    }

    [Fact]
    public void CsvConfig_Defaults_AreCorrect()
    {
        var config = new CsvDataProviderConfig();

        Assert.Equal("", config.FilePath);
        Assert.Equal("yyyy-MM-dd", config.DateFormat);
        Assert.True(config.HasHeader);
        Assert.Null(config.Symbol);
        Assert.Null(config.Interval);
        Assert.Null(config.From);
        Assert.Null(config.To);
    }

    [Fact]
    public void HttpConfig_Defaults_AreCorrect()
    {
        var config = new HttpDataProviderConfig();

        Assert.Equal("", config.BaseUrl);
        Assert.Equal("", config.ApiKey);
        Assert.Equal(30, config.TimeoutSeconds);
        Assert.Null(config.Symbol);
        Assert.Null(config.Interval);
    }

    [Fact]
    public void DukascopyConfig_Defaults_AreCorrect()
    {
        var config = new DukascopyDataProviderConfig();

        Assert.Equal("data/dukascopy-cache", config.CacheDirectory);
        Assert.Equal(24, config.CacheTtlHours);
        Assert.Null(config.Symbol);
        Assert.Null(config.Interval);
    }

    [Fact]
    public void Serialized_Json_Contains_TypeDiscriminator()
    {
        var config = new CsvDataProviderConfig { FilePath = "test.csv" };
        var json = JsonSerializer.Serialize<DataProviderConfig>(config, JsonOptions);

        Assert.Contains("\"$type\": \"csv\"", json);
    }

    [Fact]
    public void Adapter_FromDictionary_EmptyDictionary_ReturnsDefaults()
    {
        var dict = new Dictionary<string, object>();

        var config = DataProviderConfigAdapter.FromDictionary("csv", dict);

        var csv = Assert.IsType<CsvDataProviderConfig>(config);
        Assert.Equal("", csv.FilePath);
        Assert.Equal("yyyy-MM-dd", csv.DateFormat);
        Assert.True(csv.HasHeader);
    }

    [Fact]
    public void Adapter_FromDictionary_UnknownProviderType_DefaultsToCsv()
    {
        var dict = new Dictionary<string, object> { ["FilePath"] = "test.csv" };

        var config = DataProviderConfigAdapter.FromDictionary("unknown", dict);

        var csv = Assert.IsType<CsvDataProviderConfig>(config);
        Assert.Equal("test.csv", csv.FilePath);
    }

    [Fact]
    public void Config_WithDateRange_SerializesAndDeserializes()
    {
        var from = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2023, 12, 31, 0, 0, 0, TimeSpan.Zero);

        var config = new CsvDataProviderConfig
        {
            Symbol = "SPY",
            From = from,
            To = to,
            FilePath = "data.csv"
        };

        var json = JsonSerializer.Serialize<DataProviderConfig>(config, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<DataProviderConfig>(json, JsonOptions);

        Assert.NotNull(deserialized);
        var csv = Assert.IsType<CsvDataProviderConfig>(deserialized);
        Assert.Equal(from, csv.From);
        Assert.Equal(to, csv.To);
    }
}
