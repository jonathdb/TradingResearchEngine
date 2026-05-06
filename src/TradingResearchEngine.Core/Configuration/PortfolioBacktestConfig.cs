namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Multi-symbol backtest configuration. First-class alternative to <see cref="ScenarioConfig"/>
/// for portfolio-level backtesting with diversification analysis.
/// Supports a single strategy applied to all symbols or one strategy per symbol.
/// </summary>
public sealed record PortfolioBacktestConfig(
    /// <summary>List of data source configurations, one per symbol in the portfolio.</summary>
    IReadOnlyList<DataConfig> Symbols,
    /// <summary>
    /// Strategy configurations. Either a single strategy applied to all symbols,
    /// or exactly one strategy per symbol (count must equal <see cref="Symbols"/> count).
    /// </summary>
    IReadOnlyList<StrategyConfig> Strategies,
    /// <summary>Portfolio-level risk constraints including heat limits and rebalancing mode.</summary>
    PortfolioRiskConfig PortfolioRisk,
    /// <summary>Execution realism settings shared across all symbol runs.</summary>
    ExecutionConfig Execution,
    /// <summary>Initial cash balance for the portfolio (default 100,000).</summary>
    decimal InitialCash = 100_000m,
    /// <summary>Optional random seed for deterministic replay across all symbol runs.</summary>
    int? Seed = null,
    /// <summary>Optional timeframe label override applied to all symbols (e.g. "Daily", "H4").</summary>
    string? Timeframe = null);
