using System.Globalization;
using TradingResearchEngine.Infrastructure.Settings;

namespace TradingResearchEngine.Infrastructure.DataProviders;

/// <summary>Metadata about a CSV data file.</summary>
public sealed record DataFileInfo(
    string FileName,
    string FullPath,
    long FileSizeBytes,
    int RowCount,
    string DetectedFormat,
    string? FirstTimestamp,
    string? LastTimestamp,
    string[] Headers);

/// <summary>Manages CSV data files in a configured data directory.</summary>
public sealed class DataFileService
{
    private readonly string _dataDir;
    private readonly string? _qdmWatchDir;
    private readonly SettingsService? _settingsService;

    public DataFileService(string? dataDir = null, string? qdmWatchDir = null, SettingsService? settingsService = null)
    {
        _dataDir = dataDir ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        _qdmWatchDir = qdmWatchDir;
        _settingsService = settingsService;
        if (!Directory.Exists(_dataDir)) Directory.CreateDirectory(_dataDir);
    }

    
    /// <summary>Returns the base data directory path.</summary>
    public string DataDirectory => _dataDir;

    /// <summary>Lists all CSV files in the data directory with metadata.</summary>
    public List<DataFileInfo> ListFiles()
    {
        var files = new List<DataFileInfo>();
        foreach (var path in Directory.GetFiles(_dataDir, "*.csv"))
        {
            files.Add(AnalyzeFile(path));
        }
        // Also check samples/data relative to working directory
        var samplesDir = FindSamplesDir();
        if (samplesDir is not null && samplesDir != _dataDir)
        {
            foreach (var path in Directory.GetFiles(samplesDir, "*.csv"))
            {
                if (!files.Any(f => f.FileName == Path.GetFileName(path)))
                    files.Add(AnalyzeFile(path));
            }
        }

        // Scan QDM watch directory if configured and exists
        if (!string.IsNullOrWhiteSpace(_qdmWatchDir) && Directory.Exists(_qdmWatchDir))
        {
            foreach (var path in Directory.GetFiles(_qdmWatchDir, "*.csv"))
            {
                if (!files.Any(f => f.FileName == Path.GetFileName(path)))
                    files.Add(AnalyzeFile(path));
            }
        }

        return files.OrderBy(f => f.FileName).ToList();
    }

    /// <summary>Reads the first N rows of a CSV file for preview.</summary>
    public List<string[]> PreviewFile(string fullPath, int maxRows = 20)
    {
        var rows = new List<string[]>();
        using var reader = new StreamReader(fullPath);
        string? line;
        int count = 0;
        while ((line = reader.ReadLine()) is not null && count < maxRows)
        {
            rows.Add(line.Split(','));
            count++;
        }
        return rows;
    }

    /// <summary>Validates that a CSV file has the expected engine schema.</summary>
    public (bool Valid, string Message) ValidateSchema(string fullPath)
    {
        try
        {
            using var reader = new StreamReader(fullPath);
            var header = reader.ReadLine();
            if (header is null) return (false, "File is empty.");

            var firstDataLine = reader.ReadLine();
            var lines = firstDataLine is not null
                ? new[] { header, firstDataLine }
                : new[] { header };
            var format = CsvFormatConverter.DetectFormat(lines);
            if (format == CsvFormatConverter.SourceFormat.Engine)
                return (true, "Valid engine format (Timestamp,Open,High,Low,Close,Volume).");

            return (true, $"Detected format: {format}. Can be converted to engine format.");
        }
        catch (Exception ex)
        {
            return (false, $"Error reading file: {ex.Message}");
        }
    }

    /// <summary>Converts a file to engine format and saves it.</summary>
    public string ConvertToEngineFormat(string sourcePath)
    {
        var content = File.ReadAllText(sourcePath);
        var timezoneId = _settingsService?.Load().QdmTimezoneId ?? "UTC";
        var converted = CsvFormatConverter.Convert(content, CsvFormatConverter.SourceFormat.Auto, timezoneId);
        var outputName = Path.GetFileNameWithoutExtension(sourcePath) + "_converted.csv";
        var outputPath = Path.Combine(_dataDir, outputName);
        File.WriteAllText(outputPath, converted);
        return outputPath;
    }

    /// <summary>Copies a file into the data directory.</summary>
    public string ImportFile(string sourcePath)
    {
        var destPath = Path.Combine(_dataDir, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destPath, overwrite: true);
        return destPath;
    }

    private static DataFileInfo AnalyzeFile(string path)
    {
        var fi = new FileInfo(path);
        string[] headers = Array.Empty<string>();
        int rowCount = 0;
        string? firstTs = null, lastTs = null;
        string format = "Unknown";

        try
        {
            using var reader = new StreamReader(path);
            var headerLine = reader.ReadLine();
            if (headerLine is not null)
            {
                headers = headerLine.Split(',');
                var firstDataLine = reader.ReadLine();
                var lines = firstDataLine is not null
                    ? new[] { headerLine, firstDataLine }
                    : new[] { headerLine };
                format = CsvFormatConverter.DetectFormat(lines).ToString();

                // Count data rows (first data line already read)
                if (firstDataLine is not null)
                {
                    rowCount = 1;
                    firstTs = firstDataLine.Split(',').FirstOrDefault();
                    string? line;
                    string? lastLine = firstDataLine;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        rowCount++;
                        lastLine = line;
                    }
                    lastTs = lastLine.Split(',').FirstOrDefault();
                }
            }
        }
        catch { /* best effort */ }

        return new DataFileInfo(fi.Name, fi.FullName, fi.Length, rowCount, format, firstTs, lastTs, headers);
    }

    private static string? FindSamplesDir()
    {
        var dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 6; i++)
        {
            var candidate = Path.Combine(dir, "samples", "data");
            if (Directory.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        return null;
    }
}
