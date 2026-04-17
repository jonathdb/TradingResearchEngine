using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.DataProviders;

public class DataFileServiceQdmTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string _qdmWatchDir;

    private const string QdmHeader = "Date,Time,Open,High,Low,Close,Volume";
    private const string QdmRow1 = "2020.01.02,00:00,1.12100,1.12250,1.12000,1.12190,1234";
    private const string QdmRow2 = "2020.01.03,01:30,1.12300,1.12400,1.12100,1.12350,5678";

    public DataFileServiceQdmTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), $"tre-data-{Guid.NewGuid():N}");
        _qdmWatchDir = Path.Combine(Path.GetTempPath(), $"tre-qdm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_qdmWatchDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dataDir)) Directory.Delete(_dataDir, true);
        if (Directory.Exists(_qdmWatchDir)) Directory.Delete(_qdmWatchDir, true);
    }

    [Fact]
    public void DataFileService_QdmWatchDirectory_ListsQdmFiles()
    {
        // Arrange — place two QDM CSVs in the watch directory
        WriteQdmFile(Path.Combine(_qdmWatchDir, "eurusd-m15.csv"));
        WriteQdmFile(Path.Combine(_qdmWatchDir, "gbpusd-h1.csv"));

        var svc = new DataFileService(_dataDir, _qdmWatchDir);

        // Act
        var files = svc.ListFiles();

        // Assert — both QDM files should appear
        Assert.Contains(files, f => f.FileName == "eurusd-m15.csv");
        Assert.Contains(files, f => f.FileName == "gbpusd-h1.csv");
    }

    [Fact]
    public void DataFileService_QdmWatchDirectory_DeduplicatesByFileName()
    {
        // Arrange — same filename in both directories
        var sharedName = "eurusd-m15.csv";
        var dataFilePath = Path.Combine(_dataDir, sharedName);
        var qdmFilePath = Path.Combine(_qdmWatchDir, sharedName);

        // DataDirectory version has engine-format content
        File.WriteAllText(dataFilePath,
            "Timestamp,Open,High,Low,Close,Volume\n2020-01-02T00:00:00.0000000+00:00,1.12100,1.12250,1.12000,1.12190,1234");
        WriteQdmFile(qdmFilePath);

        var svc = new DataFileService(_dataDir, _qdmWatchDir);

        // Act
        var files = svc.ListFiles();

        // Assert — only one file with that name, and it's the DataDirectory version
        var matches = files.Where(f => f.FileName == sharedName).ToList();
        Assert.Single(matches);
        Assert.Equal(Path.GetFullPath(dataFilePath), Path.GetFullPath(matches[0].FullPath));
    }

    [Fact]
    public void DataFileService_QdmWatchDirectory_NullOrMissing_NoChange()
    {
        // Arrange — put a file in DataDirectory so we have a baseline
        WriteQdmFile(Path.Combine(_dataDir, "baseline.csv"));

        // Act — null qdmWatchDir
        var svcNull = new DataFileService(_dataDir, null);
        var filesNull = svcNull.ListFiles();

        // Act — empty string qdmWatchDir
        var svcEmpty = new DataFileService(_dataDir, "");
        var filesEmpty = svcEmpty.ListFiles();

        // Act — non-existent directory
        var svcMissing = new DataFileService(_dataDir, "/tmp/does-not-exist-" + Guid.NewGuid().ToString("N"));
        var filesMissing = svcMissing.ListFiles();

        // Assert — all three should contain the baseline file and have the same count
        // (samples/data may also be picked up by FindSamplesDir, so we check by name not count)
        Assert.Contains(filesNull, f => f.FileName == "baseline.csv");
        Assert.Contains(filesEmpty, f => f.FileName == "baseline.csv");
        Assert.Contains(filesMissing, f => f.FileName == "baseline.csv");
        Assert.Equal(filesNull.Count, filesEmpty.Count);
        Assert.Equal(filesNull.Count, filesMissing.Count);
    }

    [Fact]
    public void DataFileService_ConvertQdmFile_OutputInDataDirectory()
    {
        // Arrange — QDM file lives in the watch directory
        var qdmFilePath = Path.Combine(_qdmWatchDir, "eurusd-m15.csv");
        WriteQdmFile(qdmFilePath);

        var svc = new DataFileService(_dataDir, _qdmWatchDir);

        // Act
        var outputPath = svc.ConvertToEngineFormat(qdmFilePath);

        // Assert — output is in DataDirectory, not in QdmWatchDirectory
        Assert.StartsWith(Path.GetFullPath(_dataDir), Path.GetFullPath(outputPath));
        Assert.True(File.Exists(outputPath));
        Assert.Equal("eurusd-m15_converted.csv", Path.GetFileName(outputPath));

        // Verify the converted content starts with the engine header
        var lines = File.ReadAllLines(outputPath);
        Assert.Equal("Timestamp,Open,High,Low,Close,Volume", lines[0]);
        Assert.True(lines.Length > 1, "Converted file should have data rows");
    }

    [Fact]
    public void DataFileService_ConvertQdmFile_OriginalUnmodified()
    {
        // Arrange — QDM file in watch directory
        var qdmFilePath = Path.Combine(_qdmWatchDir, "eurusd-m15.csv");
        WriteQdmFile(qdmFilePath);
        var originalContent = File.ReadAllText(qdmFilePath);

        var svc = new DataFileService(_dataDir, _qdmWatchDir);

        // Act
        svc.ConvertToEngineFormat(qdmFilePath);

        // Assert — source file is byte-for-byte identical
        var afterContent = File.ReadAllText(qdmFilePath);
        Assert.Equal(originalContent, afterContent);
    }

    /// <summary>Writes a minimal valid QDM CSV file to the given path.</summary>
    private static void WriteQdmFile(string path)
    {
        File.WriteAllText(path, $"{QdmHeader}\n{QdmRow1}\n{QdmRow2}");
    }
}
