using System.Globalization;
using TradingResearchEngine.Infrastructure.DataProviders;
using static TradingResearchEngine.Infrastructure.DataProviders.CsvFormatConverter;

namespace TradingResearchEngine.UnitTests.V3;

public class CsvFormatConverterQdmTests
{
    [Fact]
    public void DetectFormat_QdmHeader_ReturnsQuantDataManager()
    {
        var lines = new[]
        {
            "Date,Time,Open,High,Low,Close,Volume",
            "2020.01.02,00:00,1.12100,1.12250,1.12000,1.12190,1234"
        };

        var result = CsvFormatConverter.DetectFormat(lines);

        Assert.Equal(SourceFormat.QuantDataManager, result);
    }

    [Fact]
    public void DetectFormat_Mt5Header_StillReturnsMetaTrader()
    {
        var lines = new[]
        {
            "Date,Time,Open,High,Low,Close,Volume",
            "2020-01-02,00:00:00,1.12100,1.12250,1.12000,1.12190,1234"
        };

        var result = CsvFormatConverter.DetectFormat(lines);

        Assert.Equal(SourceFormat.MetaTrader, result);
    }

    [Fact]
    public void DetectFormat_SingleLineOnly_DefaultsToMetaTrader()
    {
        var lines = new[] { "Date,Time,Open,High,Low,Close,Volume" };

        var result = CsvFormatConverter.DetectFormat(lines);

        Assert.Equal(SourceFormat.MetaTrader, result);
    }

    [Fact]
    public void ConvertQuantDataManager_KnownRow_ProducesCorrectTimestamp()
    {
        var csv = "Date,Time,Open,High,Low,Close,Volume\n" +
                  "2020.01.02,14:30,1.12100,1.12250,1.12000,1.12190,1234";

        var output = CsvFormatConverter.Convert(csv, SourceFormat.QuantDataManager);
        var lines = output.Split('\n');

        Assert.Equal(2, lines.Length);
        var dataLine = lines[1];
        var fields = dataLine.Split(',');
        var ts = DateTimeOffset.Parse(fields[0], CultureInfo.InvariantCulture);

        Assert.Equal(2020, ts.Year);
        Assert.Equal(1, ts.Month);
        Assert.Equal(2, ts.Day);
        Assert.Equal(14, ts.Hour);
        Assert.Equal(30, ts.Minute);
        Assert.Equal(0, ts.Second);
        Assert.Equal(TimeSpan.Zero, ts.Offset);
    }

    [Fact]
    public void Convert_QdmFile_OutputHeaderIsEngineFormat()
    {
        var csv = "Date,Time,Open,High,Low,Close,Volume\n" +
                  "2020.01.02,00:00,1.12100,1.12250,1.12000,1.12190,1234\n" +
                  "2020.01.03,01:00,1.13000,1.13500,1.12900,1.13200,5678";

        var output = CsvFormatConverter.Convert(csv, SourceFormat.QuantDataManager);
        var header = output.Split('\n')[0];

        Assert.Equal("Timestamp,Open,High,Low,Close,Volume", header);
    }

    [Fact]
    public void ConvertLine_QdmMalformedRow_ReturnsNull()
    {
        // ConvertLine is private, so we test via Convert — a malformed row should be skipped
        var csv = "Date,Time,Open,High,Low,Close,Volume\n" +
                  "2020.01.02,00:00,1.12100,1.12250,1.12000,1.12190,1234\n" +
                  "2020.01.03,01:00,1.13000";  // only 3 fields — malformed

        var output = CsvFormatConverter.Convert(csv, SourceFormat.QuantDataManager);
        var lines = output.Split('\n');

        // Header + 1 valid row; malformed row skipped
        Assert.Equal(2, lines.Length);
        Assert.Equal("Timestamp,Open,High,Low,Close,Volume", lines[0]);
    }
}
