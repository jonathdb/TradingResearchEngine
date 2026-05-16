namespace TradingResearchEngine.Application.Configuration;

/// <summary>
/// Configuration options for paper-trading polling behavior.
/// Bound from <c>appsettings.json:PaperTrading</c> via the <c>IOptions&lt;PaperTradingOptions&gt;</c> pattern.
/// Supports hot-reload via <c>IOptionsMonitor&lt;PaperTradingOptions&gt;</c>.
/// </summary>
public sealed class PaperTradingOptions
{
    /// <summary>
    /// Configuration section name used for binding from <c>appsettings.json</c>.
    /// </summary>
    public const string SectionName = "PaperTrading";

    /// <summary>
    /// The polling interval between emitted bars during paper-trading playback.
    /// Must be between <see cref="MinInterval"/> and <see cref="MaxInterval"/>.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Minimum allowed polling interval. Prevents excessively fast polling
    /// that could overwhelm downstream consumers.
    /// Default: 500 milliseconds.
    /// </summary>
    public TimeSpan MinInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Maximum allowed polling interval. Prevents excessively slow polling
    /// that would make paper-trading unresponsive.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan MaxInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Validates that the configured <see cref="PollingInterval"/> is within the
    /// acceptable bounds defined by <see cref="MinInterval"/> and <see cref="MaxInterval"/>.
    /// </summary>
    /// <returns>
    /// A list of validation error messages. Empty when the configuration is valid.
    /// </returns>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (PollingInterval <= TimeSpan.Zero)
            errors.Add("PollingInterval must be greater than zero.");

        if (MinInterval <= TimeSpan.Zero)
            errors.Add("MinInterval must be greater than zero.");

        if (MaxInterval <= TimeSpan.Zero)
            errors.Add("MaxInterval must be greater than zero.");

        if (MinInterval >= MaxInterval)
            errors.Add("MinInterval must be less than MaxInterval.");

        if (PollingInterval < MinInterval)
            errors.Add($"PollingInterval ({PollingInterval.TotalMilliseconds}ms) is below MinInterval ({MinInterval.TotalMilliseconds}ms).");

        if (PollingInterval > MaxInterval)
            errors.Add($"PollingInterval ({PollingInterval.TotalMilliseconds}ms) exceeds MaxInterval ({MaxInterval.TotalMilliseconds}ms).");

        return errors;
    }
}
