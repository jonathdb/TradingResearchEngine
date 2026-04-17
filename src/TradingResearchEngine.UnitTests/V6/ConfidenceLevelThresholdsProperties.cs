using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Research;

namespace TradingResearchEngine.UnitTests.V6;

/// <summary>
/// Feature: trading-research-engine, Property 21: Confidence Level Thresholds
/// N completed items out of 9: HIGH when N ≥ 8, MEDIUM when 5 ≤ N &lt; 8, LOW when N &lt; 5.
/// **Validates: Requirement 23.2**
/// </summary>
public class ConfidenceLevelThresholdsProperties
{
    [Property(MaxTest = 100)]
    public bool ConfidenceLevel_MatchesThresholds(
        bool b1, bool b2, bool b3, bool b4, bool b5, bool b6, bool b7, bool b8, bool b9)
    {
        var checklist = new ResearchChecklist(
            b1, b2, b3, b4, b5, b6, b7, b8, b9);

        int passed = new[] { b1, b2, b3, b4, b5, b6, b7, b8, b9 }.Count(x => x);

        string expectedLevel = passed switch
        {
            >= 8 => "HIGH",
            >= 5 => "MEDIUM",
            _ => "LOW"
        };

        return checklist.PassedCount == passed
            && checklist.TotalChecks == 9
            && checklist.ConfidenceLevel == expectedLevel;
    }
}
