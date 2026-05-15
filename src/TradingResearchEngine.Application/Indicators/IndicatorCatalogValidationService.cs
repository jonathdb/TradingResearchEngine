using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingResearchEngine.Application.Indicators;

/// <summary>
/// Hosted service that validates all indicator catalog entries at application startup.
/// Iterates all entries, invokes each factory with default parameters, and logs warnings
/// for any that fail or return null. Runs once during startup and then completes.
/// </summary>
public sealed class IndicatorCatalogValidationService : IHostedService
{
    private readonly ILogger<IndicatorCatalogValidationService> _logger;

    /// <summary>
    /// Creates a new instance of the indicator catalog validation service.
    /// </summary>
    /// <param name="logger">Logger for reporting validation results.</param>
    public IndicatorCatalogValidationService(ILogger<IndicatorCatalogValidationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting indicator catalog validation...");
        SkenderIndicatorCatalog.ValidateAll(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
