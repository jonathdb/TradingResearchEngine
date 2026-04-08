using TradingResearchEngine.Core.Events;
using TradingResearchEngine.Core.Execution;
using TradingResearchEngine.Core.Sessions;

namespace TradingResearchEngine.Application.Execution;

/// <summary>
/// Widens slippage during illiquid sessions and narrows it during core sessions.
/// Wraps a base slippage model and applies a session-dependent multiplier.
/// Deterministic given the same inputs.
/// </summary>
public sealed class SessionAwareSlippageModel : ISlippageModel
{
    private readonly ISlippageModel _baseModel;
    private readonly ISessionCalendar _calendar;
    private readonly decimal _coreMultiplier;
    private readonly decimal _offHoursMultiplier;
    private readonly HashSet<string> _coreSessions;

    /// <param name="baseModel">Underlying slippage model to scale.</param>
    /// <param name="calendar">Session calendar for classification.</param>
    /// <param name="coreMultiplier">Multiplier during core sessions (default 1.0).</param>
    /// <param name="offHoursMultiplier">Multiplier during off-hours (default 2.0).</param>
    /// <param name="coreSessions">Session names considered "core" (default: Regular, London, NewYork, Overlap).</param>
    public SessionAwareSlippageModel(
        ISlippageModel baseModel,
        ISessionCalendar calendar,
        decimal coreMultiplier = 1.0m,
        decimal offHoursMultiplier = 2.0m,
        IEnumerable<string>? coreSessions = null)
    {
        _baseModel = baseModel;
        _calendar = calendar;
        _coreMultiplier = coreMultiplier;
        _offHoursMultiplier = offHoursMultiplier;
        _coreSessions = new HashSet<string>(
            coreSessions ?? new[] { "Regular", "London", "NewYork", "Overlap" },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public decimal ComputeAdjustment(OrderEvent order, MarketDataEvent market)
    {
        decimal baseSlippage = _baseModel.ComputeAdjustment(order, market);
        string session = _calendar.ClassifySession(market.Timestamp);
        decimal multiplier = _coreSessions.Contains(session) ? _coreMultiplier : _offHoursMultiplier;
        return baseSlippage * multiplier;
    }
}
