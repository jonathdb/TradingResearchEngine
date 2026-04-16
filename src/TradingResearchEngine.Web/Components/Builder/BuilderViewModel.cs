using TradingResearchEngine.Application.Strategy;
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

    // Navigation
    public int CurrentStep { get; set; } = 1;
    public bool IsDirty { get; set; }
    public string DraftId { get; set; } = Guid.NewGuid().ToString();

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
                ExecutionRealismProfile.StandardBacktest),
            RiskConfig: new RiskConfig(
                new Dictionary<string, object>(), InitialCash, AnnualRiskFreeRate),
            PresetId: PresetId,
            PresetOverrides: PresetOverrides.Count > 0 ? new Dictionary<string, object>(PresetOverrides) : null,
            CreatedAt: now,
            UpdatedAt: now);
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
            Timeframe: Timeframe);
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
            StrategyName = draft.StrategyName ?? "",
            StrategyType = draft.StrategyType,
            TemplateId = draft.TemplateId,
            SourceType = draft.SourceType,
            Hypothesis = draft.Hypothesis,
            ExpectedFailureMode = draft.ExpectedFailureMode,
            PresetId = draft.PresetId,
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
