using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// Compares two <see cref="StrategyVersion"/> instances and produces a structured
/// <see cref="StrategyDiff"/> using resolved/effective values.
/// </summary>
public sealed class StrategyDiffService
{
    /// <summary>Material fields that affect simulation output.</summary>
    private static readonly HashSet<string> MaterialExecutionFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "SlippageModelType", "CommissionModelType", "FillMode", "RealismProfile"
    };

    /// <summary>Compares two versions and returns a structured diff.</summary>
    public StrategyDiff Compare(StrategyVersion a, StrategyVersion b)
    {
        var paramChanges = CompareParameters(a.Parameters, b.Parameters);
        var execChanges = CompareExecution(a.BaseScenarioConfig, b.BaseScenarioConfig);
        var dataChanges = CompareDataWindow(a.BaseScenarioConfig, b.BaseScenarioConfig);
        var riskChanges = CompareRisk(a.BaseScenarioConfig, b.BaseScenarioConfig);
        var realismChanges = CompareRealism(a.BaseScenarioConfig, b.BaseScenarioConfig);
        var stageChange = CompareField("Stage", "DevelopmentStage", a.ChangeNote, b.ChangeNote, ChangeSignificance.Cosmetic);
        var hypothesisChange = CompareField("Hypothesis", "Hypothesis", a.Hypothesis, b.Hypothesis, ChangeSignificance.Cosmetic);

        return new StrategyDiff(paramChanges, execChanges, dataChanges, riskChanges, realismChanges, stageChange, hypothesisChange);
    }

    private static List<FieldChange> CompareParameters(
        Dictionary<string, object> a, Dictionary<string, object> b)
    {
        var changes = new List<FieldChange>();
        var allKeys = new HashSet<string>(a.Keys.Concat(b.Keys), StringComparer.OrdinalIgnoreCase);

        foreach (var key in allKeys)
        {
            a.TryGetValue(key, out var va);
            b.TryGetValue(key, out var vb);
            if (!ValuesEqual(va, vb))
                changes.Add(new FieldChange("Parameters", key, va, vb, ChangeSignificance.Material));
        }
        return changes;
    }

    private static List<FieldChange> CompareExecution(ScenarioConfig a, ScenarioConfig b)
    {
        var changes = new List<FieldChange>();
        var ea = a.EffectiveExecutionConfig;
        var eb = b.EffectiveExecutionConfig;

        AddIfDifferent(changes, "Execution", "SlippageModelType", ea.SlippageModelType, eb.SlippageModelType);
        AddIfDifferent(changes, "Execution", "CommissionModelType", ea.CommissionModelType, eb.CommissionModelType);
        AddIfDifferent(changes, "Execution", "FillMode", ea.FillMode, eb.FillMode);
        AddIfDifferent(changes, "Execution", "RealismProfile", ea.RealismProfile, eb.RealismProfile);
        return changes;
    }

    private static List<FieldChange> CompareDataWindow(ScenarioConfig a, ScenarioConfig b)
    {
        var changes = new List<FieldChange>();
        var da = a.EffectiveDataConfig;
        var db = b.EffectiveDataConfig;

        AddIfDifferent(changes, "Data", "DataProviderType", da.DataProviderType, db.DataProviderType, ChangeSignificance.Material);
        AddIfDifferent(changes, "Data", "Timeframe", da.Timeframe, db.Timeframe, ChangeSignificance.Material);
        AddIfDifferent(changes, "Data", "BarsPerYear", da.BarsPerYear, db.BarsPerYear, ChangeSignificance.Material);
        return changes;
    }

    private static List<FieldChange> CompareRisk(ScenarioConfig a, ScenarioConfig b)
    {
        var changes = new List<FieldChange>();
        var ra = a.EffectiveRiskConfig;
        var rb = b.EffectiveRiskConfig;

        AddIfDifferent(changes, "Risk", "InitialCash", ra.InitialCash, rb.InitialCash, ChangeSignificance.Material);
        AddIfDifferent(changes, "Risk", "AnnualRiskFreeRate", ra.AnnualRiskFreeRate, rb.AnnualRiskFreeRate, ChangeSignificance.Minor);
        return changes;
    }

    private static List<FieldChange> CompareRealism(ScenarioConfig a, ScenarioConfig b)
    {
        var changes = new List<FieldChange>();
        var ea = a.EffectiveExecutionConfig;
        var eb = b.EffectiveExecutionConfig;

        AddIfDifferent(changes, "Realism", "SessionCalendarType", ea.SessionOptions?.SessionCalendarType, eb.SessionOptions?.SessionCalendarType);
        return changes;
    }

    private static void AddIfDifferent<T>(List<FieldChange> changes, string section, string field, T? a, T? b, ChangeSignificance? overrideSignificance = null)
    {
        if (Equals(a, b)) return;
        var sig = overrideSignificance ?? (MaterialExecutionFields.Contains(field) ? ChangeSignificance.Material : ChangeSignificance.Minor);
        changes.Add(new FieldChange(section, field, a, b, sig));
    }

    private static FieldChange? CompareField(string section, string field, object? a, object? b, ChangeSignificance significance)
    {
        if (ValuesEqual(a, b)) return null;
        return new FieldChange(section, field, a, b, significance);
    }

    private static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.ToString() == b.ToString();
    }
}
