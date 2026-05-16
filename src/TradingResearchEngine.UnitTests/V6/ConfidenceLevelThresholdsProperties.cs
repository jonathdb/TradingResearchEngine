using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 21: Confidence Level Thresholds
/// N completed items out of 11: HIGH when N ≥ 9, MEDIUM when 6 ≤ N &lt; 9, LOW when N &lt; 6.
/// V10: Updated for 11-item checklist (added DSR and MinBTL).
/// **Validates: Requirement 23.2**
/// </summary>
public class ConfidenceLevelThresholdsProperties
{
    [Property(MaxTest = 20)]
    public bool ConfidenceLevel_MatchesThresholds(
        bool b1, bool b2, bool b3, bool b4, bool b5, bool b6, bool b7, bool b8, bool b9)
    {
        // Construct with DSR and MinBTL as Passed to test full range
        var dsrStatus = ChecklistItemStatus.Passed("DSR OK");
        var minBtlStatus = ChecklistItemStatus.Passed("MinBTL OK");

        var checklist = new ResearchChecklist(
            b1, b2, b3, b4, b5, b6, b7, b8, b9,
            DsrStatus: dsrStatus,
            MinBtlStatus: minBtlStatus);

        // 9 booleans + 2 always-passed quantitative items = passed count
        int boolPassed = new[] { b1, b2, b3, b4, b5, b6, b7, b8, b9 }.Count(x => x);
        int passed = boolPassed + 2; // DSR and MinBTL are both Passed

        string expectedLevel = passed switch
        {
            >= 9 => "HIGH",
            >= 6 => "MEDIUM",
            _ => "LOW"
        };

        return checklist.PassedCount == passed
            && checklist.TotalChecks == 11
            && checklist.ConfidenceLevel == expectedLevel;
    }

    [Property(MaxTest = 20)]
    public bool ConfidenceLevel_WithNullQuantitativeItems_MatchesThresholds(
        bool b1, bool b2, bool b3, bool b4, bool b5, bool b6, bool b7, bool b8, bool b9)
    {
        // When DSR and MinBTL are null (not evaluated), they don't contribute to PassedCount
        var checklist = new ResearchChecklist(
            b1, b2, b3, b4, b5, b6, b7, b8, b9);

        int passed = new[] { b1, b2, b3, b4, b5, b6, b7, b8, b9 }.Count(x => x);

        string expectedLevel = passed switch
        {
            >= 9 => "HIGH",
            >= 6 => "MEDIUM",
            _ => "LOW"
        };

        return checklist.PassedCount == passed
            && checklist.TotalChecks == 11
            && checklist.ConfidenceLevel == expectedLevel;
    }
}
