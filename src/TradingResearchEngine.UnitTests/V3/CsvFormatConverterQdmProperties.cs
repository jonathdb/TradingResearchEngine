using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TradingResearchEngine.Infrastructure.DataProviders;
using static TradingResearchEngine.Infrastructure.DataProviders.CsvFormatConverter;

namespace TradingResearchEngine.UnitTests.V3;

/// <summary>
/// Property-based tests for CsvFormatConverter QDM support.
/// **Validates: Requirements 1.2, 1.3, 2.1, 2.2, 2.3, 2.4, 2.5**
/// </summary>
public class CsvFormatConverterQdmProperties
{
    // Feature: qdm-folder-integration, Property 1: Format detection disambiguation
    /// <summary>
    /// For any CSV lines array where the header contains Date and Time columns (and not Timestamp),
    /// DetectFormat SHALL return QuantDataManager when the first data row uses dot separators,
    /// and MetaTrader when the first data row uses dash separators.
    /// **Validates: Requirements 1.2, 1.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property FormatDetection_DotMeansQdm_DashMeansMetaTrader()
    {
        var gen =
            from year in Gen.Choose(1970, 2030)
            from month in Gen.Choose(1, 12)
            from day in Gen.Choose(1, 28)
            from hour in Gen.Choose(0, 23)
            from minute in Gen.Choose(0, 59)
            from open in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from high in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from low in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from close in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from volume in Gen.Choose(0, 100000)
            from useDot in Gen.Elements(true, false)
            select (year, month, day, hour, minute, open, high, low, close, volume, useDot);

        return Prop.ForAll(
            gen.ToArbitrary(),
            t =>
            {
                var (year, month, day, hour, minute, open, high, low, close, volume, useDot) = t;
                var header = "Date,Time,Open,High,Low,Close,Volume";
                var datePart = useDot
                    ? $"{year:D4}.{month:D2}.{day:D2}"
                    : $"{year:D4}-{month:D2}-{day:D2}";
                var timePart = $"{hour:D2}:{minute:D2}";
                var dataRow = $"{datePart},{timePart},{open},{high},{low},{close},{volume}";
                var lines = new[] { header, dataRow };

                var result = CsvFormatConverter.DetectFormat(lines);
                var expected = useDot
                    ? SourceFormat.QuantDataManager
                    : SourceFormat.MetaTrader;

                return result == expected;
            });
    }

    // Feature: qdm-folder-integration, Property 2: QDM conversion timestamp round-trip
    /// <summary>
    /// For any valid QDM CSV row with date yyyy.MM.dd and time HH:mm,
    /// converting via Convert with SourceFormat.QuantDataManager and parsing the resulting
    /// Timestamp with DateTimeOffset.Parse using InvariantCulture SHALL produce a DateTimeOffset
    /// whose year, month, day, hour, and minute match the original input values.
    /// **Validates: Requirements 2.1, 2.2, 2.3, 3.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property QdmConversion_TimestampRoundTrip()
    {
        var gen =
            from year in Gen.Choose(1970, 2030)
            from month in Gen.Choose(1, 12)
            from day in Gen.Choose(1, 28)
            from hour in Gen.Choose(0, 23)
            from minute in Gen.Choose(0, 59)
            from open in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from high in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from low in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from close in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from volume in Gen.Choose(0, 100000)
            select (year, month, day, hour, minute, open, high, low, close, volume);

        return Prop.ForAll(
            gen.ToArbitrary(),
            t =>
            {
                var (year, month, day, hour, minute, open, high, low, close, volume) = t;
                var date = $"{year:D4}.{month:D2}.{day:D2}";
                var time = $"{hour:D2}:{minute:D2}";
                var header = "Date,Time,Open,High,Low,Close,Volume";
                var dataRow = $"{date},{time},{open},{high},{low},{close},{volume}";
                var csv = $"{header}\n{dataRow}";

                var output = CsvFormatConverter.Convert(csv, SourceFormat.QuantDataManager);
                var outputLines = output.Split('\n');
                if (outputLines.Length < 2) return false;

                var fields = outputLines[1].Split(',');
                var ts = DateTimeOffset.Parse(fields[0], CultureInfo.InvariantCulture);

                return ts.Year == year
                    && ts.Month == month
                    && ts.Day == day
                    && ts.Hour == hour
                    && ts.Minute == minute;
            });
    }

