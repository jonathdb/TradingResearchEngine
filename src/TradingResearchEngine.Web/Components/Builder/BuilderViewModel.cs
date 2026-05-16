using TradingResearchEngine.Application.Strategies;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Web.Components.Builder;

/// <summary>
/// Mutable ViewModel for the 5-step strategy builder.
/// Lives in the Web layer only. Maps to immutable domain records on save.
/// </summary>
public sealed class BuilderViewModel
{
    // Step 1 — Choose Starting Point
    public SourceType SourceType { get; set; } = SourceType.Template;
    public string? TemplateId { get; set; }
    public string? StrategyType { get; set; }
    public string StrategyName { get; set; } = "";
    public string? Hypothesis { get; set; }
    public string? ExpectedFailureMode { get; set; }

    // Step 2 — Data & Execution Window
    public string? DataFilePath { get; set; }
    public string Symbol { get; set; } = "";
    public string Interval { get; set; } = "1D";
    public string Timeframe { get; set; } = "Daily";
    public DateTimeOffset? StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public decimal InSamplePercent { get; set; } = 70m;
    public decimal? SealedTestPercent { get; set; }

    // Step 3 — Strategy Parameters
    public Dictionary<string, object> Parameters { get; set; } = new();
    public bool AdvancedMode { get; set; }

    // Step 4 — Realism & Risk Profile
    public string? PresetId { get; set; }
    public Dictionary<string, object> PresetOverrides { get; set; } = new();
    public string SlippageModelType { get; set; } = "ZeroSlippageModel";
    public string CommissionModelType { get; set; } = "ZeroCommissionModel";
    public decimal InitialCash { get; set; } = 100_000m;
    public decimal AnnualRiskFreeRate { get; set; } = 0.05m;
    public decimal StopLoss { get; set; } = 2.0m;
    public int FillDelayBars { get; set; } = 0;

    /// <summary>
    /// The effective realism profile for this builder session.
    /// In beginner mode, this is set from the template's <see cref="StrategyTemplate.DefaultRealismProfile"/>.
    /// Advanced users may override via preset selection or advanced overrides.
    /// </summary>
    public ExecutionRealismProfile RealismProfile { get; set; } = ExecutionRealismProfile.StandardBacktest;

    // Navigation
    public int CurrentStep { get; set; } = 1;
    public int MaxVisitedStep { get; set; } = 1;
    public bool IsDirty { get; set; }
    public string DraftId { get; set; } = Guid.NewGuid().ToString();

    // Auto-save identity
    /// <summary>Strategy ID when editing an existing strategy. Null for new strategies.</summary>
    public string? StrategyId { get; set; }
    /// <summary>Strategy version ID when editing an existing version. Null for new strategies.</summary>
    public string? StrategyVersionId { get; set; }
    /// <summary>Transient session GUID for new strategy drafts.</summary>
    public string SessionGuid { get; set; } = Guid.NewGuid().ToString();
    /// <summary>Timestamp of the last successful auto-save. Null if no save has occurred.</summary>
    public DateTimeOffset? LastDraftSavedAt { get; set; }

    // V9: Condition Builder state
    /// <summary>Parsed entry condition AST for the visual condition builder. Null when using raw text mode.</summary>
    public TradingResearchEngine.Application.Strategies.Composite.Conditions.ConditionNode? ParsedEntryCondition { get; set; }
    /// <summary>Parsed exit condition AST for the visual condition builder. Null when using raw text mode.</summary>
    public TradingResearchEngine.Application.Strategies.Composite.Conditions.ConditionNode? ParsedExitCondition { get; set; }
    /// <summary>Raw entry condition expression string (used when visual builder cannot parse).</summary>
    public string? EntryConditionText { get; set; }
    /// <summary>Raw exit condition expression string (used when visual builder cannot parse).</summary>
    public string? ExitConditionText { get; set; }

    // V9: Builder mode
    /// <summary>When true, hides advanced parameters and shows contextual help. Persisted in user settings.</summary>
    public bool IsBeginnerMode { get; set; } = true;

    /// <summary>Canonical BarsPerYear for the current timeframe.</summary>
    public int BarsPerYear => Timeframe switch
    {
        "H4" => 1512,
        "H1" => 6048,
        "M15" => 24192,
        _ => 252
    };

    private Dictionary<string, object> BuildDataProviderOptions()
    {
        var opts = new Dictionary<string, object>();
        if (DataFilePath is not null) opts["FilePath"] = DataFilePath;
        if (!string.IsNullOrEmpty(Symbol)) opts["Symbol"] = Symbol;
        if (!string.IsNullOrEmpty(Interval)) opts["Interval"] = Interval;
        return opts;
    }

    /// <summary>Maps the mutable ViewModel to an immutable ConfigDraft for persistence.</summary>
    public ConfigDraft ToConfigDraft()
    {
        var now = DateTimeOffset.UtcNow;
        return new ConfigDraft(
            DraftId: DraftId,
            CurrentStep: CurrentStep,
            MaxVisitedStep: MaxVisitedStep,
            StrategyName: string.IsNullOrWhiteSpace(StrategyName) ? null : StrategyName,
            StrategyType: StrategyType,
            TemplateId: TemplateId,
            SourceType: SourceType,
            Hypothesis: Hypothesis,
            ExpectedFailureMode: ExpectedFailureMode,
            DataConfig: DataFilePath is not null
                ? new DataConfig("csv",
                    BuildDataProviderOptions(),
                    Timeframe, BarsPerYear)
                : null,
            StrategyParameters: Parameters.Count > 0 ? new Dictionary<string, object>(Parameters) : null,
            ExecutionConfig: new ExecutionConfig(
                SlippageModelType, CommissionModelType,
                FillMode.NextBarOpen,
                RealismProfile,
                FillDelayBars: FillDelayBars),
            RiskConfig: new RiskConfig(
                new Dictionary<string, object>(), InitialCash, AnnualRiskFreeRate),
            PresetId: PresetId,
            PresetOverrides: PresetOverrides.Count > 0 ? new Dictionary<string, object>(PresetOverrides) : null,
            CreatedAt: now,
            UpdatedAt: now,
            StrategyId: StrategyId,
            StrategyVersionId: StrategyVersionId,
            SessionGuid: SessionGuid);
    }

