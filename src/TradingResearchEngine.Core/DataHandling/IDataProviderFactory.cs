using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Core.DataHandling;

/// <summary>
/// Factory that creates an <see cref="IDataProvider"/> from a provider type name and options dictionary.
/// Defined in Core so Application can resolve providers without referencing Infrastructure.
/// </summary>
public interface IDataProviderFactory
{
    /// <summary>Creates a data provider based on the type name and options from ScenarioConfig.</summary>
    IDataProvider Create(string providerType, Dictionary<string, object> options);

    /// <summary>
    /// Creates a data provider from a strongly-typed <see cref="DataProviderConfig"/>.
    /// Default implementation converts to dictionary and delegates to the legacy overload.
    /// </summary>
    /// <param name="config">The typed data provider configuration.</param>
    /// <returns>A configured <see cref="IDataProvider"/> instance.</returns>
    IDataProvider Create(DataProviderConfig config) =>
        Create(config.ProviderType, DataProviderConfigAdapter.ToDictionary(config));
}
