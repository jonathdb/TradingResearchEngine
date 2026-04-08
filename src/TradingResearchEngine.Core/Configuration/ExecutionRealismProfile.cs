namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Execution realism level. Each profile configures defaults for fill mode,
/// slippage, spread handling, stop fill conservatism, and order expiry.
/// </summary>
public enum ExecutionRealismProfile
{
    /// <summary>SameBarClose fill, zero slippage, no partial fills. Maximum speed for parameter sweeps.</summary>
    FastResearch,

    /// <summary>NextBarOpen fill, fixed spread slippage, standard order expiry. Default for backtesting.</summary>
    StandardBacktest,

    /// <summary>NextBarOpen fill, ATR-scaled slippage, session-aware spread widening, pessimistic stop fills.</summary>
    BrokerConservative
}
