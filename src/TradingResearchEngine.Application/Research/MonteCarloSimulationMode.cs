namespace TradingResearchEngine.Application.Research;

/// <summary>
/// Specifies the statistical approach used by the Monte Carlo simulation workflow.
/// Each mode provides a different perspective on path variability and robustness.
/// </summary>
public enum MonteCarloSimulationMode
{
    /// <summary>
    /// IID bootstrap of individual trade returns. Each simulated path draws trades
    /// independently with replacement from the original trade sequence.
    /// Best for strategies with uncorrelated trade outcomes.
    /// </summary>
    TradeResample,

    /// <summary>
    /// Block bootstrap resampling of trade returns. Contiguous blocks of trades are
    /// sampled together to preserve serial autocorrelation in the return sequence.
    /// Best for trend-following strategies where consecutive trade outcomes are correlated.
    /// Block size is controlled by <see cref="Configuration.MonteCarloOptions.BlockSize"/>.
    /// </summary>
    BlockBootstrap,

    /// <summary>
    /// Resamples the equity curve's period returns directly rather than trade-level returns.
    /// Each simulated path draws bar-level returns with replacement from the original equity curve,
    /// providing a different statistical perspective on path variability that captures
    /// intra-trade equity fluctuations and time-in-market effects.
    /// </summary>
    ReturnSeries
}
