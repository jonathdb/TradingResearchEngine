namespace TradingResearchEngine.Core.Exceptions;

/// <summary>
/// Thrown when a <see cref="Configuration.ScenarioConfig"/> is incompatible with the
/// selected data provider or replay mode (e.g. tick mode with a bar-only provider).
/// </summary>
public sealed class ConfigurationException : Exception
{
    /// <inheritdoc/>
    public ConfigurationException(string message) : base(message) { }

    /// <inheritdoc/>
    public ConfigurationException(string message, Exception inner) : base(message, inner) { }
}
