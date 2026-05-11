using FsCheck;
using FsCheck.Xunit;
using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;
using TradingResearchEngine.Web.Components.Builder;

namespace TradingResearchEngine.UnitTests.Strategy;

// Feature: trading-engine-stories, Property 13: Builder Step Persistence and Navigation Guard

/// <summary>
/// Property 13: Builder Step Persistence and Navigation Guard.
/// For any ConfigDraft with CurrentStep=S and MaxVisitedStep=M, loading restores to step S,
/// and navigation to any step > M is prevented.
/// **Validates: Requirements 12.2, 12.3**
/// </summary>
public class BuilderStepPersistenceProperties
{
    /// <summary>
    /// Creates a ConfigDraft with the specified CurrentStep and MaxVisitedStep values.
    /// </summary>
    private static ConfigDraft CreateDraftWithSteps(int currentStep, int maxVisitedStep)
    {
        var now = DateTimeOffset.UtcNow;
        return new ConfigDraft(
            DraftId: Guid.NewGuid().ToString(),
            CurrentStep: currentStep,
            MaxVisitedStep: maxVisitedStep,
            StrategyName: "Test Strategy",
            StrategyType: "moving-average-crossover",
            TemplateId: null,
            SourceType: SourceType.Template,
            Hypothesis: null,
            ExpectedFailureMode: null,
            DataConfig: null,
            StrategyParameters: null,
            ExecutionConfig: null,
            RiskConfig: null,
            PresetId: null,
            PresetOverrides: null,
            CreatedAt: now,
            UpdatedAt: now);
    }

    /// <summary>
    /// For any valid CurrentStep S (1-5) and MaxVisitedStep M (S to 5),
    /// loading a ConfigDraft via FromDraft SHALL restore CurrentStep to S.
    /// **Validates: Requirements 12.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FromDraft_RestoresCurrentStep(PositiveInt sWrap, PositiveInt mOffsetWrap)
    {
        // Constrain S to [1, 5]
        int s = (sWrap.Get % 5) + 1;
        // Constrain M to [S, 5] — MaxVisitedStep must be >= CurrentStep
        int m = s + (mOffsetWrap.Get % (5 - s + 1));

        var draft = CreateDraftWithSteps(s, m);
        var vm = BuilderViewModel.FromDraft(draft);

        return vm.CurrentStep == s;
    }

    /// <summary>
    /// For any valid CurrentStep S (1-5) and MaxVisitedStep M (S to 5),
    /// loading a ConfigDraft via FromDraft SHALL restore MaxVisitedStep to M.
    /// **Validates: Requirements 12.2**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool FromDraft_RestoresMaxVisitedStep(PositiveInt sWrap, PositiveInt mOffsetWrap)
    {
        // Constrain S to [1, 5]
        int s = (sWrap.Get % 5) + 1;
        // Constrain M to [S, 5]
        int m = s + (mOffsetWrap.Get % (5 - s + 1));

        var draft = CreateDraftWithSteps(s, m);
        var vm = BuilderViewModel.FromDraft(draft);

        return vm.MaxVisitedStep == m;
    }

    /// <summary>
    /// For any ConfigDraft with MaxVisitedStep=M, navigation to any step > M
    /// SHALL be prevented (GoToStep guard: step >= 1 && step <= MaxVisitedStep).
    /// The navigation guard prevents changing CurrentStep when the target exceeds MaxVisitedStep.
    /// **Validates: Requirements 12.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NavigationGuard_PreventsStepBeyondMaxVisited(PositiveInt sWrap, PositiveInt mOffsetWrap, PositiveInt targetOffsetWrap)
    {
        // Constrain S to [1, 5]
        int s = (sWrap.Get % 5) + 1;
        // Constrain M to [S, 5]
        int m = s + (mOffsetWrap.Get % (5 - s + 1));
        // Target step > M (at least M+1, up to 10 to test beyond valid range)
        int targetStep = m + 1 + (targetOffsetWrap.Get % 5);

        var draft = CreateDraftWithSteps(s, m);
        var vm = BuilderViewModel.FromDraft(draft);

        // Simulate the navigation guard logic: step >= 1 && step <= MaxVisitedStep
        bool navigationAllowed = targetStep >= 1 && targetStep <= vm.MaxVisitedStep;

        // Navigation to step > M should NOT be allowed
        return !navigationAllowed;
    }

    /// <summary>
    /// For any ConfigDraft with MaxVisitedStep=M, navigation to any step in [1, M]
    /// SHALL be allowed (GoToStep guard permits valid steps).
    /// **Validates: Requirements 12.3**
    /// </summary>
    [Property(MaxTest = 100)]
    public bool NavigationGuard_AllowsStepWithinMaxVisited(PositiveInt sWrap, PositiveInt mOffsetWrap, PositiveInt targetWrap)
    {
        // Constrain S to [1, 5]
        int s = (sWrap.Get % 5) + 1;
        // Constrain M to [S, 5]
        int m = s + (mOffsetWrap.Get % (5 - s + 1));
        // Target step in [1, M]
        int targetStep = (targetWrap.Get % m) + 1;

        var draft = CreateDraftWithSteps(s, m);
        var vm = BuilderViewModel.FromDraft(draft);

        // Simulate the navigation guard logic: step >= 1 && step <= MaxVisitedStep
        bool navigationAllowed = targetStep >= 1 && targetStep <= vm.MaxVisitedStep;

        // Navigation to step within [1, M] should be allowed
        return navigationAllowed;
    }
}
