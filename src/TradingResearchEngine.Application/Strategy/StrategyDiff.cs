namespace TradingResearchEngine.Application.Strategy;

/// <summary>Whether a field change affects simulation output.</summary>
public enum ChangeSignificance
{
    /// <summary>Display-only change (e.g. ChangeNote, Hypothesis).</summary>
    Cosmetic,
    /// <summary>Minor change that may affect results marginally.</summary>
    Minor,
    /// <summary>Change that materially affects simulation output.</summary>
    Material
}

/// <summary>A single field change between two strategy versions.</summary>
/// <param name="Section">The config section (Parameters, Execution, Data, Risk, Realism).</param>
/// <param name="FieldName">The field that changed.</param>
/// <param name="OldValue">Value in version A.</param>
/// <param name="NewValue">Value in version B.</param>
/// <param name="Significance">How significant the change is.</param>
public sealed record FieldChange(
    string Section,
    string FieldName,
    object? OldValue,
    object? NewValue,
    ChangeSignificance Significance);

/// <summary>Complete diff between two strategy versions.</summary>
public sealed record StrategyDiff(
    IReadOnlyList<FieldChange> ParameterChanges,
    IReadOnlyList<FieldChange> ExecutionChanges,
    IReadOnlyList<FieldChange> DataWindowChanges,
    IReadOnlyList<FieldChange> RiskChanges,
    IReadOnlyList<FieldChange> RealismChanges,
    FieldChange? StageChange,
    FieldChange? HypothesisChange);
