using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;
using Xunit;

namespace TradingResearchEngine.UnitTests.Strategy;

/// <summary>
/// Bug condition exploration and warning validation tests for <see cref="ConfigDraftValidator"/>.
/// </summary>
public class ConfigDraftValidatorWarningTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Bug condition exploration: the tpl-donchian-breakout template should have
    /// RecommendedTimeframe == "Any" because the strategy is timeframe-agnostic.
    /// On UNFIXED code this test FAILS (value is "Daily"), confirming the bug exists.
    /// </summary>
    [Fact]
    public void DonchianTemplate_RecommendedTimeframe_IsAny()
    {
        var template = DefaultStrategyTemplates.All
            .First(t => t.TemplateId == "tpl-donchian-breakout");

        Assert.Equal("Any", template.RecommendedTimeframe);
    }

    // ── Part A: ValidateStep Preservation ──

    /// <summary>
    /// Preservation: ValidateStep returns no errors for a valid ConfigDraft at step 2.
    /// </summary>
    [Fact]
    public void ValidateStep_ValidDraftAtStep2_ReturnsNoErrors()
    {
        var draft = new ConfigDraft(
            DraftId: "draft-1",
            CurrentStep: 2,
            StrategyName: "My Strategy",
            StrategyType: "donchian-breakout",
            TemplateId: "tpl-donchian-breakout",
            SourceType: SourceType.Template,
            Hypothesis: null,
            ExpectedFailureMode: null,
            DataConfig: new DataConfig("csv", new Dictionary<string, object>(), "Daily", 252),
            StrategyParameters: null,
            ExecutionConfig: null,
            RiskConfig: null,
            PresetId: null,
            PresetOverrides: null,
            CreatedAt: T0,
            UpdatedAt: T0);

        var errors = ConfigDraftValidator.ValidateStep(draft);

        Assert.Empty(errors);
    }

    /// <summary>
    /// Preservation: ValidateStep returns an error when StrategyType is missing at step 2.
    /// </summary>
    [Fact]
    public void ValidateStep_MissingStrategyType_ReturnsError()
    {
        var draft = new ConfigDraft(
            DraftId: "draft-1",
            CurrentStep: 2,
            StrategyName: "My Strategy",
            StrategyType: null,
            TemplateId: "tpl-donchian-breakout",
            SourceType: SourceType.Template,
            Hypothesis: null,
            ExpectedFailureMode: null,
            DataConfig: new DataConfig("csv", new Dictionary<string, object>(), "Daily", 252),
            StrategyParameters: null,
            ExecutionConfig: null,
            RiskConfig: null,
            PresetId: null,
            PresetOverrides: null,
            CreatedAt: T0,
            UpdatedAt: T0);

        var errors = ConfigDraftValidator.ValidateStep(draft);

        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("StrategyType", StringComparison.OrdinalIgnoreCase));
    }

    // ── Part B: Other Template Preservation ──

    /// <summary>
    /// Preservation: all non-donchian templates retain RecommendedTimeframe = "Daily".
    /// </summary>
    [Fact]
    public void OtherTemplates_RecommendedTimeframe_RemainDaily()
    {
        var expectedDaily = new[] { "tpl-vol-trend", "tpl-zscore-mr", "tpl-stationary-mr", "tpl-regime-rotation", "tpl-buy-hold" };

        foreach (var templateId in expectedDaily)
        {
            var template = DefaultStrategyTemplates.All
                .First(t => t.TemplateId == templateId);

            Assert.Equal("Daily", template.RecommendedTimeframe);
        }
    }

    // ── Part C: ValidateWarnings Tests ──

    private static ConfigDraft MakeDraft(int step = 2, string? strategyType = "donchian-breakout",
        string? templateId = "tpl-donchian-breakout", string? timeframe = "Daily") =>
        new("draft-1", step, "Test", strategyType, templateId, SourceType.Template,
            null, null,
            timeframe is not null ? new DataConfig("csv", new Dictionary<string, object>(), timeframe, 252) : null,
            null, null, null, null, null, T0, T0);

    private static IReadOnlyList<StrategyTemplate> MakeTemplates(string templateId, string recommendedTimeframe) =>
        new[] { new StrategyTemplate(templateId, "Test", "Test", "test", "test",
            new Dictionary<string, object>(), recommendedTimeframe) };

    /// <summary>
    /// ValidateWarnings returns no warning when the template's RecommendedTimeframe is "Any".
    /// </summary>
    [Fact]
    public void ValidateWarnings_RecommendedTimeframeIsAny_ReturnsNoWarning()
    {
        var draft = MakeDraft(timeframe: "Daily");
        var warnings = ConfigDraftValidator.ValidateWarnings(draft, DefaultStrategyTemplates.All);

        Assert.Empty(warnings);
    }

    /// <summary>
    /// ValidateWarnings returns no warning when the template's RecommendedTimeframe is empty
    /// (treated as null-equivalent by the validator).
    /// </summary>
    [Fact]
    public void ValidateWarnings_RecommendedTimeframeIsNull_ReturnsNoWarning()
    {
        var draft = MakeDraft(templateId: "tpl-custom");
        var templates = MakeTemplates("tpl-custom", "");
        var warnings = ConfigDraftValidator.ValidateWarnings(draft, templates);

        Assert.Empty(warnings);
    }

    /// <summary>
    /// ValidateWarnings returns no warning when DataConfig.Timeframe is null.
    /// </summary>
    [Fact]
    public void ValidateWarnings_DataTimeframeIsNull_ReturnsNoWarning()
    {
        var draft = MakeDraft(templateId: "tpl-vol-trend", timeframe: null);
        var warnings = ConfigDraftValidator.ValidateWarnings(draft, DefaultStrategyTemplates.All);

        Assert.Empty(warnings);
    }

    /// <summary>
    /// ValidateWarnings returns a warning when DataConfig.Timeframe and the template's
    /// RecommendedTimeframe are both non-null, non-"Any", and mismatched.
    /// </summary>
    [Fact]
    public void ValidateWarnings_MismatchedTimeframes_ReturnsWarning()
    {
        var draft = MakeDraft(templateId: "tpl-vol-trend", timeframe: "M15");
        var warnings = ConfigDraftValidator.ValidateWarnings(draft, DefaultStrategyTemplates.All);

        Assert.NotEmpty(warnings);
        Assert.Contains("M15", warnings[0]);
        Assert.Contains("Daily", warnings[0]);
    }

    /// <summary>
    /// ValidateWarnings returns no warning when DataConfig.Timeframe matches the
    /// template's RecommendedTimeframe.
    /// </summary>
    [Fact]
    public void ValidateWarnings_MatchingTimeframes_ReturnsNoWarning()
    {
        var draft = MakeDraft(templateId: "tpl-vol-trend", timeframe: "Daily");
        var warnings = ConfigDraftValidator.ValidateWarnings(draft, DefaultStrategyTemplates.All);

        Assert.Empty(warnings);
    }

    /// <summary>
    /// ValidateWarnings returns no warning when CurrentStep is below 2.
    /// </summary>
    [Fact]
    public void ValidateWarnings_CurrentStepBelow2_ReturnsNoWarning()
    {
        var draft = MakeDraft(step: 1, templateId: "tpl-vol-trend", timeframe: "M15");
        var warnings = ConfigDraftValidator.ValidateWarnings(draft, DefaultStrategyTemplates.All);

        Assert.Empty(warnings);
    }

    /// <summary>
    /// ValidateWarnings returns no warning when TemplateId is null (no template match).
    /// </summary>
    [Fact]
    public void ValidateWarnings_TemplateIdIsNull_ReturnsNoWarning()
    {
        var draft = MakeDraft(templateId: null, timeframe: "M15");
        var warnings = ConfigDraftValidator.ValidateWarnings(draft, DefaultStrategyTemplates.All);

        Assert.Empty(warnings);
    }
}
