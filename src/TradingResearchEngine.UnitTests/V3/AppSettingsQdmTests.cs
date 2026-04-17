using System.Text.Json;
using TradingResearchEngine.Infrastructure.Settings;

namespace TradingResearchEngine.UnitTests.V3;

public class AppSettingsQdmTests
{
    [Fact]
    public void AppSettings_Default_QdmWatchDirectoryIsNull()
    {
        var defaults = AppSettings.Default;

        Assert.Null(defaults.QdmWatchDirectory);
    }

    [Fact]
    public void AppSettings_OldJsonWithoutQdmField_DeserializesWithNull()
    {
        // Simulate a settings JSON from before QdmWatchDirectory was added
        var oldJson = """
        {
            "DataDirectory": "data",
            "ExportDirectory": "exports",
            "DefaultRealismProfile": 1,
            "DefaultInitialCash": 100000,
            "DefaultRiskFreeRate": 0.02,
            "DefaultSizingPolicy": "PercentEquity"
        }
        """;

        var opts = new JsonSerializerOptions { WriteIndented = true };
        var settings = JsonSerializer.Deserialize<AppSettings>(oldJson, opts);

        Assert.NotNull(settings);
        Assert.Null(settings!.QdmWatchDirectory);
        Assert.Equal("data", settings.DataDirectory);
        Assert.Equal("exports", settings.ExportDirectory);
    }
}
