using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingResearchEngine.Infrastructure.DataProviders;

namespace TradingResearchEngine.IntegrationTests.DataProviders;

/// <summary>
/// Bug condition exploration tests that verify default paths are project-relative
/// and CsvDataProvider throws descriptive errors on missing files.
///
/// These tests encode the EXPECTED (fixed) behavior. They are expected to FAIL
/// on unfixed code, confirming the bug exists.
///
/// Validates: Requirements 1.1, 1.2, 1.3, 1.5
/// </summary>
public class DefaultPathBugConditionTests
{
    /// <summary>
    /// Property F1: DataFileService defaults to project-relative path.
    /// GIVEN DataFileService is constructed without an explicit dataDir
    /// THEN DataDirectory ends with platform-appropriate "data" path segment
    /// AND does not contain "AppData" or "LocalApplicationData".
    ///
    /// On unfixed code this FAILS because the default resolves to %LOCALAPPDATA%.
    /// </summary>
    [Fact]
    public void DataFileService_DefaultConstructor_DataDirectoryIsProjectRelative()
    {
        // Arrange & Act
        var service = new DataFileService(null);
        var dataDir = service.DataDirectory;

        // Assert — expected (fixed) behavior
        var normalised = dataDir.Replace('\\', '/');
        Assert.True(normalised.EndsWith("/data"),
            $"DataDirectory should end with '/data' but was: {dataDir}");
        Assert.DoesNotContain("AppData", dataDir, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalApplicationData", dataDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Property F3: DukascopyDataProvider uses project-relative cache.
    /// GIVEN a DukascopyDataProvider constructed without an explicit cacheDir
    /// THEN the cache directory path ends with "data/dukascopy-cache" (or platform equivalent)
    /// AND does not contain "AppData" or "LocalApplicationData".
    ///
    /// On unfixed code this FAILS because CacheDir is a static readonly field
    /// pointing to %LOCALAPPDATA%\TradingResearchEngine\DukascopyCache.
    /// After the fix, _cacheDir is an instance field set in the constructor.
    /// </summary>
    [Fact]
    public void DukascopyDataProvider_DefaultConstruction_CacheDirIsProjectRelative()
    {
        // Construct a DukascopyDataProvider with default cacheDir (no explicit override)
        var httpClient = new HttpClient();
        var logger = NullLoggerFactory.Instance.CreateLogger<DukascopyDataProvider>();
        var provider = new DukascopyDataProvider(httpClient, logger);

        // Read the private instance field _cacheDir via reflection
        var cacheField = typeof(DukascopyDataProvider)
            .GetField("_cacheDir", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(cacheField);

        var cacheDir = cacheField!.GetValue(provider) as string;
        Assert.NotNull(cacheDir);

        var normalised = cacheDir!.Replace('\\', '/');
        Assert.True(normalised.EndsWith("data/dukascopy-cache"),
            $"_cacheDir should end with 'data/dukascopy-cache' but was: {cacheDir}");
        Assert.DoesNotContain("AppData", cacheDir, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalApplicationData", cacheDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Property F2: CsvDataProvider throws descriptive error on missing file.
    /// GIVEN a CsvDataProvider with a non-existent file path
    /// WHEN GetBars is called
    /// THEN a FileNotFoundException is thrown
    /// AND the message contains the file path
    /// AND the message contains guidance text ("data directory" or "configuration").
    ///
    /// On unfixed code this FAILS because CsvDataProvider propagates a raw
    /// FileNotFoundException from StreamReader without guidance text.
    /// </summary>
    [Fact]
    public async Task CsvDataProvider_NonExistentFile_ThrowsDescriptiveFileNotFoundException()
    {
        // Arrange
        var fakePath = Path.Combine("nonexistent", "dir", "missing-data.csv");
        var logger = NullLoggerFactory.Instance.CreateLogger<CsvDataProvider>();
        var provider = new CsvDataProvider(fakePath, logger);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<FileNotFoundException>(async () =>
        {
            await foreach (var _ in provider.GetBars("TEST", "1D",
                DateTimeOffset.MinValue, DateTimeOffset.MaxValue))
            {
                // consume the enumerable to trigger the exception
            }
        });

        // The exception message must contain the file path
        Assert.Contains(fakePath, ex.Message, StringComparison.OrdinalIgnoreCase);

        // The exception message must contain guidance text
        var hasGuidance = ex.Message.Contains("data directory", StringComparison.OrdinalIgnoreCase)
                       || ex.Message.Contains("configuration", StringComparison.OrdinalIgnoreCase);
        Assert.True(hasGuidance,
            $"FileNotFoundException message should contain guidance text ('data directory' or 'configuration') but was: {ex.Message}");
    }
}
