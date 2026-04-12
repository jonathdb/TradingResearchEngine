namespace TradingResearchEngine.Application.Strategy;

/// <summary>
/// How a <see cref="StrategyVersion"/> was created. Used for traceability
/// and to distinguish template-based, forked, imported, and manual versions.
/// </summary>
public enum SourceType
{
    /// <summary>Created from a strategy template.</summary>
    Template,
    /// <summary>Imported from a JSON config file.</summary>
    Import,
    /// <summary>Forked from an existing strategy version.</summary>
    Fork,
    /// <summary>Created manually from a blank starting point.</summary>
    Manual
}
