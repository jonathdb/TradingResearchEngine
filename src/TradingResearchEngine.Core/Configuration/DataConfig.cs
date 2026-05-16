using System.Text.Json.Serialization;

namespace TradingResearchEngine.Core.Configuration;

/// <summary>
/// Data provider settings sub-object for <see cref="ScenarioConfig"/> decomposition.
/// Groups data source type, provider options, timeframe, and annualisation factor.
/// </summary>
public sealed record DataConfig(
    /// <summary>The data provider implementation key (e.g. "csv", "http").</summary>
    string DataProviderType,
    /// <summary>
    /// Legacy provider-specific options dictionary. Retained for backward compatibility
    /// with existing JSON configuration files. New code should prefer <see cref="TypedProviderConfig"/>.
    /// </summary>
    [property: Obsolete("Use TypedProviderConfig for compile-time safety. Dictionary retained for JSON backward compatibility.")]
    Dictionary<string, object> DataProviderOptions,
    /// <summary>Explicit timeframe label (e.g. "Daily", "H4"). Null for legacy configs.</summary>
    string? Timeframe = null,
    /// <summary>Bars per year for Sharpe/Sortino annualisation. Daily=252, H4=1512, H1=6048, M15=24192.</summary>
    int BarsPerYear = 252,
    /// <summary>
    /// Strongly-typed data provider configuration. When present, takes precedence over
    /// the legacy <see cref="DataProviderOptions"/> dictionary. Supports polymorphic
    /// deserialization via <see cref="DataProviderConfigConverter"/>.
    /// </summary>
    DataProviderConfig? TypedProviderConfig = null)
{
    /// <summary>
    /// Gets the effective typed provider configuration, converting from the legacy dictionary
    /// if <see cref="TypedProviderConfig"/> is not explicitly set.
    /// </summary>
    [JsonIgnore]
    public DataProviderConfig EffectiveTypedConfig =>
#pragma warning disable CS0618 // Accessing obsolete DataProviderOptions for backward compatibility conversion
        TypedProviderConfig ?? DataProviderConfigAdapter.FromDictionary(DataProviderType, DataProviderOptions);
#pragma warning restore CS0618
}
