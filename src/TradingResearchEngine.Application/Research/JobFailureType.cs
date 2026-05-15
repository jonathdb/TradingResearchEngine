namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Classifies the nature of a job failure for diagnostics and retry decisions.
/// </summary>
public enum JobFailureType
{
    /// <summary>
    /// A transient failure (network timeout, temporary unavailability, I/O error)
    /// that may succeed on retry.
    /// </summary>
    Transient,

    /// <summary>
    /// A terminal failure (invalid configuration, missing data, unsupported job type)
    /// that will not succeed on retry.
    /// </summary>
    Terminal
}
