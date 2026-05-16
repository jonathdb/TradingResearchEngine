namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Configuration options for the <c>PollingRestStreamingDataProvider</c>.
/// Bound from <c>appsettings.json:PollingProvider</c> via the <c>IOptions&lt;PollingProviderOptions&gt;</c> pattern.
/// </summary>
public sealed class PollingProviderOptions
{
    /// <summary>
    /// Configuration section name used for binding from <c>appsettings.json</c>.
    /// </summary>
    public const string SectionName = "PollingProvider";

    /// <summary>
    /// The interval between REST endpoint polls.
    /// Default: 1 minute.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The number of consecutive poll failures before a structured warning is emitted.
    /// Default: 5.
    /// </summary>
    public int ConsecutiveFailureWarningThreshold { get; set; } = 5;

    /// <summary>
    /// The REST endpoint URL to poll for bar data.
    /// When null or empty, the provider is considered unconfigured.
    /// </summary>
    public string? EndpointUrl { get; set; }
}