    /// <summary>Builds a ScenarioConfig suitable for engine execution.</summary>
    public ScenarioConfig ToScenarioConfig()
    {
        var dataOpts = new Dictionary<string, object>();
        if (DataFilePath is not null) dataOpts["FilePath"] = DataFilePath;
        if (!string.IsNullOrEmpty(Symbol)) dataOpts["Symbol"] = Symbol;
        if (!string.IsNullOrEmpty(Interval)) dataOpts["Interval"] = Interval;

        return new ScenarioConfig(
            ScenarioId: $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            Description: $"{StrategyName} backtest",
            ReplayMode: TradingResearchEngine.Core.Engine.ReplayMode.Bar,
            DataProviderType: "csv",
            DataProviderOptions: dataOpts,
            StrategyType: StrategyType ?? "",
            StrategyParameters: new Dictionary<string, object>(Parameters),
            RiskParameters: new Dictionary<string, object>(),
            SlippageModelType: SlippageModelType,
            CommissionModelType: CommissionModelType,
            InitialCash: InitialCash,
            AnnualRiskFreeRate: AnnualRiskFreeRate,
            RandomSeed: null,
            ResearchWorkflowType: null,
            ResearchWorkflowOptions: null,
            PropFirmOptions: null,
            FillMode: FillMode.NextBarOpen,
            BarsPerYear: BarsPerYear,
            Timeframe: Timeframe,
            Execution: new ExecutionConfig(
                SlippageModelType, CommissionModelType,
                FillMode.NextBarOpen,
                RealismProfile,
                FillDelayBars: FillDelayBars));
    }

    /// <summary>Creates a StrategyVersion from the current builder state.</summary>
    public StrategyVersion ToStrategyVersion(string strategyId, int versionNumber)
    {
        return new StrategyVersion(
            StrategyVersionId: $"{strategyId}-v{versionNumber}",
            StrategyId: strategyId,
            VersionNumber: versionNumber,
            Parameters: new Dictionary<string, object>(Parameters),
            BaseScenarioConfig: ToScenarioConfig(),
            CreatedAt: DateTimeOffset.UtcNow,
            ChangeNote: versionNumber == 1 ? "Initial version" : null,
            SourceType: SourceType,
            SourceTemplateId: SourceType == SourceType.Template ? TemplateId : null,
            Hypothesis: Hypothesis,
            ExpectedFailureMode: ExpectedFailureMode);
    }

    /// <summary>Populates the ViewModel from a persisted ConfigDraft.</summary>
    public static BuilderViewModel FromDraft(ConfigDraft draft)
    {
        var vm = new BuilderViewModel
        {
            DraftId = draft.DraftId,
            CurrentStep = draft.CurrentStep,
            MaxVisitedStep = draft.MaxVisitedStep > 0 ? draft.MaxVisitedStep : draft.CurrentStep,
            StrategyName = draft.StrategyName ?? "",
            StrategyType = draft.StrategyType,
            TemplateId = draft.TemplateId,
            SourceType = draft.SourceType,
            Hypothesis = draft.Hypothesis,
            ExpectedFailureMode = draft.ExpectedFailureMode,
            PresetId = draft.PresetId,
            StrategyId = draft.StrategyId,
            StrategyVersionId = draft.StrategyVersionId,
            SessionGuid = draft.SessionGuid ?? Guid.NewGuid().ToString(),
        };

        if (draft.DataConfig is not null)
        {
            vm.Timeframe = draft.DataConfig.Timeframe ?? "Daily";
            if (draft.DataConfig.DataProviderOptions.TryGetValue("FilePath", out var fp))
                vm.DataFilePath = fp?.ToString();
            if (draft.DataConfig.DataProviderOptions.TryGetValue("Symbol", out var sym))
                vm.Symbol = sym?.ToString() ?? "";
            if (draft.DataConfig.DataProviderOptions.TryGetValue("Interval", out var intv))
                vm.Interval = intv?.ToString() ?? "1D";
        }

        if (draft.StrategyParameters is not null)
            vm.Parameters = new Dictionary<string, object>(draft.StrategyParameters);

        if (draft.ExecutionConfig is not null)
        {
            vm.SlippageModelType = draft.ExecutionConfig.SlippageModelType;
            vm.CommissionModelType = draft.ExecutionConfig.CommissionModelType;
            vm.FillDelayBars = draft.ExecutionConfig.FillDelayBars;
            vm.RealismProfile = draft.ExecutionConfig.RealismProfile;
        }

        if (draft.RiskConfig is not null)
        {
            vm.InitialCash = draft.RiskConfig.InitialCash;
            vm.AnnualRiskFreeRate = draft.RiskConfig.AnnualRiskFreeRate;
        }

        if (draft.PresetOverrides is not null)
            vm.PresetOverrides = new Dictionary<string, object>(draft.PresetOverrides);

        return vm;
    }
}
