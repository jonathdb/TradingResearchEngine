namespace TradingResearchEngine.Core.DataHandling;

/// <summary>
/// Factory that creates an <see cref="IDataProvider"/> from a provider type name and options dictionary.
/// Defined in Core so Application can resolve providers without referencing Infrastructure.
/// </summary>
public interface IDataProviderFactory
{
    /// <summary>Creates a data provider based on the type name and options from ScenarioConfig.</summary>
    IDataProvider Create(string providerType, Dictionary<string, object> options);
}
