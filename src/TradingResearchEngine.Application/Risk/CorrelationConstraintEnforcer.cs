using Microsoft.Extensions.Logging;
using TradingResearchEngine.Core.Portfolio;

namespace TradingResearchEngine.Application.Risk;

/// <summary>
/// Evaluates whether a candidate position violates pairwise correlation constraints
/// against existing open positions. Used by <see cref="DefaultRiskLayer"/> to reject
/// or defer orders that would breach the configured <c>MaxPairwiseCorrelation</c> threshold.
/// </summary>
public sealed class CorrelationConstraintEnforcer
{
    private readonly ICorrelationDataProvider _correlationProvider;
    private readonly ILogger<CorrelationConstraintEnforcer> _logger;

    /// <summary>
    /// Initialises the enforcer with a correlation data provider and logger.
    /// </summary>
    /// <param name="correlationProvider">Provides pairwise correlation values between symbols.</param>
    /// <param name="logger">Logger for structured diagnostics.</param>
    public CorrelationConstraintEnforcer(
        ICorrelationDataProvider correlationProvider,
        ILogger<CorrelationConstraintEnforcer> logger)
    {
        _correlationProvider = correlationProvider;
        _logger = logger;
    }

    /// <summary>
    /// Evaluates whether adding a position in <paramref name="candidateSymbol"/> would violate
    /// the maximum pairwise correlation constraint against any existing open position.
    /// </summary>
    /// <param name="candidateSymbol">The symbol of the candidate position.</param>
    /// <param name="existingPositions">Currently open positions in the portfolio.</param>
    /// <param name="maxPairwiseCorrelation">Maximum allowed absolute pairwise correlation.</param>
    /// <param name="lookbackBars">Number of historical bars used for correlation computation.</param>
    /// <returns>A <see cref="CorrelationConstraintResult"/> indicating whether the position is allowed or rejected.</returns>
    public CorrelationConstraintResult Evaluate(
        string candidateSymbol,
        IReadOnlyDictionary<string, Position> existingPositions,
        decimal maxPairwiseCorrelation,
        int lookbackBars)
    {
        if (existingPositions.Count == 0)
            return CorrelationConstraintResult.Allowed();

        foreach (var (symbol, _) in existingPositions)
        {
            if (string.Equals(symbol, candidateSymbol, StringComparison.OrdinalIgnoreCase))
                continue;

            decimal correlation = _correlationProvider.GetPairwiseCorrelation(
                candidateSymbol, symbol, lookbackBars);

            if (Math.Abs(correlation) > maxPairwiseCorrelation)
            {
                string reason = $"Correlation {correlation:F4} between '{candidateSymbol}' and '{symbol}' " +
                                $"exceeds maximum allowed {maxPairwiseCorrelation:F4} (lookback: {lookbackBars} bars)";

                _logger.LogWarning(
                    "RiskRejection: Correlation constraint violated — {Reason}",
                    reason);

                return CorrelationConstraintResult.Rejected(reason, candidateSymbol, symbol, correlation);
            }
        }

        return CorrelationConstraintResult.Allowed();
    }
}

/// <summary>
/// Result of a correlation constraint evaluation.
/// </summary>
public sealed record CorrelationConstraintResult
{
    /// <summary>Whether the candidate position is allowed.</summary>
    public bool IsAllowed { get; }

    /// <summary>Reason for rejection, or null if allowed.</summary>
    public string? Reason { get; }

    /// <summary>The candidate symbol that was evaluated.</summary>
    public string? CandidateSymbol { get; }

    /// <summary>The existing symbol that caused the violation.</summary>
    public string? ViolatingSymbol { get; }

    /// <summary>The computed correlation value that caused the violation.</summary>
    public decimal? CorrelationValue { get; }

    private CorrelationConstraintResult(
        bool isAllowed,
        string? reason = null,
        string? candidateSymbol = null,
        string? violatingSymbol = null,
        decimal? correlationValue = null)
    {
        IsAllowed = isAllowed;
        Reason = reason;
        CandidateSymbol = candidateSymbol;
        ViolatingSymbol = violatingSymbol;
        CorrelationValue = correlationValue;
    }

    /// <summary>Creates an allowed result.</summary>
    public static CorrelationConstraintResult Allowed() => new(true);

    /// <summary>Creates a rejected result with diagnostic details.</summary>
    public static CorrelationConstraintResult Rejected(
        string reason,
        string candidateSymbol,
        string violatingSymbol,
        decimal correlationValue) =>
        new(false, reason, candidateSymbol, violatingSymbol, correlationValue);
}

/// <summary>
/// Provides pairwise correlation data between symbols for use by the
/// <see cref="CorrelationConstraintEnforcer"/>.
/// </summary>
public interface ICorrelationDataProvider
{
    /// <summary>
    /// Returns the Pearson correlation coefficient between two symbols
    /// computed over the specified lookback period.
    /// Returns 0 when insufficient data is available.
    /// </summary>
    /// <param name="symbolA">First symbol.</param>
    /// <param name="symbolB">Second symbol.</param>
    /// <param name="lookbackBars">Number of historical bars for the computation.</param>
    /// <returns>Correlation coefficient in the range [-1, 1].</returns>
    decimal GetPairwiseCorrelation(string symbolA, string symbolB, int lookbackBars);
}
