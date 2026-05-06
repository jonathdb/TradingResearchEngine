namespace TradingResearchEngine.Core.PaperTrading;

/// <summary>
/// Represents the lifecycle states of a paper trading session.
/// </summary>
public enum PaperTradingStatus
{
    /// <summary>Session created but not yet started.</summary>
    Idle,

    /// <summary>Session is establishing a connection to the data stream.</summary>
    Connecting,

    /// <summary>Session is actively processing bars and generating trades.</summary>
    Running,

    /// <summary>Session is paused; portfolio state is preserved but no bars are consumed.</summary>
    Paused,

    /// <summary>Session has been stopped and final results are available.</summary>
    Stopped,

    /// <summary>Session encountered an unrecoverable error.</summary>
    Error
}
