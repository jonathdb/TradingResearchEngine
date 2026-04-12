namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Data provider settings sub-object for <see cref="ScenarioConfig"/> decomposition.
/// Groups data source type, provider options, timeframe, and annualisation factor.
/// </summary>
public sealed record DataConfig(
    /// <summary>The data provider implementation key (e.g. "csv", "http").</summary>
    string DataProviderType,
    /// <summary>Provider-specific options (e.g. file path, URL, symbol).</summary>
    Dictionary<string, object> DataProviderOptions,
    /// <summary>Explicit timeframe label (e.g. "Daily", "H4"). Null for legacy configs.</summary>
    string? Timeframe = null,
    /// <summary>Bars per year for Sharpe/Sortino annualisation. Daily=252, H4=1512, H1=6048, M15=24192.</summary>
    int BarsPerYear = 252);