    // Feature: qdm-folder-integration, Property 3: QDM conversion OHLCV preservation
    /// <summary>
    /// For any valid QDM CSV row, the Open, High, Low, Close, and Volume values in the
    /// converted engine-format output SHALL be identical (string-equal after trimming)
    /// to the corresponding values in the source row.
    /// **Validates: Requirements 2.4**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property QdmConversion_OhlcvPreservation()
    {
        var gen =
            from year in Gen.Choose(1970, 2030)
            from month in Gen.Choose(1, 12)
            from day in Gen.Choose(1, 28)
            from hour in Gen.Choose(0, 23)
            from minute in Gen.Choose(0, 59)
            from open in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from high in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from low in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from close in Gen.Choose(1, 99999).Select(n => (decimal)n / 100m)
            from volume in Gen.Choose(0, 100000)
            select (year, month, day, hour, minute, open, high, low, close, volume);

        return Prop.ForAll(
            gen.ToArbitrary(),
            t =>
            {
                var (year, month, day, hour, minute, open, high, low, close, volume) = t;
                var date = $"{year:D4}.{month:D2}.{day:D2}";
                var time = $"{hour:D2}:{minute:D2}";
                var openStr = open.ToString(CultureInfo.InvariantCulture);
                var highStr = high.ToString(CultureInfo.InvariantCulture);
                var lowStr = low.ToString(CultureInfo.InvariantCulture);
                var closeStr = close.ToString(CultureInfo.InvariantCulture);
                var volStr = volume.ToString(CultureInfo.InvariantCulture);

                var header = "Date,Time,Open,High,Low,Close,Volume";
                var dataRow = $"{date},{time},{openStr},{highStr},{lowStr},{closeStr},{volStr}";
                var csv = $"{header}\n{dataRow}";

                var output = CsvFormatConverter.Convert(csv, SourceFormat.QuantDataManager);
                var outputLines = output.Split('\n');
                if (outputLines.Length < 2) return false;

                var fields = outputLines[1].Split(',');
                // fields[0] = Timestamp, fields[1..5] = O,H,L,C,V
                return fields[1].Trim() == openStr
                    && fields[2].Trim() == highStr
                    && fields[3].Trim() == lowStr
                    && fields[4].Trim() == closeStr
                    && fields[5].Trim() == volStr;
            });
    }

    // Feature: qdm-folder-integration, Property 4: Malformed line rejection
    /// <summary>
    /// For any CSV line containing fewer than seven comma-separated fields,
    /// ConvertLine with SourceFormat.QuantDataManager SHALL return null (skip the line).
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property MalformedLine_IsSkipped()
    {
        var fieldGen = Gen.Elements("abc", "123", "1.5", "2020.01.01", "00:00", "test", "42.7");
        var gen =
            from fieldCount in Gen.Choose(1, 6)
            from fields in fieldGen.ArrayOf(fieldCount)
            select string.Join(",", fields);

        return Prop.ForAll(
            gen.ToArbitrary(),
            malformedLine =>
            {
                var header = "Date,Time,Open,High,Low,Close,Volume";
                var csv = $"{header}\n{malformedLine}";

                var output = CsvFormatConverter.Convert(csv, SourceFormat.QuantDataManager);
                var outputLines = output.Split('\n');

                // Only the header should remain — the malformed line should be skipped
                return outputLines.Length == 1
                    && outputLines[0] == "Timestamp,Open,High,Low,Close,Volume";
            });
    }
}
