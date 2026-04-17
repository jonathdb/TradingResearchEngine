using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using TradingResearchEngine.Infrastructure.Settings;

namespace TradingResearchEngine.UnitTests.V3;

/// <summary>
/// Property-based tests for AppSettings QdmWatchDirectory persistence.
/// **Validates: Requirements 4.3**
/// </summary>
public class AppSettingsQdmProperties
{
    // Feature: qdm-folder-integration, Property 5: AppSettings QdmWatchDirectory persistence round-trip
    /// <summary>
    /// For any AppSettings instance with a non-null QdmWatchDirectory string,
    /// serialising to JSON via SettingsService.Save and then deserialising via
    /// SettingsService.Load SHALL produce an AppSettings where QdmWatchDirectory
    /// equals the original value.
    /// **Validates: Requirements 4.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public Property QdmWatchDirectory_PersistenceRoundTrip()
    {
        // Generate non-empty path strings: alphanumeric segments joined by path separators
        var segmentGen = Gen.Elements(
            "qdm", "exports", "data", "myFolder", "bar-data", "QDM_Output",
            "C:", "D:", "Users", "home", "Documents", "tmp", "test123");
        var gen =
            from segCount in Gen.Choose(1, 5)
            from segments in segmentGen.ArrayOf(segCount)
            select string.Join(Path.DirectorySeparatorChar.ToString(), segments);

        return Prop.ForAll(
            gen.ToArbitrary(),
            qdmPath =>
            {
                var tempFile = Path.GetTempFileName();
                try
                {
                    var original = AppSettings.Default with { QdmWatchDirectory = qdmPath };
                    var service = new SettingsService(tempFile);

                    service.Save(original);
                    var loaded = service.Load();

                    return loaded.QdmWatchDirectory == qdmPath;
                }
                finally
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
            });
    }
}
