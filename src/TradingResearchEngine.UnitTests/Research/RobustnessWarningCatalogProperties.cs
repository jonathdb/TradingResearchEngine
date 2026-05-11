using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.Research;

// Feature: trading-engine-stories, Property 14: Warning Catalog Fallback

/// <summary>
/// Property 14: Warning Catalog Fallback.
/// For any string label (including labels not in the catalog),
/// RobustnessWarningCatalog.GetExplanation(label) SHALL return a non-null string
/// without throwing — returning the catalog explanation if present, or the raw label as fallback.
/// **Validates: Requirements 13.2, 13.3**
/// </summary>
public class RobustnessWarningCatalogProperties
{
    /// <summary>
    /// For any arbitrary string label, GetExplanation never returns null and never throws.
    /// This covers random strings including empty, whitespace, special characters,
    /// known labels, and unknown labels.
    /// **Validates: Requirements 13.2, 13.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool GetExplanation_NeverReturnsNull_ForAnyString(NonNull<string> label)
    {
        var result = RobustnessWarningCatalog.GetExplanation(label.Get);

        return result is not null;
    }

    /// <summary>
    /// For any known catalog label, GetExplanation returns the catalog explanation
    /// (which differs from the raw label).
    /// **Validates: Requirements 13.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool GetExplanation_ReturnsCatalogText_ForKnownLabels(PositiveInt indexWrap)
    {
        var keys = RobustnessWarningCatalog.Explanations.Keys.ToList();
        if (keys.Count == 0) return true;

        int index = indexWrap.Get % keys.Count;
        string knownLabel = keys[index];

        var result = RobustnessWarningCatalog.GetExplanation(knownLabel);

        // Known labels should return the explanation text (not the label itself)
        return result == RobustnessWarningCatalog.Explanations[knownLabel];
    }

    /// <summary>
    /// For any string label NOT in the catalog, GetExplanation returns the raw label as fallback.
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool GetExplanation_ReturnsRawLabel_ForUnknownLabels(NonNull<string> label)
    {
        string testLabel = label.Get;

        // Skip if the label happens to be a known key
        if (RobustnessWarningCatalog.Explanations.ContainsKey(testLabel))
            return true;

        var result = RobustnessWarningCatalog.GetExplanation(testLabel);

        // Unknown labels should return the raw label as fallback
        return result == testLabel;
    }
}
